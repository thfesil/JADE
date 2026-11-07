using System;
using System.IO;
using Jade.Core;
using Xunit;

namespace Jade.Tests
{
    public class JadeDatabaseTests : IDisposable
    {
        private readonly string _testBaseDir;

        public JadeDatabaseTests()
        {
            _testBaseDir = Path.Combine(AppContext.BaseDirectory, "test_dbs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testBaseDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testBaseDir))
                {
                    Directory.Delete(_testBaseDir, true);
                }
            }
            catch
            {
                /* Exclusao de arquivos retidos pelo SO em exFAT tratada silenciosamente */
            }
        }

        [Fact]
        public void CreateAndOpenDatabase_ShouldInitializeSuccessfully()
        {
            using (var db = JadeDatabase.OpenOrCreate(_testBaseDir))
            {
                uint count = db.CountObjects();
                Assert.Equal(0u, count);
            }

            Assert.True(File.Exists(Path.Combine(_testBaseDir, "jade_ntds.dit")));
        }

        [Fact]
        public void AddStructuralObjectsAndUser_ShouldPersistAndAllowIndexedQuery()
        {
            using var db = JadeDatabase.OpenOrCreate(_testBaseDir);

            /* Insercao da Raiz de Dominio */
            var root = new JadeDirectoryEntry
            {
                Pdnt = 0,
                ObjectClass = JadeObjectClass.Container,
                SamAccountName = "DC=jade,DC=local"
            };
            uint rootDnt = db.AddObject(root);
            Assert.True(rootDnt > 0);

            /* Insercao da Unidade Organizacional */
            var ou = new JadeDirectoryEntry
            {
                Pdnt = rootDnt,
                ObjectClass = JadeObjectClass.OrganizationalUnit,
                SamAccountName = "OU=Engenharia"
            };
            uint ouDnt = db.AddObject(ou);
            Assert.True(ouDnt > rootDnt);

            /* Provisionamento de Usuario */
            var user = new JadeDirectoryEntry
            {
                Pdnt = ouDnt,
                ObjectClass = JadeObjectClass.User,
                SamAccountName = "thiago.eng",
                UserPrincipalName = "thiago.eng@jade.local",
                UserAccountControl = UserAccountControl.NormalAccount
            };
            uint userDnt = db.AddObject(user);
            Assert.True(userDnt > ouDnt);

            /* Busca indexada O(1) B+Tree por sAMAccountName */
            var found = db.FindBySamAccountName("thiago.eng");
            Assert.NotNull(found);
            Assert.Equal(userDnt, found.Dnt);
            Assert.Equal("thiago.eng", found.SamAccountName);
            Assert.Equal("thiago.eng@jade.local", found.UserPrincipalName);
            Assert.Equal(UserAccountControl.NormalAccount, found.UserAccountControl);
            Assert.False(found.IsDeleted);

            /* Validacao da contagem total */
            Assert.Equal(3u, db.CountObjects());
        }

        [Fact]
        public void ModifyUserAccountControl_ShouldUpdateUacFlags()
        {
            using var db = JadeDatabase.OpenOrCreate(_testBaseDir);

            var user = new JadeDirectoryEntry
            {
                Pdnt = 1,
                ObjectClass = JadeObjectClass.User,
                SamAccountName = "usuario.status",
                UserAccountControl = UserAccountControl.NormalAccount /* 512 */
            };
            uint dnt = db.AddObject(user);

            /* Desativacao da conta: 512 | 2 = 514 */
            db.ModifyUserAccountControl(dnt, UserAccountControl.NormalAccount | UserAccountControl.AccountDisable);

            var updated = db.FindByDnt(dnt);
            Assert.NotNull(updated);
            Assert.True(updated.UserAccountControl.HasFlag(UserAccountControl.AccountDisable));
            Assert.Equal(514u, (uint)updated.UserAccountControl);
        }

        [Fact]
        public void SetPassword_ShouldUpdatePasswordHashWithoutErrors()
        {
            using var db = JadeDatabase.OpenOrCreate(_testBaseDir);

            var user = new JadeDirectoryEntry
            {
                Pdnt = 1,
                ObjectClass = JadeObjectClass.User,
                SamAccountName = "usuario.senha"
            };
            uint dnt = db.AddObject(user);

            db.SetPassword(dnt, "SenhaComplexa@2026");

            var updated = db.FindByDnt(dnt);
            Assert.NotNull(updated);
            Assert.NotNull(updated.UnicodePassword);
            Assert.True(updated.UnicodePassword.Length > 0);
        }

        [Fact]
        public void RidMaster_ShouldAllocateSequentially_AndThrowOnExhaustion()
        {
            using var db = JadeDatabase.OpenOrCreate(_testBaseDir);

            uint rid1 = db.AllocateRid();
            uint rid2 = db.AllocateRid();

            Assert.True(rid2 > rid1);
            Assert.Equal(rid1 + 1, rid2);

            /* Injetando simulacao de esgotamento do RID Master */
            db.SetRidExhaustionSimulation(true);

            var ex = Assert.Throws<JadeException>(() => db.AllocateRid());
            Assert.Equal(8209, ex.ErrorCode);
            Assert.True(ex.IsRidExhausted);

            /* Restaurando estado */
            db.SetRidExhaustionSimulation(false);
            uint rid3 = db.AllocateRid();
            Assert.Equal(rid2 + 1, rid3);
        }

        [Fact]
        public void DeleteObject_WithTombstone_ShouldMarkAsDeleted()
        {
            using var db = JadeDatabase.OpenOrCreate(_testBaseDir);

            var user = new JadeDirectoryEntry
            {
                Pdnt = 1,
                ObjectClass = JadeObjectClass.User,
                SamAccountName = "usuario.tombstone"
            };
            uint dnt = db.AddObject(user);

            db.DeleteObject(dnt, tombstone: true);

            var deleted = db.FindByDnt(dnt);
            Assert.NotNull(deleted);
            Assert.True(deleted.IsDeleted);

            /* Confirmando que nao pode mais ser localizado por sAMAccountName original */
            var notFound = db.FindBySamAccountName("usuario.tombstone");
            Assert.Null(notFound);
        }
    }
}
