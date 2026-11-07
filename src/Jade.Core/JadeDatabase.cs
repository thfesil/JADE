using System;
using System.IO;
using System.Text;
using Jade.Native;

namespace Jade.Core
{
    public class JadeDatabase : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed;

        public string BaseDirectory { get; }
        public string DatabaseName { get; }

        private JadeDatabase(IntPtr handle, string baseDirectory, string databaseName)
        {
            _handle = handle;
            BaseDirectory = baseDirectory;
            DatabaseName = databaseName;
        }

        public static JadeDatabase OpenOrCreate(string baseDirectory, string databaseName = "jade_ntds.dit")
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                throw new ArgumentException("O diretorio base da base de dados e mandatorio.", nameof(baseDirectory));
            }

            NativeMethods.SystemInit();

            int rc = NativeMethods.DbOpen(baseDirectory, databaseName, true, out IntPtr db);
            if (rc != JadeConstants.Ok || db == IntPtr.Zero)
            {
                throw new JadeException(rc, $"Falha ao abrir ou criar o banco ESE em {baseDirectory}\\{databaseName}");
            }

            return new JadeDatabase(db, baseDirectory, databaseName);
        }

        public uint AddObject(JadeDirectoryEntry entry)
        {
            EnsureNotDisposed();
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            var native = entry.ToNative();
            int rc = NativeMethods.AddObject(_handle, ref native, out uint dnt);
            if (rc != JadeConstants.Ok)
            {
                throw new JadeException(rc, $"Falha ao inserir objeto '{entry.SamAccountName}' no Active Directory");
            }

            entry.Dnt = dnt;
            return dnt;
        }

        public JadeDirectoryEntry? FindBySamAccountName(string samAccountName)
        {
            EnsureNotDisposed();
            if (string.IsNullOrEmpty(samAccountName)) return null;

            int rc = NativeMethods.FindBySam(_handle, samAccountName, out JadeNativeObject native);
            if (rc == JadeConstants.ErrRecordNotFound)
            {
                return null;
            }
            if (rc != JadeConstants.Ok)
            {
                throw new JadeException(rc, $"Erro ao consultar objeto por sAMAccountName '{samAccountName}'");
            }

            return JadeDirectoryEntry.FromNative(native);
        }

        public JadeDirectoryEntry? FindByDnt(uint dnt)
        {
            EnsureNotDisposed();

            int rc = NativeMethods.FindByDnt(_handle, dnt, out JadeNativeObject native);
            if (rc == JadeConstants.ErrRecordNotFound)
            {
                return null;
            }
            if (rc != JadeConstants.Ok)
            {
                throw new JadeException(rc, $"Erro ao consultar objeto por DNT '{dnt}'");
            }

            return JadeDirectoryEntry.FromNative(native);
        }

        public void ModifyUserAccountControl(uint dnt, UserAccountControl uac)
        {
            EnsureNotDisposed();

            int rc = NativeMethods.ModifyUac(_handle, dnt, (uint)uac);
            if (rc != JadeConstants.Ok)
            {
                throw new JadeException(rc, $"Falha ao atualizar userAccountControl do DNT '{dnt}' para {(uint)uac}");
            }
        }

        public void SetPassword(uint dnt, string plainTextPassword)
        {
            EnsureNotDisposed();
            if (plainTextPassword == null) throw new ArgumentNullException(nameof(plainTextPassword));

            byte[] pwdBytes = Encoding.UTF8.GetBytes(plainTextPassword);
            int rc = NativeMethods.SetPassword(_handle, dnt, pwdBytes, (uint)pwdBytes.Length);
            if (rc != JadeConstants.Ok)
            {
                throw new JadeException(rc, $"Falha ao redefinir a senha do DNT '{dnt}'");
            }
        }

        public uint AllocateRid()
        {
            EnsureNotDisposed();

            int rc = NativeMethods.AllocateRid(_handle, out uint rid);
            if (rc == JadeConstants.ErrRidExhausted)
            {
                throw new JadeException(JadeConstants.ErrRidExhausted, "O pool de RIDs do dominio foi esgotado (Win32 ERROR_DS_RIDM_UNWILLING_TO_PERFORM / 00002011).");
            }
            if (rc != JadeConstants.Ok)
            {
                throw new JadeException(rc, "Falha na alocacao de RID pelo FSMO Master");
            }

            return rid;
        }

        public void SetRidExhaustionSimulation(bool simulate)
        {
            EnsureNotDisposed();

            int rc = NativeMethods.SetRidExhaustionSimulation(_handle, simulate);
            if (rc != JadeConstants.Ok)
            {
                throw new JadeException(rc, "Falha ao definir o estado de simulacao de esgotamento de RIDs");
            }
        }

        public void DeleteObject(uint dnt, bool tombstone = true)
        {
            EnsureNotDisposed();

            int rc = NativeMethods.DeleteObject(_handle, dnt, tombstone);
            if (rc != JadeConstants.Ok)
            {
                throw new JadeException(rc, $"Falha ao excluir o objeto DNT '{dnt}' (Tombstone: {tombstone})");
            }
        }

        public uint CountObjects()
        {
            EnsureNotDisposed();

            int rc = NativeMethods.CountObjects(_handle, out uint total);
            if (rc != JadeConstants.Ok)
            {
                throw new JadeException(rc, "Falha ao contabilizar registros na tabela datatable");
            }

            return total;
        }

        private void EnsureNotDisposed()
        {
            if (_disposed || _handle == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(JadeDatabase));
            }
        }

        public void Close()
        {
            Dispose();
        }

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
                GC.SuppressFinalize(this);
            }
        }

        ~JadeDatabase()
        {
            Dispose();
        }
    }
}
