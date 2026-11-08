using System;
using System.IO;
using System.Text;
using Jade.Native;

namespace Jade.Core
{
    public class JadeAuthenticationResult
    {
        public bool IsAuthenticated { get; init; }
        public int LdapResultCode { get; init; }
        public string DiagnosticMessage { get; init; } = string.Empty;
        public JadeDirectoryEntry? Entry { get; init; }
    }

    public class JadeDatabase : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed;
        private readonly object _syncRoot = new object();

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
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            lock (_syncRoot)
            {
                EnsureNotDisposed();

                if ((entry.ObjectSid == null || entry.ObjectSid.Length == 0) && entry.ObjectClass == JadeObjectClass.User)
                {
                    int ridRc = NativeMethods.AllocateRid(_handle, out uint allocatedRid);
                    if (ridRc == JadeConstants.ErrRidExhausted)
                    {
                        throw new JadeException(JadeConstants.ErrRidExhausted, "O pool de RIDs do dominio foi esgotado (Win32 ERROR_DS_RIDM_UNWILLING_TO_PERFORM / 00002011).");
                    }
                    if (ridRc == JadeConstants.Ok)
                    {
                        entry.ObjectSid = JadeSecurity.BuildBinarySid(allocatedRid);
                    }
                }

                var native = entry.ToNative();
                int rc = NativeMethods.AddObject(_handle, ref native, out uint dnt);
                if (rc != JadeConstants.Ok)
                {
                    throw new JadeException(rc, $"Falha ao inserir objeto '{entry.SamAccountName}' no Active Directory");
                }

                entry.Dnt = dnt;
                return dnt;
            }
        }

        public JadeDirectoryEntry? FindBySamAccountName(string samAccountName)
        {
            if (string.IsNullOrEmpty(samAccountName)) return null;

            lock (_syncRoot)
            {
                EnsureNotDisposed();

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
        }

        public JadeDirectoryEntry? FindByDnt(uint dnt)
        {
            lock (_syncRoot)
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
        }

        public void ModifyUserAccountControl(uint dnt, UserAccountControl uac)
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();

                int rc = NativeMethods.ModifyUac(_handle, dnt, (uint)uac);
                if (rc != JadeConstants.Ok)
                {
                    throw new JadeException(rc, $"Falha ao atualizar userAccountControl do DNT '{dnt}' para {(uint)uac}");
                }
            }
        }

        public void SetPassword(uint dnt, string plainTextPassword)
        {
            if (plainTextPassword == null) throw new ArgumentNullException(nameof(plainTextPassword));

            byte[] pwdHash = JadeSecurity.ComputePasswordHash(plainTextPassword);

            lock (_syncRoot)
            {
                EnsureNotDisposed();

                int rc = NativeMethods.SetPassword(_handle, dnt, pwdHash, (uint)pwdHash.Length);
                if (rc != JadeConstants.Ok)
                {
                    throw new JadeException(rc, $"Falha ao redefinir a senha do DNT '{dnt}'");
                }
            }
        }

        public void SetPasswordFromLdapBytes(uint dnt, byte[] ldapPasswordBytes)
        {
            if (ldapPasswordBytes == null) throw new ArgumentNullException(nameof(ldapPasswordBytes));

            byte[] pwdHash = JadeSecurity.ComputePasswordHashFromLdapBytes(ldapPasswordBytes);

            lock (_syncRoot)
            {
                EnsureNotDisposed();

                int rc = NativeMethods.SetPassword(_handle, dnt, pwdHash, (uint)pwdHash.Length);
                if (rc != JadeConstants.Ok)
                {
                    throw new JadeException(rc, $"Falha ao redefinir a senha LDAP do DNT '{dnt}'");
                }
            }
        }

        public JadeAuthenticationResult Authenticate(string identity, string password)
        {
            if (string.IsNullOrWhiteSpace(identity))
            {
                return new JadeAuthenticationResult
                {
                    IsAuthenticated = true,
                    LdapResultCode = 0,
                    DiagnosticMessage = "Anonymous bind accepted."
                };
            }

            string sam = ExtractSamAccountName(identity);
            var entry = FindBySamAccountName(sam);

            /* Usuario inexistente ou marcado como Tombstone (525 - user not found) */
            if (entry == null || entry.IsDeleted)
            {
                return new JadeAuthenticationResult
                {
                    IsAuthenticated = false,
                    LdapResultCode = 49, /* LDAP_INVALID_CREDENTIALS */
                    DiagnosticMessage = "80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 525, v4563"
                };
            }

            /* Conta desativada no userAccountControl (533 - account disabled) */
            if (entry.UserAccountControl.HasFlag(UserAccountControl.AccountDisable))
            {
                return new JadeAuthenticationResult
                {
                    IsAuthenticated = false,
                    LdapResultCode = 49, /* LDAP_INVALID_CREDENTIALS */
                    DiagnosticMessage = "80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 533, v4563",
                    Entry = entry
                };
            }

            /* Verificacao criptografica de senha em tempo constante (52e - invalid credentials) */
            if (!JadeSecurity.VerifyPassword(entry.UnicodePassword, password ?? string.Empty))
            {
                return new JadeAuthenticationResult
                {
                    IsAuthenticated = false,
                    LdapResultCode = 49, /* LDAP_INVALID_CREDENTIALS */
                    DiagnosticMessage = "80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data 52e, v4563",
                    Entry = entry
                };
            }

            return new JadeAuthenticationResult
            {
                IsAuthenticated = true,
                LdapResultCode = 0, /* LDAP_SUCCESS */
                DiagnosticMessage = string.Empty,
                Entry = entry
            };
        }

        public static string ExtractSamAccountName(string identityOrDn)
        {
            if (string.IsNullOrWhiteSpace(identityOrDn)) return string.Empty;

            string trimmed = identityOrDn.Trim();

            /* Formato DN: CN=usuario,OU=Engenharia,DC=jade,DC=local */
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                int commaIdx = trimmed.IndexOf(',');
                if (commaIdx > 3)
                {
                    return trimmed.Substring(3, commaIdx - 3).Trim();
                }
                return trimmed.Substring(3).Trim();
            }

            /* Formato NetBIOS: JADE\usuario */
            int slashIdx = trimmed.IndexOf('\\');
            if (slashIdx >= 0 && slashIdx + 1 < trimmed.Length)
            {
                return trimmed.Substring(slashIdx + 1).Trim();
            }

            /* Formato UPN: usuario@jade.local */
            int atIdx = trimmed.IndexOf('@');
            if (atIdx > 0)
            {
                return trimmed.Substring(0, atIdx).Trim();
            }

            return trimmed;
        }

        public uint AllocateRid()
        {
            lock (_syncRoot)
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
        }

        public void SetRidExhaustionSimulation(bool simulate)
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();

                int rc = NativeMethods.SetRidExhaustionSimulation(_handle, simulate);
                if (rc != JadeConstants.Ok)
                {
                    throw new JadeException(rc, "Falha ao definir o estado de simulacao de esgotamento de RIDs");
                }
            }
        }

        public void DeleteObject(uint dnt, bool tombstone = true)
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();

                int rc = NativeMethods.DeleteObject(_handle, dnt, tombstone);
                if (rc != JadeConstants.Ok)
                {
                    throw new JadeException(rc, $"Falha ao excluir o objeto DNT '{dnt}' (Tombstone: {tombstone})");
                }
            }
        }

        public uint CountObjects()
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();

                int rc = NativeMethods.CountObjects(_handle, out uint total);
                if (rc != JadeConstants.Ok)
                {
                    throw new JadeException(rc, "Falha ao contabilizar registros na tabela datatable");
                }

                return total;
            }
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
            lock (_syncRoot)
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
        }

        ~JadeDatabase()
        {
            Dispose();
        }
    }
}
