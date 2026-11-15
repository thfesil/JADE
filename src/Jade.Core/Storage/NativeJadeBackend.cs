using System;
using System.IO;
using Jade.Native;

namespace Jade.Core.Storage
{
    internal sealed class NativeJadeBackend : IJadeStorageBackend
    {
        private IntPtr _handle;
        private bool _disposed;

        private NativeJadeBackend(IntPtr handle)
        {
            _handle = handle;
        }

        public static NativeJadeBackend OpenOrCreate(string baseDirectory, string databaseName)
        {
            Directory.CreateDirectory(baseDirectory);
            NativeMethods.SystemInit();

            int rc = NativeMethods.DbOpen(baseDirectory, databaseName, true, out IntPtr db);
            if (rc != JadeConstants.Ok || db == IntPtr.Zero)
            {
                string fullPath = Path.Combine(baseDirectory, databaseName);
                throw new JadeException(rc, $"Falha ao abrir ou criar o banco ESE em {fullPath}");
            }

            return new NativeJadeBackend(db);
        }

        public int AllocateRid(out uint rid) => NativeMethods.AllocateRid(_handle, out rid);
        public int AddObject(ref JadeNativeObject obj, out uint dnt) => NativeMethods.AddObject(_handle, ref obj, out dnt);
        public int FindBySam(string sam, out JadeNativeObject obj) => NativeMethods.FindBySam(_handle, sam, out obj);
        public int FindByDnt(uint dnt, out JadeNativeObject obj) => NativeMethods.FindByDnt(_handle, dnt, out obj);
        public int ModifyUac(uint dnt, uint newUac) => NativeMethods.ModifyUac(_handle, dnt, newUac);
        public int ModifyNcdnt(uint dnt, uint newNcdnt) => NativeMethods.ModifyNcdnt(_handle, dnt, newNcdnt);
        public int SetPassword(uint dnt, byte[] pwd, uint pwdLen) => NativeMethods.SetPassword(_handle, dnt, pwd, pwdLen);
        public int SetRidExhaustionSimulation(bool active) => NativeMethods.SetRidExhaustionSimulation(_handle, active);
        public int DeleteObject(uint dnt, bool tombstone) => NativeMethods.DeleteObject(_handle, dnt, tombstone);
        public int CountObjects(out uint count) => NativeMethods.CountObjects(_handle, out count);

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeMethods.DbClose(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }
    }
}
