using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jade.Server.Ldap
{
    public static class BerTag
    {
        public const byte Boolean = 0x01;
        public const byte Integer = 0x02;
        public const byte OctetString = 0x04;
        public const byte Enumerated = 0x0A;
        public const byte Sequence = 0x30;
        public const byte Set = 0x31;

        /* Application Tags (RFC 4511) */
        public const byte BindRequest = 0x60;
        public const byte BindResponse = 0x61;
        public const byte UnbindRequest = 0x42;
        public const byte SearchRequest = 0x63;
        public const byte SearchResultEntry = 0x64;
        public const byte SearchResultDone = 0x65;
        public const byte ModifyRequest = 0x66;
        public const byte ModifyResponse = 0x67;
        public const byte AddRequest = 0x68;
        public const byte AddResponse = 0x69;
        public const byte DelRequest = 0x4A;
        public const byte DelResponse = 0x6B;
        public const byte ExtendedRequest = 0x77;
        public const byte ExtendedResponse = 0x78;
    }

    public enum LdapResultCode
    {
        Success = 0,
        OperationsError = 1,
        ProtocolError = 2,
        AuthMethodNotSupported = 7,
        NoSuchAttribute = 16,
        InvalidAttributeSyntax = 21,
        NoSuchObject = 32,
        InvalidDnSyntax = 34,
        InvalidCredentials = 49,
        InsufficientAccessRights = 50,
        Busy = 51,
        Unavailable = 52,
        UnwillingToPerform = 53,
        NamingViolation = 64,
        ObjectClassViolation = 65,
        EntryAlreadyExists = 68,
        Other = 80
    }

    public sealed class BerReader
    {
        private readonly byte[] _buffer;
        private int _position;
        private readonly int _end;

        public int Position => _position;
        public int Remaining => _end - _position;
        public bool HasData => _position < _end;

        public BerReader(byte[] buffer, int offset = 0, int? count = null)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _position = offset;
            _end = offset + (count ?? (buffer.Length - offset));
        }

        public byte PeekTag()
        {
            if (_position >= _end) throw new EndOfStreamException("Fim inesperado do pacote BER ao inspecionar tag.");
            return _buffer[_position];
        }

        public byte ReadTag()
        {
            if (_position >= _end) throw new EndOfStreamException("Fim inesperado do pacote BER ao ler tag.");
            return _buffer[_position++];
        }

        public int ReadLength()
        {
            if (_position >= _end) throw new EndOfStreamException("Fim inesperado do pacote BER ao ler tamanho.");
            byte first = _buffer[_position++];
            if ((first & 0x80) == 0)
            {
                return first;
            }

            int numOctets = first & 0x7F;
            if (numOctets == 0 || numOctets > 4)
            {
                throw new InvalidDataException($"Comprimento BER invalido ou indefinido (octetos: {numOctets}).");
            }

            if (_position + numOctets > _end)
            {
                throw new EndOfStreamException("Fim inesperado do pacote BER ao ler bytes de tamanho longo.");
            }

            int length = 0;
            for (int i = 0; i < numOctets; i++)
            {
                length = (length << 8) | _buffer[_position++];
            }

            return length;
        }

        public BerReader ReadConstructed(byte expectedTag)
        {
            byte actualTag = ReadTag();
            if (actualTag != expectedTag)
            {
                throw new InvalidDataException($"Tag BER inesperada. Esperada: 0x{expectedTag:X2}, Recebida: 0x{actualTag:X2}.");
            }

            int len = ReadLength();
            if (_position + len > _end)
            {
                throw new EndOfStreamException("Comprimento do elemento construido excede o buffer BER disponivel.");
            }

            var subReader = new BerReader(_buffer, _position, len);
            _position += len;
            return subReader;
        }

        public BerReader ReadAnyConstructed(out byte tag)
        {
            tag = ReadTag();
            int len = ReadLength();
            if (_position + len > _end)
            {
                throw new EndOfStreamException("Comprimento do elemento excede o buffer BER disponivel.");
            }

            var subReader = new BerReader(_buffer, _position, len);
            _position += len;
            return subReader;
        }

        public int ReadInteger(byte expectedTag = BerTag.Integer)
        {
            byte tag = ReadTag();
            if (tag != expectedTag)
            {
                throw new InvalidDataException($"Esperada tag inteira 0x{expectedTag:X2}, recebida 0x{tag:X2}.");
            }

            int len = ReadLength();
            if (len <= 0 || len > 4 || _position + len > _end)
            {
                throw new InvalidDataException($"Comprimento de inteiro BER invalido: {len}.");
            }

            int value = (_buffer[_position] & 0x80) != 0 ? -1 : 0;
            for (int i = 0; i < len; i++)
            {
                value = (value << 8) | _buffer[_position++];
            }

            return value;
        }

        public int ReadEnumerated()
        {
            return ReadInteger(BerTag.Enumerated);
        }

        public bool ReadBoolean()
        {
            byte tag = ReadTag();
            if (tag != BerTag.Boolean)
            {
                throw new InvalidDataException($"Esperada tag booleana 0x01, recebida 0x{tag:X2}.");
            }

            int len = ReadLength();
            if (len != 1 || _position + 1 > _end)
            {
                throw new InvalidDataException("Comprimento de booleano BER invalido.");
            }

            return _buffer[_position++] != 0;
        }

        public byte[] ReadOctetString(byte expectedTag = BerTag.OctetString)
        {
            byte tag = ReadTag();
            if (tag != expectedTag)
            {
                throw new InvalidDataException($"Esperada tag OctetString 0x{expectedTag:X2}, recebida 0x{tag:X2}.");
            }

            int len = ReadLength();
            if (len < 0 || _position + len > _end)
            {
                throw new EndOfStreamException("Comprimento de OctetString excede o buffer.");
            }

            byte[] data = new byte[len];
            Buffer.BlockCopy(_buffer, _position, data, 0, len);
            _position += len;
            return data;
        }

        public string ReadString(byte expectedTag = BerTag.OctetString)
        {
            byte[] bytes = ReadOctetString(expectedTag);
            return Encoding.UTF8.GetString(bytes);
        }

        public byte[] ReadRemainingBytes()
        {
            int len = Remaining;
            byte[] data = new byte[len];
            Buffer.BlockCopy(_buffer, _position, data, 0, len);
            _position = _end;
            return data;
        }

        public void SkipElement()
        {
            ReadTag();
            int len = ReadLength();
            _position = Math.Min(_end, _position + len);
        }

        /// <summary>
        /// Le um pacote LDAP BER completo a partir de um Stream de rede (TCP ou TLS).
        /// Retorna null caso a conexao seja encerrada limpa no inicio da leitura.
        /// </summary>
        public static async Task<byte[]?> ReadMessageBytesAsync(Stream stream, CancellationToken ct)
        {
            byte[] tagBuf = new byte[1];
            int read = await stream.ReadAsync(tagBuf.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0) return null;

            byte[] lenFirstBuf = new byte[1];
            await ReadExactAsync(stream, lenFirstBuf, 0, 1, ct).ConfigureAwait(false);

            byte firstLenByte = lenFirstBuf[0];
            int bodyLength;
            byte[] lenExtraBytes = Array.Empty<byte>();

            if ((firstLenByte & 0x80) == 0)
            {
                bodyLength = firstLenByte;
            }
            else
            {
                int numOctets = firstLenByte & 0x7F;
                if (numOctets <= 0 || numOctets > 4)
                {
                    throw new InvalidDataException($"Cabecalho de tamanho BER invalido: {numOctets} octetos.");
                }

                lenExtraBytes = new byte[numOctets];
                await ReadExactAsync(stream, lenExtraBytes, 0, numOctets, ct).ConfigureAwait(false);

                bodyLength = 0;
                for (int i = 0; i < numOctets; i++)
                {
                    bodyLength = (bodyLength << 8) | lenExtraBytes[i];
                }
            }

            if (bodyLength < 0 || bodyLength > 10 * 1024 * 1024)
            {
                throw new InvalidDataException($"Tamanho de pacote LDAP excede o limite de seguranca: {bodyLength} bytes.");
            }

            int headerSize = 2 + lenExtraBytes.Length;
            byte[] fullPacket = new byte[headerSize + bodyLength];
            fullPacket[0] = tagBuf[0];
            fullPacket[1] = firstLenByte;
            if (lenExtraBytes.Length > 0)
            {
                Buffer.BlockCopy(lenExtraBytes, 0, fullPacket, 2, lenExtraBytes.Length);
            }

            if (bodyLength > 0)
            {
                await ReadExactAsync(stream, fullPacket, headerSize, bodyLength, ct).ConfigureAwait(false);
            }

            return fullPacket;
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException("Conexao encerrada inesperadamente no meio da leitura de um pacote LDAP.");
                }
                totalRead += read;
            }
        }
    }

    public sealed class BerWriter
    {
        private readonly MemoryStream _stream = new MemoryStream();

        public void WriteTagAndLength(byte tag, int length)
        {
            _stream.WriteByte(tag);
            if (length < 0x80)
            {
                _stream.WriteByte((byte)length);
            }
            else if (length <= 0xFF)
            {
                _stream.WriteByte(0x81);
                _stream.WriteByte((byte)length);
            }
            else if (length <= 0xFFFF)
            {
                _stream.WriteByte(0x82);
                _stream.WriteByte((byte)(length >> 8));
                _stream.WriteByte((byte)(length & 0xFF));
            }
            else if (length <= 0xFFFFFF)
            {
                _stream.WriteByte(0x83);
                _stream.WriteByte((byte)(length >> 16));
                _stream.WriteByte((byte)((length >> 8) & 0xFF));
                _stream.WriteByte((byte)(length & 0xFF));
            }
            else
            {
                _stream.WriteByte(0x84);
                _stream.WriteByte((byte)(length >> 24));
                _stream.WriteByte((byte)((length >> 16) & 0xFF));
                _stream.WriteByte((byte)((length >> 8) & 0xFF));
                _stream.WriteByte((byte)(length & 0xFF));
            }
        }

        public void WriteInteger(int value, byte tag = BerTag.Integer)
        {
            byte[] bytes;
            if (value >= -128 && value <= 127)
            {
                bytes = new[] { (byte)value };
            }
            else if (value >= -32768 && value <= 32767)
            {
                bytes = new[] { (byte)(value >> 8), (byte)(value & 0xFF) };
            }
            else if (value >= -8388608 && value <= 8388607)
            {
                bytes = new[] { (byte)(value >> 16), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF) };
            }
            else
            {
                bytes = new[]
                {
                    (byte)(value >> 24),
                    (byte)((value >> 16) & 0xFF),
                    (byte)((value >> 8) & 0xFF),
                    (byte)(value & 0xFF)
                };
            }

            WriteTagAndLength(tag, bytes.Length);
            _stream.Write(bytes, 0, bytes.Length);
        }

        public void WriteEnumerated(int value)
        {
            WriteInteger(value, BerTag.Enumerated);
        }

        public void WriteBoolean(bool value)
        {
            WriteTagAndLength(BerTag.Boolean, 1);
            _stream.WriteByte(value ? (byte)0xFF : (byte)0x00);
        }

        public void WriteOctetString(byte[]? value, byte tag = BerTag.OctetString)
        {
            byte[] data = value ?? Array.Empty<byte>();
            WriteTagAndLength(tag, data.Length);
            if (data.Length > 0)
            {
                _stream.Write(data, 0, data.Length);
            }
        }

        public void WriteString(string? value, byte tag = BerTag.OctetString)
        {
            byte[] bytes = string.IsNullOrEmpty(value) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(value);
            WriteOctetString(bytes, tag);
        }

        public void WriteConstructed(byte tag, Action<BerWriter> innerAction)
        {
            var inner = new BerWriter();
            innerAction(inner);
            byte[] payload = inner.ToArray();
            WriteTagAndLength(tag, payload.Length);
            if (payload.Length > 0)
            {
                _stream.Write(payload, 0, payload.Length);
            }
        }

        public void WriteLdapResult(byte responseTag, LdapResultCode resultCode, string matchedDn = "", string diagnosticMessage = "")
        {
            WriteConstructed(responseTag, w =>
            {
                w.WriteEnumerated((int)resultCode);
                w.WriteString(matchedDn ?? string.Empty);
                w.WriteString(diagnosticMessage ?? string.Empty);
            });
        }

        public static byte[] BuildLdapMessage(int messageId, Action<BerWriter> protocolOpWriter)
        {
            var root = new BerWriter();
            root.WriteConstructed(BerTag.Sequence, msg =>
            {
                msg.WriteInteger(messageId);
                protocolOpWriter(msg);
            });
            return root.ToArray();
        }

        public byte[] ToArray()
        {
            return _stream.ToArray();
        }
    }
}
