using System;
using System.IO;
using Jade.Core;
using Jade.Native;
using Xunit;

namespace Jade.Tests
{
    public class PortableBackendTests : IDisposable
    {
        private readonly string _testDir;

        public PortableBackendTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), $"jade_portable_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDir);
        }

        [Fact]
        public void ManagedBackend_ShouldSupportFullLifecycleAndPersistenceAcrossReopen()
        {
            uint userDnt;

            using (var db = JadeDatabase.OpenOrCreate(_testDir, "jade_portable.dit", JadeStorageBackendMode.Managed))
            {
                Assert.False(db.IsUsingNativeBackend);

                uint ouDnt = db.AddObject(new JadeDirectoryEntry
                {
                    Pdnt = 0,
                    ObjectClass = JadeObjectClass.OrganizationalUnit,
                    SamAccountName = "OU=LinuxPortable"
                });
                Assert.True(ouDnt > 0);

                var user = new JadeDirectoryEntry
                {
                    Pdnt = ouDnt,
                    ObjectClass = JadeObjectClass.User,
                    SamAccountName = "linux.user",
                    UserPrincipalName = "linux.user@jade.local",
                    UserAccountControl = UserAccountControl.NormalAccount,
                    AccessLevel = JadeAccessLevel.AccountOperator
                };
                userDnt = db.AddObject(user);
                db.SetPassword(userDnt, "SenhaLinux#2026");

                var dupEx = Assert.Throws<JadeException>(() => db.AddObject(new JadeDirectoryEntry
                {
                    Pdnt = ouDnt,
                    ObjectClass = JadeObjectClass.User,
                    SamAccountName = "LINUX.USER"
                }));
                Assert.Equal(JadeConstants.ErrDuplicateSam, dupEx.ErrorCode);

                uint rid1 = db.AllocateRid();
                uint rid2 = db.AllocateRid();
                Assert.Equal(rid1 + 1, rid2);

                db.SetRidExhaustionSimulation(true);
                var ridEx = Assert.Throws<JadeException>(() => db.AllocateRid());
                Assert.Equal(JadeConstants.ErrRidExhausted, ridEx.ErrorCode);
                db.SetRidExhaustionSimulation(false);
            }

            using (var reopened = JadeDatabase.OpenOrCreate(_testDir, "jade_portable.dit", JadeStorageBackendMode.Managed))
            {
                var found = reopened.FindBySamAccountName("linux.user");
                Assert.NotNull(found);
                Assert.Equal(userDnt, found!.Dnt);
                Assert.Equal(JadeAccessLevel.AccountOperator, found.AccessLevel);

                var okAuth = reopened.Authenticate("linux.user", "SenhaLinux#2026");
                Assert.True(okAuth.IsAuthenticated);

                var badAuth = reopened.Authenticate("linux.user", "SenhaErrada");
                Assert.False(badAuth.IsAuthenticated);
                Assert.Contains("data 52e", badAuth.DiagnosticMessage);

                reopened.ModifyUserAccountControl(userDnt, UserAccountControl.NormalAccount | UserAccountControl.AccountDisable);
                var disabledAuth = reopened.Authenticate("linux.user", "SenhaLinux#2026");
                Assert.False(disabledAuth.IsAuthenticated);
                Assert.Contains("data 533", disabledAuth.DiagnosticMessage);

                reopened.DeleteObject(userDnt, tombstone: true);
                var deleted = reopened.FindByDnt(userDnt);
                Assert.NotNull(deleted);
                Assert.True(deleted!.IsDeleted);
                Assert.Contains("DEL:", deleted.SamAccountName);

                Assert.Equal(2u, reopened.CountObjects());
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
