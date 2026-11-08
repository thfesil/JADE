using System;
using System.IO;
using System.Threading.Tasks;
using Jade.Core;
using Jade.Server;
using Jade.Server.Ldap;
using Xunit;

namespace Jade.Tests
{
    public class JadeDomainControllerServerTests : IAsyncDisposable
    {
        private readonly string _testDataDir;
        private readonly JadeDomainControllerServer _server;

        public JadeDomainControllerServerTests()
        {
            _testDataDir = Path.Combine(AppContext.BaseDirectory, "dc_network_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDataDir);

            var options = new JadeDomainControllerOptions
            {
                DataDirectory = _testDataDir,
                LdapPort = 0,  /* Porta efemera */
                LdapsPort = 0, /* Porta efemera */
                RequireTlsForPasswordOperations = true
            };

            _server = new JadeDomainControllerServer(options);
            _server.Start();
        }

        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            try
            {
                if (Directory.Exists(_testDataDir))
                {
                    Directory.Delete(_testDataDir, true);
                }
            }
            catch
            {
                /* Exclusao de arquivos retidos tratada silenciosamente */
            }
        }

        [Fact]
        public async Task RootDse_AndStartTls_ShouldUpgradeConnectionAndAllowPasswordOperations()
        {
            await using var client = new JadeLdapClient();
            await client.ConnectAsync("127.0.0.1", _server.ActiveLdapPort, useImplicitLdaps: false);

            /* 1. RootDSE sem autenticacao */
            var rootDse = await client.QueryRootDseAsync();
            Assert.NotNull(rootDse);
            Assert.Equal("DC=jade,DC=local", rootDse.GetFirstValue("defaultNamingContext"));
            Assert.Equal("dc01.jade.local", rootDse.GetFirstValue("dnsHostName"));

            /* 2. Tentativa de definir senha em canal nao criptografado deve ser rejeitada pelo DC */
            var unencryptedAdd = await client.AddUserAsync(
                "CN=usuario.sem.tls,CN=Users,DC=jade,DC=local",
                "usuario.sem.tls",
                "usuario.sem.tls@jade.local",
                "Senha@123");

            Assert.Equal(LdapResultCode.UnwillingToPerform, unencryptedAdd.ResultCode);
            Assert.Contains("DSID-0C090446", unencryptedAdd.DiagnosticMessage);

            /* 3. Upgrade StartTLS na porta 389 */
            var tlsResp = await client.StartTlsAsync("dc01.jade.local");
            Assert.True(tlsResp.IsSuccess);
            Assert.True(client.IsTlsEncrypted);

            /* 4. Apos StartTLS, a criacao com unicodePwd e aceita */
            var encryptedAdd = await client.AddUserAsync(
                "CN=usuario.com.tls,CN=Users,DC=jade,DC=local",
                "usuario.com.tls",
                "usuario.com.tls@jade.local",
                "SenhaSegura@2026");

            Assert.True(encryptedAdd.IsSuccess);
        }

        [Fact]
        public async Task LdapsFullLifecycle_Provision_Authenticate_Disable_ResetPassword_AndDelete()
        {
            await using var client = new JadeLdapClient();
            await client.ConnectAsync("127.0.0.1", _server.ActiveLdapsPort, useImplicitLdaps: true);
            Assert.True(client.IsTlsEncrypted);

            /* 1. Bind como Administrator */
            var adminBind = await client.BindAsync("CN=Administrator,CN=Users,DC=jade,DC=local", "Admin@Jade2026!");
            Assert.True(adminBind.IsSuccess);

            /* 2. Provisionamento de usuario via AddRequest */
            string userDn = "CN=analista.dc,CN=Users,DC=jade,DC=local";
            var addResp = await client.AddUserAsync(
                userDn,
                "analista.dc",
                "analista.dc@jade.local",
                "SenhaInicial#2026",
                userAccountControl: 512);
            Assert.True(addResp.IsSuccess);

            /* 3. Busca LDAP SearchRequest */
            var searchEntry = await client.SearchBySamAccountNameAsync("DC=jade,DC=local", "analista.dc");
            Assert.NotNull(searchEntry);
            Assert.Equal("analista.dc", searchEntry.GetFirstValue("sAMAccountName"));
            Assert.StartsWith("S-1-5-21-", searchEntry.GetFirstValue("objectSidString"));

            /* 4. Autenticacao da nova conta (BindRequest) */
            var validBind = await client.BindAsync("analista.dc@jade.local", "SenhaInicial#2026");
            Assert.True(validBind.IsSuccess);

            /* 5. Senha incorreta deve retornar LDAP 49 com subcodigo AD data 52e */
            var invalidPwdBind = await client.BindAsync("analista.dc@jade.local", "SenhaErrada");
            Assert.Equal(LdapResultCode.InvalidCredentials, invalidPwdBind.ResultCode);
            Assert.Contains("data 52e", invalidPwdBind.DiagnosticMessage);

            /* 6. Desativacao da conta (UAC 514) e validacao de bloqueio (data 533) */
            var disableResp = await client.ModifyUserAccountControlAsync(userDn, 514);
            Assert.True(disableResp.IsSuccess);

            var disabledBind = await client.BindAsync("analista.dc@jade.local", "SenhaInicial#2026");
            Assert.Equal(LdapResultCode.InvalidCredentials, disabledBind.ResultCode);
            Assert.Contains("data 533", disabledBind.DiagnosticMessage);

            /* 7. Reativacao (UAC 512) e Redefinicao de Senha via ModifyRequest */
            await client.ModifyUserAccountControlAsync(userDn, 512);
            var modPwdResp = await client.ModifyPasswordAsync(userDn, "NovaSenha@2026!");
            Assert.True(modPwdResp.IsSuccess);

            var reenabledBind = await client.BindAsync("analista.dc", "NovaSenha@2026!");
            Assert.True(reenabledBind.IsSuccess);

            /* Reautentica como Administrator para operacoes administrativas de dominio */
            await client.BindAsync("CN=Administrator,CN=Users,DC=jade,DC=local", "Admin@Jade2026!");

            /* 8. Simulacao de esgotamento de RIDs (Win32 8209) */
            _server.SetRidExhaustionSimulation(true);
            var ridFailResp = await client.AddUserAsync(
                "CN=usuario.sem.rid,CN=Users,DC=jade,DC=local",
                "usuario.sem.rid",
                "usuario.sem.rid@jade.local",
                "Senha#2026");
            Assert.Equal(LdapResultCode.UnwillingToPerform, ridFailResp.ResultCode);
            Assert.Contains("8209", ridFailResp.DiagnosticMessage);
            _server.SetRidExhaustionSimulation(false);

            /* 9. Exclusao Tombstone (DelRequest) */
            var delResp = await client.DeleteObjectAsync(userDn);
            Assert.True(delResp.IsSuccess);

            var afterDeleteSearch = await client.SearchBySamAccountNameAsync("DC=jade,DC=local", "analista.dc");
            Assert.Null(afterDeleteSearch);
        }

        [Fact]
        public async Task RbacAndAdminSdHolder_ShouldEnforceFourCorporateAccessLevels()
        {
            await using var client = new JadeLdapClient();
            await client.ConnectAsync("127.0.0.1", _server.ActiveLdapsPort, useImplicitLdaps: true);

            /* 1. Administrator (DomainAdmin) provisiona contas dos 3 demais niveis */
            await client.BindAsync("CN=Administrator,CN=Users,DC=jade,DC=local", "Admin@Jade2026!");

            await client.AddUserAsync(
                "CN=user.comum,CN=Users,DC=jade,DC=local",
                "user.comum",
                "user.comum@jade.local",
                "SenhaUser#2026",
                accessLevel: JadeAccessLevel.DomainUser);

            await client.AddUserAsync(
                "CN=auditor.sec,CN=Users,DC=jade,DC=local",
                "auditor.sec",
                "auditor.sec@jade.local",
                "SenhaAudit#2026",
                accessLevel: JadeAccessLevel.SecurityAuditor);

            await client.AddUserAsync(
                "CN=operador.conta,CN=Users,DC=jade,DC=local",
                "operador.conta",
                "operador.conta@jade.local",
                "SenhaOper#2026",
                accessLevel: JadeAccessLevel.AccountOperator);

            /* 2. SecurityAuditor: pode consultar, mas nao pode alterar nem a propria senha */
            var auditBind = await client.BindAsync("auditor.sec@jade.local", "SenhaAudit#2026");
            Assert.True(auditBind.IsSuccess);

            var auditSearch = await client.SearchBySamAccountNameAsync("DC=jade,DC=local", "user.comum");
            Assert.NotNull(auditSearch);
            Assert.Equal("DomainUser", auditSearch.GetFirstValue("accessLevel"));

            var auditSelfPwd = await client.ModifyPasswordAsync("CN=auditor.sec,CN=Users,DC=jade,DC=local", "Tentativa#2026");
            Assert.Equal(LdapResultCode.InsufficientAccessRights, auditSelfPwd.ResultCode);

            /* 3. DomainUser: pode alterar a propria senha, mas nao pode criar contas nem alterar outros usuarios */
            var userBind = await client.BindAsync("user.comum@jade.local", "SenhaUser#2026");
            Assert.True(userBind.IsSuccess);

            var userSelfPwd = await client.ModifyPasswordAsync("CN=user.comum,CN=Users,DC=jade,DC=local", "NovaSenhaUser#2026");
            Assert.True(userSelfPwd.IsSuccess);

            var userCreateAttempt = await client.AddUserAsync(
                "CN=intruso,CN=Users,DC=jade,DC=local",
                "intruso",
                "intruso@jade.local",
                "Senha#123");
            Assert.Equal(LdapResultCode.InsufficientAccessRights, userCreateAttempt.ResultCode);

            /* 4. AccountOperator: pode gerenciar DomainUser, mas e bloqueado pelo AdminSDHolder ao tocar em DomainAdmin */
            var operBind = await client.BindAsync("operador.conta@jade.local", "SenhaOper#2026");
            Assert.True(operBind.IsSuccess);

            var operDisableUser = await client.ModifyUserAccountControlAsync("CN=user.comum,CN=Users,DC=jade,DC=local", 514);
            Assert.True(operDisableUser.IsSuccess);

            var operDisableAdmin = await client.ModifyUserAccountControlAsync("CN=Administrator,CN=Users,DC=jade,DC=local", 514);
            Assert.Equal(LdapResultCode.InsufficientAccessRights, operDisableAdmin.ResultCode);
            Assert.Contains("AdminSDHolder", operDisableAdmin.DiagnosticMessage);

            var operEscalateAttempt = await client.ModifyAccessLevelAsync("CN=user.comum,CN=Users,DC=jade,DC=local", JadeAccessLevel.DomainAdmin);
            Assert.Equal(LdapResultCode.InsufficientAccessRights, operEscalateAttempt.ResultCode);
            Assert.Contains("AdminSDHolder", operEscalateAttempt.DiagnosticMessage);
        }
    }
}
