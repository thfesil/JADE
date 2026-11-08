using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jade.Server.Ldap;

namespace Jade.Server
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            PrintBanner();

            if (args.Any(a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                return await RunNetworkAndCryptoSelfTestAsync().ConfigureAwait(false);
            }

            string dataDir = GetArgValue(args, "--data-dir") ?? Path.Combine(AppContext.BaseDirectory, "jade_dc_data");
            int ldapPort = int.TryParse(GetArgValue(args, "--ldap-port"), out int p1) ? p1 : 3890;
            int ldapsPort = int.TryParse(GetArgValue(args, "--ldaps-port"), out int p2) ? p2 : 6360;

            var options = new JadeDomainControllerOptions
            {
                DataDirectory = dataDir,
                LdapPort = ldapPort,
                LdapsPort = ldapsPort,
                RequireTlsForPasswordOperations = true,
                Logger = Console.WriteLine
            };

            await using var server = new JadeDomainControllerServer(options);
            server.Start();

            Console.WriteLine();
            Console.WriteLine("=======================================================================");
            Console.WriteLine("  CONTROLADOR DE DOMINIO JADE OPERACIONAL");
            Console.WriteLine($"  Dominio Base (Root DN) : {options.DomainDn}");
            Console.WriteLine($"  Hostname DNS           : {options.DnsHostName}");
            Console.WriteLine($"  Porta LDAP (StartTLS)  : {server.ActiveLdapPort}");
            Console.WriteLine($"  Porta LDAPS (TLS 1.3)  : {server.ActiveLdapsPort}");
            Console.WriteLine($"  Certificado X.509      : {server.ServerCertificate.Subject}");
            Console.WriteLine($"  Thumbprint SHA-1       : {server.ServerCertificate.Thumbprint}");
            Console.WriteLine($"  Base NTDS.DIT          : {Path.Combine(options.DataDirectory, options.DatabaseName)}");
            Console.WriteLine("=======================================================================");
            Console.WriteLine("Comandos interativos disponiveis:");
            Console.WriteLine("  [R] Alternar Simulacao de Esgotamento de RIDs (Win32 8209)");
            Console.WriteLine("  [S] Exibir Status e Estatisticas do Controlador de Dominio");
            Console.WriteLine("  [Q] Encerrar Servidor de forma limpa");
            Console.WriteLine("=======================================================================");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            while (!cts.IsCancellationRequested)
            {
                if (!Console.IsInputRedirected && Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true).Key;
                    if (key == ConsoleKey.Q)
                    {
                        break;
                    }
                    if (key == ConsoleKey.R)
                    {
                        server.SetRidExhaustionSimulation(!server.IsRidExhaustionSimulated);
                    }
                    if (key == ConsoleKey.S)
                    {
                        uint count = server.Database.CountObjects();
                        Console.WriteLine($"[STATUS] Objetos no NTDS.DIT: {count} | Conexoes Ativas: {server.ActiveConnectionCount} | Esgotamento RID (8209): {(server.IsRidExhaustionSimulated ? "ATIVO" : "INATIVO")}");
                    }
                }

                await Task.Delay(200).ConfigureAwait(false);
            }

            Console.WriteLine("Encerrando servicos LDAP/LDAPS e comitando transacoes ESE...");
            return 0;
        }

        private static async Task<int> RunNetworkAndCryptoSelfTestAsync()
        {
            string tempDir = Path.Combine(AppContext.BaseDirectory, "self_test_dc", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                Console.WriteLine("Iniciando Auto-Teste de Rede, Protocolo LDAP v3 e Criptografia TLS do Jade DC...\n");

                var options = new JadeDomainControllerOptions
                {
                    DataDirectory = tempDir,
                    LdapPort = 0,  /* Porta efemera automatica */
                    LdapsPort = 0, /* Porta efemera automatica */
                    RequireTlsForPasswordOperations = true,
                    Logger = msg => Console.WriteLine($"  {msg}")
                };

                await using (var server = new JadeDomainControllerServer(options))
                {
                    server.Start();

                    /* Etapa 1: Descoberta RootDSE e StartTLS na porta LDAP */
                    Console.WriteLine("\n[1/8] Testando Conexao LDAP, Descoberta RootDSE e Upgrade StartTLS...");
                    await using (var plainClient = new JadeLdapClient())
                    {
                        await plainClient.ConnectAsync("127.0.0.1", server.ActiveLdapPort, useImplicitLdaps: false);
                        var rootDse = await plainClient.QueryRootDseAsync();
                        if (rootDse == null || rootDse.GetFirstValue("defaultNamingContext") != "DC=jade,DC=local")
                        {
                            Console.WriteLine("FALHA: RootDSE nao retornou o contexto de nomenclatura esperado.");
                            return 1;
                        }
                        Console.WriteLine($"      RootDSE confirmado: defaultNamingContext={rootDse.GetFirstValue("defaultNamingContext")}, dnsHostName={rootDse.GetFirstValue("dnsHostName")}");

                        /* Validando bloqueio de operacao de senha sem TLS */
                        var blockedAdd = await plainClient.AddUserAsync(
                            "CN=bloqueado.tls,CN=Users,DC=jade,DC=local",
                            "bloqueado.tls",
                            "bloqueado.tls@jade.local",
                            "SenhaSemTls#2026");

                        if (blockedAdd.ResultCode != LdapResultCode.UnwillingToPerform)
                        {
                            Console.WriteLine("FALHA: O servidor permitiu definir unicodePwd sem criptografia TLS!");
                            return 1;
                        }
                        Console.WriteLine($"      Seguranca confirmada: Definicao de senha sem TLS rejeitada com {blockedAdd.ResultCode} (DSID-0C090446).");

                        /* Elevando conexao via StartTLS */
                        var startTlsResp = await plainClient.StartTlsAsync("dc01.jade.local");
                        if (!startTlsResp.IsSuccess || !plainClient.IsTlsEncrypted)
                        {
                            Console.WriteLine("FALHA ao negociar StartTLS.");
                            return 1;
                        }
                        Console.WriteLine("      Upgrade StartTLS negociado com sucesso.");
                    }

                    /* Etapa 2: Conexao direta LDAPS (Porta 636 implicita) e Bind Administrativo */
                    Console.WriteLine("\n[2/8] Testando Canal Criptografado LDAPS (TLS) e Autenticacao Administrator...");
                    await using var ldapsClient = new JadeLdapClient();
                    await ldapsClient.ConnectAsync("127.0.0.1", server.ActiveLdapsPort, useImplicitLdaps: true);

                    var adminBind = await ldapsClient.BindAsync("CN=Administrator,CN=Users,DC=jade,DC=local", options.DefaultAdminPassword);
                    if (!adminBind.IsSuccess)
                    {
                        Console.WriteLine($"FALHA no Bind do Administrator: {adminBind.DiagnosticMessage}");
                        return 1;
                    }
                    Console.WriteLine("      Bind Administrativo autenticado com sucesso sob canal LDAPS.");

                    /* Etapa 3: Provisionamento de Conta via AddRequest com unicodePwd UTF-16LE entre aspas */
                    Console.WriteLine("\n[3/8] Provisionando Conta Corporativa via LDAP AddRequest (unicodePwd UTF-16LE)...");
                    string userDn = "CN=especialista.iam,CN=Users,DC=jade,DC=local";
                    var addResp = await ldapsClient.AddUserAsync(
                        userDn,
                        "especialista.iam",
                        "especialista.iam@jade.local",
                        "SenhaInicial@2026!",
                        userAccountControl: 512);

                    if (!addResp.IsSuccess)
                    {
                        Console.WriteLine($"FALHA no AddRequest: {addResp.DiagnosticMessage}");
                        return 1;
                    }

                    var searchedUser = await ldapsClient.SearchBySamAccountNameAsync("DC=jade,DC=local", "especialista.iam");
                    if (searchedUser == null)
                    {
                        Console.WriteLine("FALHA: Usuario criado nao foi localizado via SearchRequest.");
                        return 1;
                    }
                    Console.WriteLine($"      Conta localizada no LDAP: DN='{searchedUser.DistinguishedName}', SID='{searchedUser.GetFirstValue("objectSidString")}'");

                    /* Etapa 4: Autenticacao do Usuario Criado e Teste de Senha Incorreta (data 52e) */
                    Console.WriteLine("\n[4/8] Validando Autenticacao (BindRequest) e Rejeicao de Credencial Invalida (data 52e)...");
                    var userBindOk = await ldapsClient.BindAsync("especialista.iam@jade.local", "SenhaInicial@2026!");
                    if (!userBindOk.IsSuccess)
                    {
                        Console.WriteLine("FALHA: Usuario nao conseguiu autenticar com a senha correta.");
                        return 1;
                    }

                    var userBindBad = await ldapsClient.BindAsync("especialista.iam@jade.local", "SenhaIncorreta999");
                    if (userBindBad.ResultCode != LdapResultCode.InvalidCredentials || !userBindBad.DiagnosticMessage.Contains("data 52e"))
                    {
                        Console.WriteLine("FALHA: Senha incorreta nao retornou erro AD 49 (data 52e).");
                        return 1;
                    }
                    Console.WriteLine("      Autenticacao valida aceita e senha incorreta rejeitada com subcodigo AD 'data 52e'.");

                    /* Etapa 5: Desativacao da Conta via ModifyRequest (UAC 514) e Bloqueio de Logon (data 533) */
                    Console.WriteLine("\n[5/8] Testando Desativacao de Conta (UAC 514) e Rejeicao de Logon (data 533)...");
                    var modUacResp = await ldapsClient.ModifyUserAccountControlAsync(userDn, 514);
                    if (!modUacResp.IsSuccess)
                    {
                        Console.WriteLine("FALHA ao modificar userAccountControl para 514.");
                        return 1;
                    }

                    var disabledBind = await ldapsClient.BindAsync("especialista.iam@jade.local", "SenhaInicial@2026!");
                    if (disabledBind.ResultCode != LdapResultCode.InvalidCredentials || !disabledBind.DiagnosticMessage.Contains("data 533"))
                    {
                        Console.WriteLine("FALHA: Conta desativada nao retornou subcodigo AD 'data 533'.");
                        return 1;
                    }
                    Console.WriteLine("      Conta desativada bloqueada no Bind com subcodigo oficial AD 'data 533'.");

                    /* Etapa 6: Reativacao (UAC 512) e Redefinicao Criptografica de Senha */
                    Console.WriteLine("\n[6/8] Reativando Conta (UAC 512) e Redefinindo Senha via ModifyRequest...");
                    await ldapsClient.ModifyUserAccountControlAsync(userDn, 512);
                    var modPwdResp = await ldapsClient.ModifyPasswordAsync(userDn, "NovaSenha Segura#2026");
                    if (!modPwdResp.IsSuccess)
                    {
                        Console.WriteLine("FALHA ao redefinir senha via ModifyRequest.");
                        return 1;
                    }

                    var newPwdBind = await ldapsClient.BindAsync("especialista.iam", "NovaSenha Segura#2026");
                    if (!newPwdBind.IsSuccess)
                    {
                        Console.WriteLine("FALHA ao autenticar com a nova senha redefinida.");
                        return 1;
                    }
                    Console.WriteLine("      Nova senha validada com exito.");

                    /* Etapa 7: Simulacao de Esgotamento de RIDs (Win32 8209) via Rede */
                    Console.WriteLine("\n[7/8] Testando Esgotamento do FSMO RID Master sobre LDAP (Erro 8209 / WILL_NOT_PERFORM)...");
                    server.SetRidExhaustionSimulation(true);
                    var exhaustedResp = await ldapsClient.AddUserAsync(
                        "CN=falha.rid,CN=Users,DC=jade,DC=local",
                        "falha.rid",
                        "falha.rid@jade.local",
                        "Senha#2026");

                    if (exhaustedResp.ResultCode != LdapResultCode.UnwillingToPerform || !exhaustedResp.DiagnosticMessage.Contains("8209"))
                    {
                        Console.WriteLine("FALHA: Esgotamento de RIDs nao retornou LDAP_UNWILLING_TO_PERFORM (8209).");
                        return 1;
                    }
                    Console.WriteLine($"      Injecao de falha interceptada via protocolo LDAP: {exhaustedResp.DiagnosticMessage}");
                    server.SetRidExhaustionSimulation(false);

                    /* Etapa 8: Exclusao Tombstone via DelRequest */
                    Console.WriteLine("\n[8/8] Testando Exclusao Logica Tombstone via LDAP DelRequest...");
                    var delResp = await ldapsClient.DeleteObjectAsync(userDn);
                    if (!delResp.IsSuccess)
                    {
                        Console.WriteLine("FALHA ao excluir objeto via DelRequest.");
                        return 1;
                    }

                    var deletedSearch = await ldapsClient.SearchBySamAccountNameAsync("DC=jade,DC=local", "especialista.iam");
                    if (deletedSearch != null)
                    {
                        Console.WriteLine("FALHA: Objeto excluido continuou visivel em busca normal.");
                        return 1;
                    }
                    Console.WriteLine("      Objeto movido para Tombstone e removido das buscas ativas.");
                }

                Console.WriteLine("\n=======================================================================");
                Console.WriteLine("  STATUS: TODOS OS 8 TESTES DE REDE E CRIPTOGRAFIA DO DC APROVADOS");
                Console.WriteLine("=======================================================================");
                return 0;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static string? GetArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static void PrintBanner()
        {
            Console.WriteLine("=======================================================================");
            Console.WriteLine("  JADE SERVER: EMULATED ACTIVE DIRECTORY DOMAIN CONTROLLER");
            Console.WriteLine("  Storage: Windows Jet Blue ESE (esent.dll) | Protocol: LDAPv3 / LDAPS");
            Console.WriteLine("=======================================================================");
        }
    }
}
