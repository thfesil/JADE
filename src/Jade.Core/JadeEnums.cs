using System;

namespace Jade.Core
{
    public enum JadeObjectClass : uint
    {
        Container = 196613,
        OrganizationalUnit = 196614,
        User = 196621,
        Group = 196622
    }

    [Flags]
    public enum UserAccountControl : uint
    {
        AccountDisable = 0x0002,
        NormalAccount = 0x0200,
        DontExpirePassword = 0x10000
    }
}
