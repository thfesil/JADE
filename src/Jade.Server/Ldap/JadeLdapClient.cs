using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jade.Server.Ldap
{
    public sealed class LdapOperationResponse
    {
        public LdapResultCode ResultCode { get; init; }
        public string MatchedDn { get; init; } = string.Empty;
        public string DiagnosticMessage { get; init; } = string.Empty;
        public bool IsSuccess => ResultCode == LdapResultCode.Success;
    }

    public sealed class LdapSearchEntry
    {
        public string DistinguishedName { get; init; } = string.Empty;
        public Dictionary<string, List<string>> Attributes { get; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<byte[]>> BinaryAttributes { get; } = new Dictionary<string, List<byte[]>>(StringComparer.OrdinalIgnoreCase);

        public string? GetFirstValue(string attributeName)
        {
            if (Attributes.TryGetValue(attributeName, out var list) && list.Count > 0)
            {
                return list[0];
            }
            return null;
        }
    }

    public sealed class JadeLdapClient : IAsyncDisposable, IDisposable
    {
        private readonly TcpClient _tcpClient = new TcpClient();
        private Stream? _activeStream;
        private int _nextMessageId = 1;
        private bool _disposed;

        public bool IsTlsEncrypted { get; private set; }

        public async Task ConnectAsync(string host, int port, bool useImplicitLdaps = false, CancellationToken ct = default)
        {
            await _tcpClient.ConnectAsync(host, port, ct).ConfigureAwait(false);
            _tcpClient.NoDelay = true;
            _activeStream = _tcpClient.GetStream();

            if (useImplicitLdaps)
            {
                await NegotiateTlsAsync(host, ct).ConfigureAwait(false);
            }
        }

        public async Task<LdapOperationResponse> StartTlsAsync(string targetHost = "dc01.jade.local", CancellationToken ct = default)
        {
            EnsureConnected();
            int msgId = Interlocked.Increment(ref _nextMessageId);

            byte[] req = BerWriter.BuildLdapMessage(msgId, w =>
            {
                w.WriteConstructed(BerTag.ExtendedRequest, ext =>
                {
                    ext.WriteString(JadeDomainControllerServer.StartTlsOid, 0x80);
                });
            });

            await _activeStream!.WriteAsync(req, ct).ConfigureAwait(false);
            await _activeStream.FlushAsync(ct).ConfigureAwait(false);

            var resp = await ReadResultResponseAsync(BerTag.ExtendedResponse, ct).ConfigureAwait(false);
            if (resp.IsSuccess)
            {
                await NegotiateTlsAsync(targetHost, ct).ConfigureAwait(false);
            }

            return resp;
        }

        private async Task NegotiateTlsAsync(string targetHost, CancellationToken ct)
        {
            var sslStream = new SslStream(
                _activeStream!,
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (_, _, _, _) => true);

            var clientOptions = new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            };

            await sslStream.AuthenticateAsClientAsync(clientOptions, ct).ConfigureAwait(false);
            _activeStream = sslStream;
            IsTlsEncrypted = true;
        }

        public async Task<LdapOperationResponse> BindAsync(string bindDn, string password, CancellationToken ct = default)
        {
            EnsureConnected();
            int msgId = Interlocked.Increment(ref _nextMessageId);

            byte[] req = BerWriter.BuildLdapMessage(msgId, w =>
            {
                w.WriteConstructed(BerTag.BindRequest, bind =>
                {
                    bind.WriteInteger(3); /* LDAP v3 */
                    bind.WriteString(bindDn ?? string.Empty);
                    bind.WriteString(password ?? string.Empty, 0x80); /* Simple Auth [0] */
                });
            });

            await _activeStream!.WriteAsync(req, ct).ConfigureAwait(false);
            await _activeStream.FlushAsync(ct).ConfigureAwait(false);

            return await ReadResultResponseAsync(BerTag.BindResponse, ct).ConfigureAwait(false);
        }

        public async Task<LdapSearchEntry?> QueryRootDseAsync(CancellationToken ct = default)
        {
            var entries = await SearchAsync(string.Empty, scope: 0, filterAttr: "objectClass", filterVal: "*", ct).ConfigureAwait(false);
            return entries.Count > 0 ? entries[0] : null;
        }

        public async Task<LdapSearchEntry?> SearchBySamAccountNameAsync(string baseDn, string samAccountName, CancellationToken ct = default)
        {
            var entries = await SearchAsync(baseDn, scope: 2, filterAttr: "sAMAccountName", filterVal: samAccountName, ct).ConfigureAwait(false);
            return entries.Count > 0 ? entries[0] : null;
        }

        public async Task<List<LdapSearchEntry>> SearchAsync(
            string baseDn,
            int scope,
            string filterAttr,
            string filterVal,
            CancellationToken ct = default)
        {
            EnsureConnected();
            int msgId = Interlocked.Increment(ref _nextMessageId);

            byte[] req = BerWriter.BuildLdapMessage(msgId, w =>
            {
                w.WriteConstructed(BerTag.SearchRequest, s =>
                {
                    s.WriteString(baseDn ?? string.Empty);
                    s.WriteEnumerated(scope); /* 0=Base, 1=OneLevel, 2=Subtree */
                    s.WriteEnumerated(0);     /* derefAliases */
                    s.WriteInteger(100);      /* sizeLimit */
                    s.WriteInteger(30);       /* timeLimit */
                    s.WriteBoolean(false);    /* typesOnly */

                    /* EqualityMatch [3] (0xA3) */
                    s.WriteConstructed(0xA3, eq =>
                    {
                        eq.WriteString(filterAttr);
                        eq.WriteString(filterVal);
                    });

                    /* Attributes sequence */
                    s.WriteConstructed(BerTag.Sequence, _ => { });
                });
            });

            await _activeStream!.WriteAsync(req, ct).ConfigureAwait(false);
            await _activeStream.FlushAsync(ct).ConfigureAwait(false);

            var results = new List<LdapSearchEntry>();
            while (true)
            {
                byte[]? packet = await BerReader.ReadMessageBytesAsync(_activeStream, ct).ConfigureAwait(false);
                if (packet == null) throw new EndOfStreamException("Conexao encerrada durante leitura de SearchResponse.");

                var rootReader = new BerReader(packet);
                var msgSeq = rootReader.ReadConstructed(BerTag.Sequence);
                msgSeq.ReadInteger(); /* messageId */
                var opReader = msgSeq.ReadAnyConstructed(out byte opTag);

                if (opTag == BerTag.SearchResultEntry)
                {
                    string objDn = opReader.ReadString();
                    var entry = new LdapSearchEntry { DistinguishedName = objDn };
                    var attrsSeq = opReader.ReadConstructed(BerTag.Sequence);

                    while (attrsSeq.HasData)
                    {
                        var attrSeq = attrsSeq.ReadConstructed(BerTag.Sequence);
                        string attrName = attrSeq.ReadString();
                        var valSet = attrSeq.ReadConstructed(BerTag.Set);

                        var strValues = new List<string>();
                        var binValues = new List<byte[]>();

                        while (valSet.HasData)
                        {
                            byte[] rawVal = valSet.ReadOctetString();
                            binValues.Add(rawVal);
                            strValues.Add(Encoding.UTF8.GetString(rawVal));
                        }

                        entry.Attributes[attrName] = strValues;
                        entry.BinaryAttributes[attrName] = binValues;
                    }

                    results.Add(entry);
                }
                else if (opTag == BerTag.SearchResultDone)
                {
                    break;
                }
            }

            return results;
        }

        public async Task<LdapOperationResponse> AddUserAsync(
            string dn,
            string samAccountName,
            string userPrincipalName,
            string? initialPassword = null,
            uint userAccountControl = 512,
            CancellationToken ct = default)
        {
            EnsureConnected();
            int msgId = Interlocked.Increment(ref _nextMessageId);

            byte[] req = BerWriter.BuildLdapMessage(msgId, w =>
            {
                w.WriteConstructed(BerTag.AddRequest, add =>
                {
                    add.WriteString(dn);
                    add.WriteConstructed(BerTag.Sequence, attrs =>
                    {
                        WriteAttr(attrs, "objectClass", Encoding.UTF8.GetBytes("user"));
                        WriteAttr(attrs, "sAMAccountName", Encoding.UTF8.GetBytes(samAccountName));
                        WriteAttr(attrs, "userPrincipalName", Encoding.UTF8.GetBytes(userPrincipalName));
                        WriteAttr(attrs, "userAccountControl", Encoding.UTF8.GetBytes(userAccountControl.ToString(CultureInfo.InvariantCulture)));

                        if (initialPassword != null)
                        {
                            /* Codificacao oficial do Active Directory para unicodePwd: UTF-16LE entre aspas duplas */
                            byte[] quotedUtf16 = Encoding.Unicode.GetBytes($"\"{initialPassword}\"");
                            WriteAttr(attrs, "unicodePwd", quotedUtf16);
                        }
                    });
                });
            });

            await _activeStream!.WriteAsync(req, ct).ConfigureAwait(false);
            await _activeStream.FlushAsync(ct).ConfigureAwait(false);

            return await ReadResultResponseAsync(BerTag.AddResponse, ct).ConfigureAwait(false);
        }

        public async Task<LdapOperationResponse> ModifyUserAccountControlAsync(string dn, uint newUac, CancellationToken ct = default)
        {
            return await ModifyAttributeAsync(
                dn,
                "userAccountControl",
                Encoding.UTF8.GetBytes(newUac.ToString(CultureInfo.InvariantCulture)),
                ct).ConfigureAwait(false);
        }

        public async Task<LdapOperationResponse> ModifyPasswordAsync(string dn, string newPassword, CancellationToken ct = default)
        {
            byte[] quotedUtf16 = Encoding.Unicode.GetBytes($"\"{newPassword}\"");
            return await ModifyAttributeAsync(dn, "unicodePwd", quotedUtf16, ct).ConfigureAwait(false);
        }

        private async Task<LdapOperationResponse> ModifyAttributeAsync(string dn, string attrName, byte[] rawValue, CancellationToken ct)
        {
            EnsureConnected();
            int msgId = Interlocked.Increment(ref _nextMessageId);

            byte[] req = BerWriter.BuildLdapMessage(msgId, w =>
            {
                w.WriteConstructed(BerTag.ModifyRequest, mod =>
                {
                    mod.WriteString(dn);
                    mod.WriteConstructed(BerTag.Sequence, changes =>
                    {
                        changes.WriteConstructed(BerTag.Sequence, changeItem =>
                        {
                            changeItem.WriteEnumerated(2); /* 2 = Replace */
                            WriteAttr(changeItem, attrName, rawValue);
                        });
                    });
                });
            });

            await _activeStream!.WriteAsync(req, ct).ConfigureAwait(false);
            await _activeStream.FlushAsync(ct).ConfigureAwait(false);

            return await ReadResultResponseAsync(BerTag.ModifyResponse, ct).ConfigureAwait(false);
        }

        public async Task<LdapOperationResponse> DeleteObjectAsync(string dn, CancellationToken ct = default)
        {
            EnsureConnected();
            int msgId = Interlocked.Increment(ref _nextMessageId);

            byte[] req = BerWriter.BuildLdapMessage(msgId, w =>
            {
                w.WriteString(dn, BerTag.DelRequest);
            });

            await _activeStream!.WriteAsync(req, ct).ConfigureAwait(false);
            await _activeStream.FlushAsync(ct).ConfigureAwait(false);

            return await ReadResultResponseAsync(BerTag.DelResponse, ct).ConfigureAwait(false);
        }

        private static void WriteAttr(BerWriter writer, string name, byte[] val)
        {
            writer.WriteConstructed(BerTag.Sequence, attr =>
            {
                attr.WriteString(name);
                attr.WriteConstructed(BerTag.Set, set =>
                {
                    set.WriteOctetString(val);
                });
            });
        }

        private async Task<LdapOperationResponse> ReadResultResponseAsync(byte expectedTag, CancellationToken ct)
        {
            byte[]? packet = await BerReader.ReadMessageBytesAsync(_activeStream!, ct).ConfigureAwait(false);
            if (packet == null)
            {
                throw new EndOfStreamException($"Conexao encerrada aguardando resposta LDAP 0x{expectedTag:X2}.");
            }

            var rootReader = new BerReader(packet);
            var msgSeq = rootReader.ReadConstructed(BerTag.Sequence);
            msgSeq.ReadInteger(); /* messageId */
            var opReader = msgSeq.ReadAnyConstructed(out _);

            int code = opReader.ReadEnumerated();
            string matchedDn = opReader.HasData ? opReader.ReadString() : string.Empty;
            string diagMsg = opReader.HasData ? opReader.ReadString() : string.Empty;

            return new LdapOperationResponse
            {
                ResultCode = (LdapResultCode)code,
                MatchedDn = matchedDn,
                DiagnosticMessage = diagMsg
            };
        }

        private void EnsureConnected()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(JadeLdapClient));
            if (_activeStream == null) throw new InvalidOperationException("O cliente LDAP nao esta conectado.");
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    if (_activeStream != null)
                    {
                        await _activeStream.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch { }
                try { _tcpClient.Dispose(); } catch { }
            }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
