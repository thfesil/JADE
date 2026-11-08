using System;
using System.Text;
using Jade.Native;

namespace Jade.Core
{
    public class JadeDirectoryEntry
    {
        public uint Dnt { get; set; }
        public uint Pdnt { get; set; }
        public uint Ncdnt { get; set; }

        public JadeAccessLevel AccessLevel
        {
            get => Enum.IsDefined(typeof(JadeAccessLevel), Ncdnt) ? (JadeAccessLevel)Ncdnt : JadeAccessLevel.DomainUser;
            set => Ncdnt = (uint)value;
        }

        public int AdminCount => AccessLevel == JadeAccessLevel.DomainAdmin ? 1 : 0;

        public uint PrimaryGroupRid => AccessLevel switch
        {
            JadeAccessLevel.DomainAdmin => 512u,
            JadeAccessLevel.AccountOperator => 548u,
            JadeAccessLevel.SecurityAuditor => 546u,
            _ => 513u
        };

        public string GetGroupDistinguishedName(string domainDn = "DC=jade,DC=local")
        {
            return AccessLevel switch
            {
                JadeAccessLevel.DomainAdmin => $"CN=Domain Admins,CN=Users,{domainDn}",
                JadeAccessLevel.AccountOperator => $"CN=Account Operators,CN=Builtin,{domainDn}",
                JadeAccessLevel.SecurityAuditor => $"CN=Security Auditors,CN=Builtin,{domainDn}",
                _ => $"CN=Domain Users,CN=Users,{domainDn}"
            };
        }

        public JadeObjectClass ObjectClass { get; set; }
        public string SamAccountName { get; set; } = string.Empty;
        public string UserPrincipalName { get; set; } = string.Empty;
        public UserAccountControl UserAccountControl { get; set; } = UserAccountControl.NormalAccount;
        public bool IsEnabled => (UserAccountControl & UserAccountControl.AccountDisable) == 0;
        public byte[]? UnicodePassword { get; set; }
        public Guid ObjectGuid { get; set; }
        public byte[]? ObjectSid { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset WhenCreated { get; set; }
        public DateTimeOffset WhenChanged { get; set; }

        public static JadeDirectoryEntry FromNative(JadeNativeObject native)
        {
            var entry = new JadeDirectoryEntry
            {
                Dnt = native.Dnt,
                Pdnt = native.Pdnt,
                Ncdnt = native.Ncdnt,
                ObjectClass = (JadeObjectClass)native.ObjectClass,
                SamAccountName = native.SamAccountName ?? string.Empty,
                UserPrincipalName = native.UserPrincipalName ?? string.Empty,
                UserAccountControl = (UserAccountControl)native.UserAccountControl,
                IsDeleted = native.IsDeleted,
                WhenCreated = native.WhenCreated > 0 ? DateTimeOffset.FromFileTime(native.WhenCreated) : DateTimeOffset.UtcNow,
                WhenChanged = native.WhenChanged > 0 ? DateTimeOffset.FromFileTime(native.WhenChanged) : DateTimeOffset.UtcNow
            };

            if (native.ObjectGuid != null && native.ObjectGuid.Length == 16)
            {
                entry.ObjectGuid = new Guid(native.ObjectGuid);
            }

            if (native.ObjectSidLen > 0 && native.ObjectSid != null)
            {
                entry.ObjectSid = new byte[native.ObjectSidLen];
                Array.Copy(native.ObjectSid, entry.ObjectSid, native.ObjectSidLen);
            }

            if (native.UnicodePwdLen > 0 && native.UnicodePwd != null)
            {
                entry.UnicodePassword = new byte[native.UnicodePwdLen];
                Array.Copy(native.UnicodePwd, entry.UnicodePassword, native.UnicodePwdLen);
            }

            return entry;
        }

        public JadeNativeObject ToNative()
        {
            var native = JadeNativeObject.CreateDefault();
            native.Dnt = Dnt;
            native.Pdnt = Pdnt;
            native.Ncdnt = Ncdnt;
            native.ObjectClass = (uint)ObjectClass;
            native.SamAccountName = SamAccountName ?? string.Empty;
            native.UserPrincipalName = UserPrincipalName ?? string.Empty;
            native.UserAccountControl = (uint)UserAccountControl;
            native.IsDeleted = IsDeleted;

            if (ObjectGuid != Guid.Empty)
            {
                byte[] guidBytes = ObjectGuid.ToByteArray();
                Array.Copy(guidBytes, native.ObjectGuid, 16);
            }

            if (ObjectSid != null && ObjectSid.Length > 0)
            {
                int copyLen = Math.Min(ObjectSid.Length, 68);
                Array.Copy(ObjectSid, native.ObjectSid, copyLen);
                native.ObjectSidLen = (uint)copyLen;
            }

            if (UnicodePassword != null && UnicodePassword.Length > 0)
            {
                int copyLen = Math.Min(UnicodePassword.Length, 512);
                Array.Copy(UnicodePassword, native.UnicodePwd, copyLen);
                native.UnicodePwdLen = (uint)copyLen;
            }

            if (WhenCreated != default)
            {
                native.WhenCreated = WhenCreated.ToFileTime();
            }

            if (WhenChanged != default)
            {
                native.WhenChanged = WhenChanged.ToFileTime();
            }

            return native;
        }
    }
}
