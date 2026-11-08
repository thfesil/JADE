using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Jade.Native
{
    public static class NativeMethods
    {
        public const string LibraryName = "jade";

        static NativeMethods()
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveDll);
        }

        private static IntPtr ResolveDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == LibraryName)
            {
                string baseDir = AppContext.BaseDirectory;

                string[] candidatePaths = new[]
                {
                    Path.Combine(baseDir, "jade.dll"),
                    Path.Combine(baseDir, "bin", "jade.dll"),
                    Path.Combine(baseDir, "runtimes", "win-x64", "native", "jade.dll"),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "native", "bin", "jade.dll")),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "native", "bin", "jade.dll")),
                    @"F:\Projects\Jade\native\bin\jade.dll"
                };

                foreach (string path in candidatePaths)
                {
                    if (File.Exists(path))
                    {
                        if (NativeLibrary.TryLoad(path, out IntPtr handle))
                        {
                            return handle;
                        }
                    }
                }
            }

            return IntPtr.Zero;
        }

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jade_system_init")]
        public static extern int SystemInit();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "jade_db_open")]
        public static extern int DbOpen(string baseDir, string dbName, [MarshalAs(UnmanagedType.I1)] bool createIfMissing, out IntPtr outDb);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jade_db_close")]
        public static extern int DbClose(IntPtr db);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jade_add_object")]
        public static extern int AddObject(IntPtr db, ref JadeNativeObject obj, out uint outDnt);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, EntryPoint = "jade_find_by_sam")]
        public static extern int FindBySam(IntPtr db, string sam, out JadeNativeObject outObj);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jade_find_by_dnt")]
        public static extern int FindByDnt(IntPtr db, uint dnt, out JadeNativeObject outObj);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jade_modify_uac")]
        public static extern int ModifyUac(IntPtr db, uint dnt, uint newUac);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jade_modify_ncdnt")]
        public static extern int ModifyNcdnt(IntPtr db, uint dnt, uint newNcdnt);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jade_set_password")]
        public static extern int SetPassword(IntPtr db, uint dnt, byte[] pwd, uint pwdLen);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jade_allocate_rid")]
        public static extern int AllocateRid(IntPtr db, out uint outRid);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jade_set_rid_exhaustion_simulation")]
        public static extern int SetRidExhaustionSimulation(IntPtr db, [MarshalAs(UnmanagedType.I1)] bool simulate);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jade_delete_object")]
        public static extern int DeleteObject(IntPtr db, uint dnt, [MarshalAs(UnmanagedType.I1)] bool tombstone);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jade_count_objects")]
        public static extern int CountObjects(IntPtr db, out uint outCount);
    }
}
