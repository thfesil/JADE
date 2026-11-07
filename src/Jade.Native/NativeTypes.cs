using System;
using System.Runtime.InteropServices;

namespace Jade.Native
{
    public static class JadeConstants
    {
        /* Active Directory Object Classes */
        public const uint ClassContainer = 196613;
        public const uint ClassOrganizationalUnit = 196614;
        public const uint ClassUser = 196621;
        public const uint ClassGroup = 196622;

        /* Active Directory UserAccountControl Flags */
        public const uint UacAccountDisable = 0x0002;
        public const uint UacNormalAccount = 0x0200;
        public const uint UacDontExpirePassword = 0x10000;

        /* Custom Jade and Windows Error Codes */
        public const int Ok = 0;
        public const int ErrInitFailed = -1;
        public const int ErrDbCreateFailed = -2;
        public const int ErrTableCreateFailed = -3;
        public const int ErrRecordNotFound = -4;
        public const int ErrDuplicateSam = -5;
        public const int ErrRidExhausted = 8209; /* Win32 ERROR_DS_RIDM_UNWILLING_TO_PERFORM / 00002011 */
        public const int ErrTransactionFailed = -7;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct JadeNativeObject
    {
        public uint Dnt;
        public uint Pdnt;
        public uint Ncdnt;
        public uint ObjectClass;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string SamAccountName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string UserPrincipalName;

        public uint UserAccountControl;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
        public byte[] UnicodePwd;

        public uint UnicodePwdLen;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ObjectGuid;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 68)]
        public byte[] ObjectSid;

        public uint ObjectSidLen;

        [MarshalAs(UnmanagedType.I1)]
        public bool IsDeleted;

        public long WhenCreated;
        public long WhenChanged;

        public static JadeNativeObject CreateDefault()
        {
            return new JadeNativeObject
            {
                SamAccountName = string.Empty,
                UserPrincipalName = string.Empty,
                UnicodePwd = new byte[512],
                ObjectGuid = new byte[16],
                ObjectSid = new byte[68]
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JadeRidState
    {
        public uint CurrentRid;
        public uint MaxRid;
        public uint BlockSize;

        [MarshalAs(UnmanagedType.I1)]
        public bool SimulateExhaustion;
    }
}
