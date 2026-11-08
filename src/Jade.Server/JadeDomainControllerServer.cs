using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jade.Core;
using Jade.Native;
using Jade.Server.Ldap;
using Jade.Server.Security;

namespace Jade.Server
{
    public sealed class JadeDomainControllerOptions
    {
        public string DataDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "jade_dc_data");
        public string DatabaseName { get; set; } = "jade_ntds.dit";
        public string DomainDn { get; set; } = "DC=jade,DC=local";
        public string DnsHostName { get; set; } = "dc01.jade.local";
        public int LdapPort { get; set; } = 3890;
        public int LdapsPort { get; set; } = 6360;
        public bool RequireTlsForPasswordOperations { get; set; } = true;
        public string DefaultAdminPassword { get; set; } = "Admin@Jade2026!";
        public Action<string>? Logger { get; set; }
    }

    public sealed class JadeDomainControllerServer : IAsyncDisposable, IDisposable
    {
        public const string StartTlsOid = "1.3.6.1.4.1.1466.20037";

        private readonly JadeDomainControllerOptions _options;
        private readonly JadeDatabase _db;
        private readonly X509Certificate2 _serverCertificate;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<Guid, TcpClient> _activeClients = new ConcurrentDictionary<Guid, TcpClient>();

        private TcpListener? _ldapListener;
        private TcpListener? _ldapsListener;
        private Task? _ldapAcceptTask;
        private Task? _ldapsAcceptTask;
        private bool _disposed;
        private bool _ridExhaustionActive;

        public JadeDatabase Database => _db;
        public X509Certificate2 ServerCertificate => _serverCertificate;
        public int ActiveLdapPort { get; private set; }
        public int ActiveLdapsPort { get; private set; }
        public int ActiveConnectionCount => _activeClients.Count;
        public bool IsRidExhaustionSimulated => _ridExhaustionActive;

        public JadeDomainControllerServer(JadeDomainControllerOptions? options = null)
        {
            _options = options ?? new JadeDomainControllerOptions();
            Directory.CreateDirectory(_options.DataDirectory);

            _db = JadeDatabase.OpenOrCreate(_options.DataDirectory, _options.DatabaseName);
            _serverCertificate = TlsCertificateManager.GetOrCreateServerCertificate(_options.DataDirectory, _options.DnsHostName);

            SeedDefaultDirectoryHierarchy();
        }

        private void SeedDefaultDirectoryHierarchy()
        {
            if (_db.CountObjects() > 0)
            {
                return;
            }

            /* 1. Dominio Raiz */
            var rootEntry = new JadeDirectoryEntry
            {
                Pdnt = 0,
                ObjectClass = JadeObjectClass.Container,
                SamAccountName = _options.DomainDn
            };
            uint rootDnt = _db.AddObject(rootEntry);

            /* 2. Container CN=Users */
            var usersContainer = new JadeDirectoryEntry
            {
                Pdnt = rootDnt,
                ObjectClass = JadeObjectClass.Container,
                SamAccountName = "CN=Users"
            };
            uint usersDnt = _db.AddObject(usersContainer);

            /* 3. OU=Domain Controllers */
            var dcOu = new JadeDirectoryEntry
            {
                Pdnt = rootDnt,
                ObjectClass = JadeObjectClass.OrganizationalUnit,
                SamAccountName = "OU=Domain Controllers"
            };
            _db.AddObject(dcOu);

            /* 4. Conta integrada Administrator */
            var adminEntry = new JadeDirectoryEntry
            {
                Pdnt = usersDnt,
                ObjectClass = JadeObjectClass.User,
                SamAccountName = "Administrator",
                UserPrincipalName = $"Administrator@{_options.DnsHostName.Substring(_options.DnsHostName.IndexOf('.') + 1)}",
                UserAccountControl = UserAccountControl.NormalAccount | UserAccountControl.DontExpirePassword
            };
            uint adminDnt = _db.AddObject(adminEntry);
            _db.SetPassword(adminDnt, _options.DefaultAdminPassword);

            Log($"Hierarquia inicial provisionada no NTDS.DIT (Administrator DNT={adminDnt}).");
        }

        public void Start()
        {
            _ldapListener = new TcpListener(IPAddress.Any, _options.LdapPort);
            _ldapListener.Start();
            ActiveLdapPort = ((IPEndPoint)_ldapListener.LocalEndpoint).Port;

            _ldapsListener = new TcpListener(IPAddress.Any, _options.LdapsPort);
            _ldapsListener.Start();
            ActiveLdapsPort = ((IPEndPoint)_ldapsListener.LocalEndpoint).Port;

            _ldapAcceptTask = Task.Run(() => AcceptLoopAsync(_ldapListener, isImplicitTls: false, _cts.Token));
            _ldapsAcceptTask = Task.Run(() => AcceptLoopAsync(_ldapsListener, isImplicitTls: true, _cts.Token));

            Log($"Controlador de Dominio Jade em execucao. LDAP={ActiveLdapPort}, LDAPS={ActiveLdapsPort} (TLS X.509 Thumbprint={_serverCertificate.Thumbprint}).");
        }

        public void SetRidExhaustionSimulation(bool simulate)
        {
            _ridExhaustionActive = simulate;
            _db.SetRidExhaustionSimulation(simulate);
            Log($"Simulacao de esgotamento do FSMO RID Master (Win32 8209): {(simulate ? "ATIVADA" : "DESATIVADA")}.");
        }

        private async Task AcceptLoopAsync(TcpListener listener, bool isImplicitTls, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    client.NoDelay = true;
                    Guid clientId = Guid.NewGuid();
                    _activeClients[clientId] = client;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleClientConnectionAsync(client, isImplicitTls, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            _activeClients.TryRemove(clientId, out _);
                            try { client.Dispose(); } catch { }
                        }
                    }, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Log($"Erro ao aceitar conexao TCP: {ex.Message}");
                    }
                }
            }
        }

        private async Task HandleClientConnectionAsync(TcpClient client, bool isImplicitTls, CancellationToken ct)
        {
            Stream activeStream = client.GetStream();
            bool isTlsEncrypted = false;
            string boundIdentity = string.Empty;

            try
            {
                if (isImplicitTls)
                {
                    var sslStream = new SslStream(activeStream, leaveInnerStreamOpen: false);
                    var sslOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _serverCertificate,
                        ClientCertificateRequired = false,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    };

                    await sslStream.AuthenticateAsServerAsync(sslOptions, ct).ConfigureAwait(false);
                    activeStream = sslStream;
                    isTlsEncrypted = true;
                }

                while (!ct.IsCancellationRequested)
                {
                    byte[]? packet = await BerReader.ReadMessageBytesAsync(activeStream, ct).ConfigureAwait(false);
                    if (packet == null)
                    {
                        break;
                    }

                    var rootReader = new BerReader(packet);
                    var msgSeq = rootReader.ReadConstructed(BerTag.Sequence);
                    int messageId = msgSeq.ReadInteger();
                    var opReader = msgSeq.ReadAnyConstructed(out byte opTag);

                    if (opTag == BerTag.UnbindRequest)
                    {
                        break;
                    }

                    if (opTag == BerTag.ExtendedRequest)
                    {
                        string oid = opReader.ReadString(0x80);
                        if (string.Equals(oid, StartTlsOid, StringComparison.Ordinal))
                        {
                            byte[] startTlsResp = BerWriter.BuildLdapMessage(messageId, w =>
                            {
                                w.WriteConstructed(BerTag.ExtendedResponse, ext =>
                                {
                                    ext.WriteEnumerated((int)LdapResultCode.Success);
                                    ext.WriteString(string.Empty);
                                    ext.WriteString("StartTLS ready");
                                });
                            });

                            await activeStream.WriteAsync(startTlsResp, ct).ConfigureAwait(false);
                            await activeStream.FlushAsync(ct).ConfigureAwait(false);

                            var sslStream = new SslStream(activeStream, leaveInnerStreamOpen: false);
                            var sslOptions = new SslServerAuthenticationOptions
                            {
                                ServerCertificate = _serverCertificate,
                                ClientCertificateRequired = false,
                                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                            };

                            await sslStream.AuthenticateAsServerAsync(sslOptions, ct).ConfigureAwait(false);
                            activeStream = sslStream;
                            isTlsEncrypted = true;
                            continue;
                        }
                    }

                    List<byte[]> responses = ProcessLdapOperation(messageId, opTag, opReader, isTlsEncrypted, ref boundIdentity);
                    foreach (byte[] resp in responses)
                    {
                        await activeStream.WriteAsync(resp, ct).ConfigureAwait(false);
                    }
                    await activeStream.FlushAsync(ct).ConfigureAwait(false);
                }
            }
            catch (EndOfStreamException)
            {
                /* Encerramento de socket pelo cliente */
            }
            catch (IOException)
            {
                /* Conexao fechada */
            }
            catch (OperationCanceledException)
            {
                /* Desligamento do servidor */
            }
            catch (Exception ex)
            {
                Log($"Excecao no processamento de sessao LDAP: {ex.Message}");
            }
            finally
            {
                try { activeStream.Dispose(); } catch { }
            }
        }

        private List<byte[]> ProcessLdapOperation(
            int messageId,
            byte opTag,
            BerReader opReader,
            bool isTlsEncrypted,
            ref string boundIdentity)
        {
            switch (opTag)
            {
                case BerTag.BindRequest:
                    return HandleBindRequest(messageId, opReader, out boundIdentity);

                case BerTag.SearchRequest:
                    return HandleSearchRequest(messageId, opReader);

                case BerTag.AddRequest:
                    return HandleAddRequest(messageId, opReader, isTlsEncrypted);

                case BerTag.ModifyRequest:
                    return HandleModifyRequest(messageId, opReader, isTlsEncrypted);

                case BerTag.DelRequest:
                    return HandleDeleteRequest(messageId, opReader);

                default:
                    return new List<byte[]>
                    {
                        BerWriter.BuildLdapMessage(messageId, w =>
                            w.WriteLdapResult(0x78, LdapResultCode.ProtocolError, "", $"Operacao LDAP 0x{opTag:X2} nao suportada."))
                    };
            }
        }

        private List<byte[]> HandleBindRequest(int messageId, BerReader opReader, out string boundIdentity)
        {
            int version = opReader.ReadInteger();
            string bindDn = opReader.ReadString();
            boundIdentity = string.Empty;

            byte authChoiceTag = opReader.PeekTag();
            string password = string.Empty;

            /* 0x80 = Simple Authentication */
            if (authChoiceTag == 0x80)
            {
                password = opReader.ReadString(0x80);
            }
            else
            {
                opReader.SkipElement();
            }

            var authResult = _db.Authenticate(bindDn, password);
            if (authResult.IsAuthenticated)
            {
                boundIdentity = bindDn;
            }

            Log($"LDAP BindRequest [v{version}] DN='{bindDn}' -> Codigo={authResult.LdapResultCode}");

            byte[] response = BerWriter.BuildLdapMessage(messageId, w =>
            {
                w.WriteLdapResult(
                    BerTag.BindResponse,
                    (LdapResultCode)authResult.LdapResultCode,
                    string.Empty,
                    authResult.DiagnosticMessage);
            });

            return new List<byte[]> { response };
        }

        private List<byte[]> HandleSearchRequest(int messageId, BerReader opReader)
        {
            string baseObject = opReader.ReadString();
            int scope = opReader.ReadEnumerated();
            int derefAliases = opReader.ReadEnumerated();
            int sizeLimit = opReader.ReadInteger();
            int timeLimit = opReader.ReadInteger();
            bool typesOnly = opReader.ReadBoolean();

            /* Extrai filtro de busca */
            string? samFilter = TryExtractSamAccountNameFromFilter(opReader);

            var packets = new List<byte[]>();

            /* Caso 1: Consulta RootDSE (BaseObject vazio e Scope 0 Base) */
            if (string.IsNullOrEmpty(baseObject) && scope == 0)
            {
                packets.Add(BuildRootDseEntryPacket(messageId));
                packets.Add(BerWriter.BuildLdapMessage(messageId, w =>
                    w.WriteLdapResult(BerTag.SearchResultDone, LdapResultCode.Success)));
                return packets;
            }

            /* Caso 2: Busca por sAMAccountName ou por DN direto */
            string targetSam = samFilter ?? string.Empty;
            if (string.IsNullOrEmpty(targetSam) && baseObject.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                targetSam = JadeDatabase.ExtractSamAccountName(baseObject);
            }

            if (!string.IsNullOrEmpty(targetSam))
            {
                var found = _db.FindBySamAccountName(targetSam);
                if (found != null && !found.IsDeleted)
                {
                    packets.Add(BuildDirectoryEntryPacket(messageId, found));
                }
            }

            packets.Add(BerWriter.BuildLdapMessage(messageId, w =>
                w.WriteLdapResult(BerTag.SearchResultDone, LdapResultCode.Success)));

            return packets;
        }

        private static string? TryExtractSamAccountNameFromFilter(BerReader opReader)
        {
            if (!opReader.HasData) return null;

            byte filterTag = opReader.PeekTag();

            /* 0xA3 = EqualityMatch (attributeDesc, assertionValue) */
            if (filterTag == 0xA3)
            {
                var eqReader = opReader.ReadConstructed(0xA3);
                string attr = eqReader.ReadString();
                string val = eqReader.ReadString();
                if (attr.Equals("sAMAccountName", StringComparison.OrdinalIgnoreCase) ||
                    attr.Equals("cn", StringComparison.OrdinalIgnoreCase) ||
                    attr.Equals("userPrincipalName", StringComparison.OrdinalIgnoreCase))
                {
                    return JadeDatabase.ExtractSamAccountName(val);
                }
                return null;
            }

            /* 0xA0 = AND filter (&(...)(...)) */
            if (filterTag == 0xA0)
            {
                var andReader = opReader.ReadConstructed(0xA0);
                while (andReader.HasData)
                {
                    string? nested = TryExtractSamAccountNameFromFilter(andReader);
                    if (!string.IsNullOrEmpty(nested))
                    {
                        return nested;
                    }
                }
                return null;
            }

            opReader.SkipElement();
            return null;
        }

        private byte[] BuildRootDseEntryPacket(int messageId)
        {
            return BerWriter.BuildLdapMessage(messageId, w =>
            {
                w.WriteConstructed(BerTag.SearchResultEntry, entry =>
                {
                    entry.WriteString(string.Empty); /* RootDSE objectName = "" */
                    entry.WriteConstructed(BerTag.Sequence, attrs =>
                    {
                        WriteAttribute(attrs, "defaultNamingContext", _options.DomainDn);
                        WriteAttribute(attrs, "rootDomainNamingContext", _options.DomainDn);
                        WriteAttribute(attrs, "configurationNamingContext", $"CN=Configuration,{_options.DomainDn}");
                        WriteAttribute(attrs, "schemaNamingContext", $"CN=Schema,CN=Configuration,{_options.DomainDn}");
                        WriteAttribute(attrs, "dnsHostName", _options.DnsHostName);
                        WriteAttribute(attrs, "supportedLDAPVersion", "3");
                        WriteAttribute(attrs, "supportedCapabilities", "1.2.840.113556.1.4.800");
                        WriteAttribute(attrs, "vendorName", "Jade Jet Active Directory Emulator");
                    });
                });
            });
        }

        private byte[] BuildDirectoryEntryPacket(int messageId, JadeDirectoryEntry entryObj)
        {
            string dn = $"CN={entryObj.SamAccountName},CN=Users,{_options.DomainDn}";
            return BerWriter.BuildLdapMessage(messageId, w =>
            {
                w.WriteConstructed(BerTag.SearchResultEntry, entry =>
                {
                    entry.WriteString(dn);
                    entry.WriteConstructed(BerTag.Sequence, attrs =>
                    {
                        WriteAttribute(attrs, "distinguishedName", dn);
                        WriteAttribute(attrs, "cn", entryObj.SamAccountName);
                        WriteAttribute(attrs, "sAMAccountName", entryObj.SamAccountName);
                        WriteAttribute(attrs, "userPrincipalName", entryObj.UserPrincipalName);
                        WriteAttribute(attrs, "userAccountControl", ((uint)entryObj.UserAccountControl).ToString(CultureInfo.InvariantCulture));
                        WriteAttribute(attrs, "dNT", entryObj.Dnt.ToString(CultureInfo.InvariantCulture));
                        WriteBinaryAttribute(attrs, "objectGUID", entryObj.ObjectGuid.ToByteArray());
                        if (entryObj.ObjectSid != null && entryObj.ObjectSid.Length > 0)
                        {
                            WriteBinaryAttribute(attrs, "objectSid", entryObj.ObjectSid);
                            WriteAttribute(attrs, "objectSidString", JadeSecurity.FormatSidString(entryObj.ObjectSid));
                        }
                    });
                });
            });
        }

        private static void WriteAttribute(BerWriter attrsWriter, string name, params string[] values)
        {
            attrsWriter.WriteConstructed(BerTag.Sequence, attr =>
            {
                attr.WriteString(name);
                attr.WriteConstructed(BerTag.Set, valSet =>
                {
                    foreach (string v in values)
                    {
                        valSet.WriteString(v);
                    }
                });
            });
        }

        private static void WriteBinaryAttribute(BerWriter attrsWriter, string name, byte[] value)
        {
            attrsWriter.WriteConstructed(BerTag.Sequence, attr =>
            {
                attr.WriteString(name);
                attr.WriteConstructed(BerTag.Set, valSet =>
                {
                    valSet.WriteOctetString(value);
                });
            });
        }

        private List<byte[]> HandleAddRequest(int messageId, BerReader opReader, bool isTlsEncrypted)
        {
            string entryDn = opReader.ReadString();
            var attrsSeq = opReader.ReadConstructed(BerTag.Sequence);

            string sam = JadeDatabase.ExtractSamAccountName(entryDn);
            string upn = string.Empty;
            uint uac = (uint)UserAccountControl.NormalAccount;
            byte[]? rawUnicodePwd = null;

            while (attrsSeq.HasData)
            {
                var attrSeq = attrsSeq.ReadConstructed(BerTag.Sequence);
                string attrType = attrSeq.ReadString();
                var valsSet = attrSeq.ReadConstructed(BerTag.Set);

                if (!valsSet.HasData) continue;

                if (attrType.Equals("sAMAccountName", StringComparison.OrdinalIgnoreCase))
                {
                    sam = valsSet.ReadString();
                }
                else if (attrType.Equals("userPrincipalName", StringComparison.OrdinalIgnoreCase))
                {
                    upn = valsSet.ReadString();
                }
                else if (attrType.Equals("userAccountControl", StringComparison.OrdinalIgnoreCase))
                {
                    string uacStr = valsSet.ReadString();
                    if (uint.TryParse(uacStr, out uint parsedUac))
                    {
                        uac = parsedUac;
                    }
                }
                else if (attrType.Equals("unicodePwd", StringComparison.OrdinalIgnoreCase))
                {
                    rawUnicodePwd = valsSet.ReadOctetString();
                }
            }

            /* Politica de seguranca do Active Directory: unicodePwd exige canal TLS */
            if (rawUnicodePwd != null && _options.RequireTlsForPasswordOperations && !isTlsEncrypted)
            {
                return new List<byte[]>
                {
                    BerWriter.BuildLdapMessage(messageId, w =>
                        w.WriteLdapResult(
                            BerTag.AddResponse,
                            LdapResultCode.UnwillingToPerform,
                            entryDn,
                            "00000052: LdapErr: DSID-0C090446, comment: In order to perform this operation a TLS connection must be established, data 0, v4563"))
                };
            }

            try
            {
                var newEntry = new JadeDirectoryEntry
                {
                    Pdnt = 2, /* CN=Users */
                    ObjectClass = JadeObjectClass.User,
                    SamAccountName = sam,
                    UserPrincipalName = string.IsNullOrEmpty(upn) ? $"{sam}@jade.local" : upn,
                    UserAccountControl = (UserAccountControl)uac,
                    ObjectGuid = Guid.NewGuid()
                };

                uint dnt = _db.AddObject(newEntry);

                if (rawUnicodePwd != null)
                {
                    _db.SetPasswordFromLdapBytes(dnt, rawUnicodePwd);
                }

                Log($"LDAP AddRequest DN='{entryDn}' (sAMAccountName='{sam}') -> Criado com DNT={dnt}");

                return new List<byte[]>
                {
                    BerWriter.BuildLdapMessage(messageId, w =>
                        w.WriteLdapResult(BerTag.AddResponse, LdapResultCode.Success))
                };
            }
            catch (JadeException jex) when (jex.IsRidExhausted)
            {
                Log($"LDAP AddRequest REJEITADO por esgotamento de RIDs (Win32 8209): DN='{entryDn}'");
                return new List<byte[]>
                {
                    BerWriter.BuildLdapMessage(messageId, w =>
                        w.WriteLdapResult(
                            BerTag.AddResponse,
                            LdapResultCode.UnwillingToPerform,
                            entryDn,
                            "00002011: SvcErr: DSID-031A124C, problem 5003 (WILL_NOT_PERFORM), data 8209"))
                };
            }
            catch (JadeException jex) when (jex.ErrorCode == JadeConstants.ErrDuplicateSam)
            {
                return new List<byte[]>
                {
                    BerWriter.BuildLdapMessage(messageId, w =>
                        w.WriteLdapResult(
                            BerTag.AddResponse,
                            LdapResultCode.EntryAlreadyExists,
                            entryDn,
                            $"00000524: UpdErr: DSID-031A11E5, problem 6005 (ENTRY_EXISTS), '{sam}'"))
                };
            }
            catch (Exception ex)
            {
                return new List<byte[]>
                {
                    BerWriter.BuildLdapMessage(messageId, w =>
                        w.WriteLdapResult(BerTag.AddResponse, LdapResultCode.OperationsError, entryDn, ex.Message))
                };
            }
        }

        private List<byte[]> HandleModifyRequest(int messageId, BerReader opReader, bool isTlsEncrypted)
        {
            string objectDn = opReader.ReadString();
            string sam = JadeDatabase.ExtractSamAccountName(objectDn);
            var existing = _db.FindBySamAccountName(sam);

            if (existing == null || existing.IsDeleted)
            {
                return new List<byte[]>
                {
                    BerWriter.BuildLdapMessage(messageId, w =>
                        w.WriteLdapResult(BerTag.ModifyResponse, LdapResultCode.NoSuchObject, objectDn, $"Objeto '{objectDn}' nao localizado."))
                };
            }

            var changesSeq = opReader.ReadConstructed(BerTag.Sequence);
            while (changesSeq.HasData)
            {
                var changeItem = changesSeq.ReadConstructed(BerTag.Sequence);
                int operation = changeItem.ReadEnumerated(); /* 0=Add, 1=Delete, 2=Replace */
                var modAttr = changeItem.ReadConstructed(BerTag.Sequence);
                string attrName = modAttr.ReadString();
                var valsSet = modAttr.ReadConstructed(BerTag.Set);

                if (!valsSet.HasData) continue;

                if (attrName.Equals("userAccountControl", StringComparison.OrdinalIgnoreCase))
                {
                    string uacVal = valsSet.ReadString();
                    if (uint.TryParse(uacVal, out uint newUac))
                    {
                        _db.ModifyUserAccountControl(existing.Dnt, (UserAccountControl)newUac);
                        Log($"LDAP ModifyRequest DN='{objectDn}' -> userAccountControl atualizado para {newUac}");
                    }
                }
                else if (attrName.Equals("unicodePwd", StringComparison.OrdinalIgnoreCase))
                {
                    if (_options.RequireTlsForPasswordOperations && !isTlsEncrypted)
                    {
                        return new List<byte[]>
                        {
                            BerWriter.BuildLdapMessage(messageId, w =>
                                w.WriteLdapResult(
                                    BerTag.ModifyResponse,
                                    LdapResultCode.UnwillingToPerform,
                                    objectDn,
                                    "00000052: LdapErr: DSID-0C090446, comment: In order to perform this operation a TLS connection must be established, data 0, v4563"))
                        };
                    }

                    byte[] pwdBytes = valsSet.ReadOctetString();
                    _db.SetPasswordFromLdapBytes(existing.Dnt, pwdBytes);
                    Log($"LDAP ModifyRequest DN='{objectDn}' -> unicodePwd redefinido sob canal seguro TLS.");
                }
            }

            return new List<byte[]>
            {
                BerWriter.BuildLdapMessage(messageId, w =>
                    w.WriteLdapResult(BerTag.ModifyResponse, LdapResultCode.Success))
            };
        }

        private List<byte[]> HandleDeleteRequest(int messageId, BerReader opReader)
        {
            byte[] dnBytes = opReader.ReadRemainingBytes();
            string targetDn = Encoding.UTF8.GetString(dnBytes);
            string sam = JadeDatabase.ExtractSamAccountName(targetDn);

            var existing = _db.FindBySamAccountName(sam);
            if (existing == null || existing.IsDeleted)
            {
                return new List<byte[]>
                {
                    BerWriter.BuildLdapMessage(messageId, w =>
                        w.WriteLdapResult(BerTag.DelResponse, LdapResultCode.NoSuchObject, targetDn, $"Objeto '{targetDn}' nao localizado."))
                };
            }

            _db.DeleteObject(existing.Dnt, tombstone: true);
            Log($"LDAP DelRequest DN='{targetDn}' (DNT={existing.Dnt}) -> Movido para Tombstone (isDeleted=1).");

            return new List<byte[]>
            {
                BerWriter.BuildLdapMessage(messageId, w =>
                    w.WriteLdapResult(BerTag.DelResponse, LdapResultCode.Success))
            };
        }

        private void Log(string message)
        {
            _options.Logger?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] [JADE-DC] {message}");
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                _cts.Cancel();

                try { _ldapListener?.Stop(); } catch { }
                try { _ldapsListener?.Stop(); } catch { }

                foreach (var kvp in _activeClients)
                {
                    try { kvp.Value.Dispose(); } catch { }
                }
                _activeClients.Clear();

                if (_ldapAcceptTask != null)
                {
                    try { await _ldapAcceptTask.ConfigureAwait(false); } catch { }
                }
                if (_ldapsAcceptTask != null)
                {
                    try { await _ldapsAcceptTask.ConfigureAwait(false); } catch { }
                }

                _serverCertificate.Dispose();
                _db.Dispose();
                _cts.Dispose();
            }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
