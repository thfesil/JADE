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

        private static string GetPlatformLibraryFileName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "jade.dll";
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "libjade.dylib";
            }
            return "libjade.so";
        }

        private static string GetRuntimeIdentifier()
        {
            string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
                : "linux";

            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => "x64"
            };

            return $"{os}-{arch}";
        }

        public static bool IsNativeLibraryAvailable()
        {
            return ResolveDll(LibraryName, typeof(NativeMethods).Assembly, null) != IntPtr.Zero;
        }

        private static IntPtr ResolveDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName != LibraryName)
            {
                return IntPtr.Zero;
            }

            string libFileName = GetPlatformLibraryFileName();
            string rid = GetRuntimeIdentifier();
            string baseDir = AppContext.BaseDirectory;

            var candidatePaths = new System.Collections.Generic.List<string>
            {
                Path.Combine(baseDir, libFileName),
                Path.Combine(baseDir, "bin", libFileName),
                Path.Combine(baseDir, "runtimes", rid, "native", libFileName)
            };

            string current = baseDir;
            for (int i = 0; i < 6; i++)
            {
                candidatePaths.Add(Path.GetFullPath(Path.Combine(current, "native", "bin", libFileName)));
                DirectoryInfo? parent = Directory.GetParent(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (parent == null)
                {
                    break;
                }
                current = parent.FullName;
            }

            foreach (string path in candidatePaths)
            {
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out IntPtr handle))
                {
                    return handle;
                }
            }

            if (NativeLibrary.TryLoad(libFileName, assembly, searchPath, out IntPtr sysHandle))
            {
                return sysHandle;
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
