using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jade.Core;
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
            PrintHelp();

            await RunInteractiveConsoleLoopAsync(server, options).ConfigureAwait(false);

            Console.WriteLine("Encerrando servicos LDAP/LDAPS e comitando transacoes ESE...");
            return 0;
        }

        private static async Task RunInteractiveConsoleLoopAsync(
            JadeDomainControllerServer server,
            JadeDomainControllerOptions options)
        {
            while (true)
            {
                Console.Write("\nJADE-DC> ");
                string? line = await Task.Run(() => Console.ReadLine()).ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                string trimmed = line.Trim().Trim('\uFEFF');
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                string[] parts = SplitCommandLine(trimmed);
                if (parts.Length == 0)
                {
                    continue;
                }

                string cmd = parts[0].ToLowerInvariant();

                try
                {
                    if (cmd == "quit" || cmd == "exit" || cmd == "q")
                    {
                        break;
                    }
                    else if (cmd == "help" || cmd == "?")
                    {
                        PrintHelp();
                    }
                    else if (cmd == "status" || cmd == "s")
                    {
                        PrintServerStatus(server, options);
                    }
                    else if (cmd == "rid")
                    {
                        HandleRidCommand(server, parts);
                    }
                    else if (cmd == "user")
                    {
                        HandleUserCommand(server, options, parts);
                    }
                    else
                    {
                        Console.WriteLine($"Comando desconhecido: '{parts[0]}'. Digite 'help' para listar os comandos.");
                    }
                }
                catch (JadeException jex)
                {
                    Console.WriteLine($"[ERRO MOTOR JADE] Codigo={jex.ErrorCode}: {jex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO] {ex.Message}");
                }
            }
        }

        private static void HandleUserCommand(
            JadeDomainControllerServer server,
            JadeDomainControllerOptions options,
            string[] parts)
        {
            if (parts.Length < 2)
            {
                Console.WriteLine("Uso: user <create|list|show|enable|disable|passwd|role|auth|delete> [argumentos]");
                return;
            }

            string sub = parts[1].ToLowerInvariant();
            string domainSuffix = options.DnsHostName.Contains('.')
                ? options.DnsHostName.Substring(options.DnsHostName.IndexOf('.') + 1)
                : "jade.local";

            switch (sub)
            {
                case "create":
                {
                    if (parts.Length < 4)
                    {
                        Console.WriteLine("Uso: user create <sAMAccountName> <senha> [DomainUser|SecurityAuditor|AccountOperator|DomainAdmin]");
                        return;
                    }

                    string sam = parts[2];
                    string password = parts[3];
                    JadeAccessLevel level = JadeAccessLevel.DomainUser;

                    if (parts.Length >= 5)
                    {
                        if (!TryParseAccessLevel(parts[4], out level))
                        {
                            Console.WriteLine($"Nivel de acesso invalido: '{parts[4]}'. Valores permitidos: DomainUser, SecurityAuditor, AccountOperator, DomainAdmin.");
                            return;
                        }
                    }

                    var entry = new JadeDirectoryEntry
                    {
                        Pdnt = 2, /* CN=Users */
                        ObjectClass = JadeObjectClass.User,
                        SamAccountName = sam,
                        UserPrincipalName = $"{sam}@{domainSuffix}",
                        UserAccountControl = UserAccountControl.NormalAccount,
                        AccessLevel = level
                    };

                    uint dnt = server.Database.AddObject(entry);
                    server.Database.SetPassword(dnt, password);

                    var created = server.Database.FindBySamAccountName(sam);
                    string sidStr = created?.ObjectSid != null ? JadeSecurity.FormatSidString(created.ObjectSid) : "N/A";

                    Console.WriteLine("Usuario corporativo provisionado com sucesso no NTDS.DIT:");
                    Console.WriteLine($"  DNT                : {dnt}");
                    Console.WriteLine($"  DistinguishedName  : CN={sam},CN=Users,{options.DomainDn}");
                    Console.WriteLine($"  sAMAccountName     : {sam}");
                    Console.WriteLine($"  userPrincipalName  : {sam}@{domainSuffix}");
                    Console.WriteLine($"  objectSid          : {sidStr}");
                    Console.WriteLine($"  Nivel de Acesso    : {level} (Grupo={entry.GetGroupDistinguishedName(options.DomainDn)})");
                    Console.WriteLine($"  adminCount         : {entry.AdminCount}");
                    break;
                }

                case "list":
                {
                    var users = server.Database.ListObjects(includeDeleted: false, filterClass: JadeObjectClass.User);
                    Console.WriteLine("--------------------------------------------------------------------------------------------------------------------");
                    Console.WriteLine($"{"DNT",-5} | {"sAMAccountName",-22} | {"Status",-10} | {"UAC",-6} | {"Nivel de Acesso",-17} | {"AdminCount",-10} | {"SID"}");
                    Console.WriteLine("--------------------------------------------------------------------------------------------------------------------");
                    foreach (var u in users)
                    {
                        string status = u.IsEnabled ? "ATIVO" : "DESATIVADO";
                        string sid = u.ObjectSid != null ? JadeSecurity.FormatSidString(u.ObjectSid) : "-";
                        Console.WriteLine($"{u.Dnt,-5} | {u.SamAccountName,-22} | {status,-10} | {(uint)u.UserAccountControl,-6} | {u.AccessLevel,-17} | {u.AdminCount,-10} | {sid}");
                    }
                    Console.WriteLine("--------------------------------------------------------------------------------------------------------------------");
                    Console.WriteLine($"Total de contas de usuario ativas: {users.Count}");
                    break;
                }

                case "show":
                {
                    if (parts.Length < 3)
                    {
                        Console.WriteLine("Uso: user show <sAMAccountName>");
                        return;
                    }

                    var u = server.Database.FindBySamAccountName(parts[2]);
                    if (u == null || u.IsDeleted)
                    {
                        Console.WriteLine($"Usuario '{parts[2]}' nao localizado no diretorio.");
                        return;
                    }

                    string sid = u.ObjectSid != null ? JadeSecurity.FormatSidString(u.ObjectSid) : "-";
                    Console.WriteLine("-----------------------------------------------------------------------");
                    Console.WriteLine($"  DistinguishedName  : CN={u.SamAccountName},CN=Users,{options.DomainDn}");
                    Console.WriteLine($"  DNT / PDNT         : {u.Dnt} / {u.Pdnt}");
                    Console.WriteLine($"  sAMAccountName     : {u.SamAccountName}");
                    Console.WriteLine($"  userPrincipalName  : {u.UserPrincipalName}");
                    Console.WriteLine($"  objectGUID         : {u.ObjectGuid}");
                    Console.WriteLine($"  objectSid          : {sid}");
                    Console.WriteLine($"  userAccountControl : {(uint)u.UserAccountControl} ({u.UserAccountControl})");
                    Console.WriteLine($"  Estado da Conta    : {(u.IsEnabled ? "ATIVA" : "DESATIVADA")}");
                    Console.WriteLine($"  Nivel de Acesso    : {u.AccessLevel}");
                    Console.WriteLine($"  Grupo Primario     : {u.GetGroupDistinguishedName(options.DomainDn)} (RID={u.PrimaryGroupRid})");
                    Console.WriteLine($"  adminCount         : {u.AdminCount} {(u.AdminCount == 1 ? "(Protegido por AdminSDHolder)" : "(Padrao)")}");
                    Console.WriteLine($"  whenCreated (UTC)  : {u.WhenCreated.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC");
                    Console.WriteLine("-----------------------------------------------------------------------");
                    break;
                }

                case "disable":
                {
                    if (parts.Length < 3)
                    {
                        Console.WriteLine("Uso: user disable <sAMAccountName>");
                        return;
                    }

                    var u = server.Database.FindBySamAccountName(parts[2]);
                    if (u == null || u.IsDeleted)
                    {
                        Console.WriteLine($"Usuario '{parts[2]}' nao localizado.");
                        return;
                    }

                    var newUac = u.UserAccountControl | UserAccountControl.AccountDisable;
                    server.Database.ModifyUserAccountControl(u.Dnt, newUac);
                    Console.WriteLine($"Conta '{u.SamAccountName}' (DNT={u.Dnt}) DESATIVADA. Novo UAC={(uint)newUac}.");
                    break;
                }

                case "enable":
                {
                    if (parts.Length < 3)
                    {
                        Console.WriteLine("Uso: user enable <sAMAccountName>");
                        return;
                    }

                    var u = server.Database.FindBySamAccountName(parts[2]);
                    if (u == null || u.IsDeleted)
                    {
                        Console.WriteLine($"Usuario '{parts[2]}' nao localizado.");
                        return;
                    }

                    var newUac = u.UserAccountControl & ~UserAccountControl.AccountDisable;
                    if (newUac == 0) newUac = UserAccountControl.NormalAccount;
                    server.Database.ModifyUserAccountControl(u.Dnt, newUac);
                    Console.WriteLine($"Conta '{u.SamAccountName}' (DNT={u.Dnt}) ATIVADA. Novo UAC={(uint)newUac}.");
                    break;
                }

                case "passwd":
                {
                    if (parts.Length < 4)
                    {
                        Console.WriteLine("Uso: user passwd <sAMAccountName> <nova_senha>");
                        return;
                    }

                    var u = server.Database.FindBySamAccountName(parts[2]);
                    if (u == null || u.IsDeleted)
                    {
                        Console.WriteLine($"Usuario '{parts[2]}' nao localizado.");
                        return;
                    }

                    server.Database.SetPassword(u.Dnt, parts[3]);
                    Console.WriteLine($"Senha criptografica (unicodePwd) de '{u.SamAccountName}' (DNT={u.Dnt}) redefinida com sucesso.");
                    break;
                }

                case "role":
                {
                    if (parts.Length < 4)
                    {
                        Console.WriteLine("Uso: user role <sAMAccountName> <DomainUser|SecurityAuditor|AccountOperator|DomainAdmin>");
                        return;
                    }

                    var u = server.Database.FindBySamAccountName(parts[2]);
                    if (u == null || u.IsDeleted)
                    {
                        Console.WriteLine($"Usuario '{parts[2]}' nao localizado.");
                        return;
                    }

                    if (!TryParseAccessLevel(parts[3], out JadeAccessLevel newLevel))
                    {
                        Console.WriteLine($"Nivel de acesso invalido: '{parts[3]}'. Valores permitidos: DomainUser, SecurityAuditor, AccountOperator, DomainAdmin.");
                        return;
                    }

                    server.Database.ModifyAccessLevel(u.Dnt, newLevel);
                    Console.WriteLine($"Nivel de acesso de '{u.SamAccountName}' (DNT={u.Dnt}) alterado para {newLevel} (adminCount={(newLevel == JadeAccessLevel.DomainAdmin ? 1 : 0)}).");
                    break;
                }

                case "auth":
                {
                    if (parts.Length < 4)
                    {
                        Console.WriteLine("Uso: user auth <sAMAccountName_ou_DN> <senha>");
                        return;
                    }

                    var res = server.Database.Authenticate(parts[2], parts[3]);
                    if (res.IsAuthenticated && res.Entry != null)
                    {
                        Console.WriteLine($"[AUTENTICACAO APROVADA] Usuario='{res.Entry.SamAccountName}' | Nivel={res.Entry.AccessLevel} | Grupo='{res.Entry.GetGroupDistinguishedName(options.DomainDn)}'");
                    }
                    else
                    {
                        Console.WriteLine($"[AUTENTICACAO REJEITADA] Codigo LDAP={res.LdapResultCode} | Diagnostico: {res.DiagnosticMessage}");
                    }
                    break;
                }

                case "delete":
                {
                    if (parts.Length < 3)
                    {
                        Console.WriteLine("Uso: user delete <sAMAccountName>");
                        return;
                    }

                    var u = server.Database.FindBySamAccountName(parts[2]);
                    if (u == null || u.IsDeleted)
                    {
                        Console.WriteLine($"Usuario '{parts[2]}' nao localizado.");
                        return;
                    }

                    server.Database.DeleteObject(u.Dnt, tombstone: true);
                    Console.WriteLine($"Usuario '{u.SamAccountName}' (DNT={u.Dnt}) movido para o container Tombstone (isDeleted=1).");
                    break;
                }

                default:
                    Console.WriteLine($"Subcomando de usuario desconhecido: '{sub}'. Digite 'help' para ajuda.");
                    break;
            }
        }

        private static void HandleRidCommand(JadeDomainControllerServer server, string[] parts)
        {
            if (parts.Length >= 3 && parts[1].Equals("exhaust", StringComparison.OrdinalIgnoreCase))
            {
                string toggle = parts[2].ToLowerInvariant();
                if (toggle == "on" || toggle == "true" || toggle == "1")
                {
                    server.SetRidExhaustionSimulation(true);
                    return;
                }
                if (toggle == "off" || toggle == "false" || toggle == "0")
                {
                    server.SetRidExhaustionSimulation(false);
                    return;
                }
            }

            server.SetRidExhaustionSimulation(!server.IsRidExhaustionSimulated);
        }

        private static void PrintServerStatus(JadeDomainControllerServer server, JadeDomainControllerOptions options)
        {
            uint totalObjects = server.Database.CountObjects();
            var activeUsers = server.Database.ListObjects(includeDeleted: false, filterClass: JadeObjectClass.User);

            Console.WriteLine("-----------------------------------------------------------------------");
            Console.WriteLine("  STATUS DO CONTROLADOR DE DOMINIO JADE");
            Console.WriteLine($"  Dominio Base (Root DN)   : {options.DomainDn}");
            Console.WriteLine($"  Porta LDAP / LDAPS       : {server.ActiveLdapPort} / {server.ActiveLdapsPort}");
            Console.WriteLine($"  Conexoes TCP Ativas      : {server.ActiveConnectionCount}");
            Console.WriteLine($"  Total Objetos (NTDS.DIT) : {totalObjects}");
            Console.WriteLine($"  Contas de Usuario Ativas : {activeUsers.Count}");
            Console.WriteLine($"  FSMO RID Exhaust (8209)  : {(server.IsRidExhaustionSimulated ? "SIMULACAO ATIVA (FALHA INJETADA)" : "OPERACIONAL (NORMAL)")}");
            Console.WriteLine("-----------------------------------------------------------------------");
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Comandos do Console Administrativo Jade:");
            Console.WriteLine("  user create <sam> <senha> [Nivel]   Cria usuario no NTDS.DIT com SID, hash de senha e nivel RBAC");
            Console.WriteLine("  user list                           Lista todas as contas de usuario ativas e seus niveis de acesso");
            Console.WriteLine("  user show <sam>                     Exibe todos os atributos de diretorio e seguranca do usuario");
            Console.WriteLine("  user disable <sam>                  Desativa a conta de usuario (userAccountControl = 514)");
            Console.WriteLine("  user enable <sam>                   Reativa a conta de usuario (userAccountControl = 512)");
            Console.WriteLine("  user passwd <sam> <nova_senha>      Redefine o hash criptografico unicodePwd do usuario");
            Console.WriteLine("  user role <sam> <Nivel>             Altera o nivel de acesso RBAC da conta");
            Console.WriteLine("  user auth <sam> <senha>             Testa autenticacao e retorna o nivel de privilegio e subcodigo AD");
            Console.WriteLine("  user delete <sam>                   Move o usuario para Tombstone (isDeleted = 1)");
            Console.WriteLine("  rid exhaust <on|off>                Ativa ou desativa a injecao de falha FSMO RID Master (Win32 8209)");
            Console.WriteLine("  status                              Exibe estatisticas em tempo real do Controlador de Dominio");
            Console.WriteLine("  help                                Exibe este guia de comandos e a matriz de niveis de acesso");
            Console.WriteLine("  quit                                Encerra o Controlador de Dominio com seguranca");
            Console.WriteLine();
            Console.WriteLine("Niveis de Acesso Corporativos Suportados (RBAC + AdminSDHolder):");
            Console.WriteLine("  0 = DomainUser       (CN=Domain Users, RID 513)      : Autentica, consulta diretorio e altera apenas a propria senha");
            Console.WriteLine("  1 = SecurityAuditor  (CN=Guests/Auditors, RID 546)   : Acesso estritamente de leitura (Bind e Search) para auditoria");
            Console.WriteLine("  2 = AccountOperator  (CN=Account Operators, RID 548) : Cria, edita, desativa e exclui usuarios exceto DomainAdmin");
            Console.WriteLine("  3 = DomainAdmin      (CN=Domain Admins, RID 512)     : Controle irrestrito do DC e protegido por AdminSDHolder (adminCount=1)");
        }

        private static bool TryParseAccessLevel(string input, out JadeAccessLevel level)
        {
            if (Enum.TryParse(input, ignoreCase: true, out level) && Enum.IsDefined(typeof(JadeAccessLevel), level))
            {
                return true;
            }
            if (uint.TryParse(input, out uint num) && num <= 3)
            {
                level = (JadeAccessLevel)num;
                return true;
            }
            level = JadeAccessLevel.DomainUser;
            return false;
        }

        private static string[] SplitCommandLine(string commandLine)
        {
            var args = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            foreach (char c in commandLine)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
            {
                args.Add(current.ToString());
            }

            return args.ToArray();
        }

        private static async Task<int> RunNetworkAndCryptoSelfTestAsync()
        {
            string tempDir = Path.Combine(AppContext.BaseDirectory, "self_test_dc", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                Console.WriteLine("Iniciando Auto-Teste de Rede, Protocolo LDAP v3, Criptografia TLS e RBAC do Jade DC...\n");

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
                    Console.WriteLine("\n[1/9] Testando Conexao LDAP, Descoberta RootDSE e Upgrade StartTLS...");
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
                    Console.WriteLine("\n[2/9] Testando Canal Criptografado LDAPS (TLS) e Autenticacao Administrator (DomainAdmin)...");
                    await using var ldapsClient = new JadeLdapClient();
                    await ldapsClient.ConnectAsync("127.0.0.1", server.ActiveLdapsPort, useImplicitLdaps: true);

                    var adminBind = await ldapsClient.BindAsync("CN=Administrator,CN=Users,DC=jade,DC=local", options.DefaultAdminPassword);
                    if (!adminBind.IsSuccess)
                    {
                        Console.WriteLine($"FALHA no Bind do Administrator: {adminBind.DiagnosticMessage}");
                        return 1;
                    }
                    Console.WriteLine("      Bind Administrativo (DomainAdmin) autenticado com sucesso sob canal LDAPS.");

                    /* Etapa 3: Provisionamento de Conta via AddRequest com unicodePwd UTF-16LE entre aspas */
                    Console.WriteLine("\n[3/9] Provisionando Conta Corporativa via LDAP AddRequest (unicodePwd UTF-16LE)...");
                    string userDn = "CN=especialista.iam,CN=Users,DC=jade,DC=local";
                    var addResp = await ldapsClient.AddUserAsync(
                        userDn,
                        "especialista.iam",
                        "especialista.iam@jade.local",
                        "SenhaInicial@2026!",
                        userAccountControl: 512,
                        accessLevel: JadeAccessLevel.DomainUser);

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
                    Console.WriteLine($"      Conta localizada no LDAP: DN='{searchedUser.DistinguishedName}', Nivel='{searchedUser.GetFirstValue("accessLevel")}', SID='{searchedUser.GetFirstValue("objectSidString")}'");

                    /* Etapa 4: Autenticacao do Usuario Criado e Teste de Senha Incorreta (data 52e) */
                    Console.WriteLine("\n[4/9] Validando Autenticacao (BindRequest) e Rejeicao de Credencial Invalida (data 52e)...");
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

                    /* Reautentica como Administrator para as operacoes administrativas seguintes */
                    await ldapsClient.BindAsync("CN=Administrator,CN=Users,DC=jade,DC=local", options.DefaultAdminPassword);

                    /* Etapa 5: Desativacao da Conta via ModifyRequest (UAC 514) e Bloqueio de Logon (data 533) */
                    Console.WriteLine("\n[5/9] Testando Desativacao de Conta (UAC 514) e Rejeicao de Logon (data 533)...");
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

                    /* Reautentica como Administrator */
                    await ldapsClient.BindAsync("CN=Administrator,CN=Users,DC=jade,DC=local", options.DefaultAdminPassword);

                    /* Etapa 6: Reativacao (UAC 512) e Redefinicao Criptografica de Senha */
                    Console.WriteLine("\n[6/9] Reativando Conta (UAC 512) e Redefinindo Senha via ModifyRequest...");
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

                    /* Etapa 7: Validacao de Controle de Acesso RBAC e Protecao AdminSDHolder */
                    Console.WriteLine("\n[7/9] Validando os 4 Niveis de Acesso RBAC e Protecao AdminSDHolder (Erro LDAP 50)...");
                    /* Como 'especialista.iam' (DomainUser) esta autenticado no socket atual, tenta criar usuario e deve receber InsufficientAccessRights */
                    var domainUserAddAttempt = await ldapsClient.AddUserAsync(
                        "CN=nao.autorizado,CN=Users,DC=jade,DC=local",
                        "nao.autorizado",
                        "nao.autorizado@jade.local",
                        "Senha#123");
                    if (domainUserAddAttempt.ResultCode != LdapResultCode.InsufficientAccessRights)
                    {
                        Console.WriteLine("FALHA: DomainUser conseguiu criar um objeto no diretorio!");
                        return 1;
                    }

                    /* Autentica como Administrator e cria um AccountOperator */
                    await ldapsClient.BindAsync("CN=Administrator,CN=Users,DC=jade,DC=local", options.DefaultAdminPassword);
                    await ldapsClient.AddUserAsync(
                        "CN=operador.iam,CN=Users,DC=jade,DC=local",
                        "operador.iam",
                        "operador.iam@jade.local",
                        "Operador@2026!",
                        userAccountControl: 512,
                        accessLevel: JadeAccessLevel.AccountOperator);

                    /* Autentica como AccountOperator: deve conseguir gerenciar DomainUser, mas falhar ao tentar alterar Administrator (AdminSDHolder) */
                    await ldapsClient.BindAsync("operador.iam@jade.local", "Operador@2026!");
                    var operatorModAdminAttempt = await ldapsClient.ModifyUserAccountControlAsync("CN=Administrator,CN=Users,DC=jade,DC=local", 514);
                    if (operatorModAdminAttempt.ResultCode != LdapResultCode.InsufficientAccessRights)
                    {
                        Console.WriteLine("FALHA: AccountOperator conseguiu modificar uma conta DomainAdmin protegida por AdminSDHolder!");
                        return 1;
                    }
                    Console.WriteLine("      RBAC e AdminSDHolder confirmados: DomainUser e AccountOperator bloqueados com LDAP 50 (INSUFF_ACCESS_RIGHTS).");

                    /* Reautentica como Administrator */
                    await ldapsClient.BindAsync("CN=Administrator,CN=Users,DC=jade,DC=local", options.DefaultAdminPassword);

                    /* Etapa 8: Simulacao de Esgotamento de RIDs (Win32 8209) via Rede */
                    Console.WriteLine("\n[8/9] Testando Esgotamento do FSMO RID Master sobre LDAP (Erro 8209 / WILL_NOT_PERFORM)...");
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

                    /* Etapa 9: Exclusao Tombstone via DelRequest */
                    Console.WriteLine("\n[9/9] Testando Exclusao Logica Tombstone via LDAP DelRequest...");
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
                Console.WriteLine("  STATUS: TODOS OS 9 TESTES DE REDE, CRIPTOGRAFIA E RBAC APROVADOS");
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
