using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Jade.Core
{
    public static class JadeSecurity
    {
        private static readonly byte[] DomainSalt = Encoding.UTF8.GetBytes("JADE-NTDS-PEK-SALT-V1");

        /* Subautoridades fixas simulando o Domain SID S-1-5-21-2485197234-1849201742-3194827109 */
        private const uint DomainSubAuth1 = 2485197234;
        private const uint DomainSubAuth2 = 1849201742;
        private const uint DomainSubAuth3 = 3194827109;

        /// <summary>
        /// Decodifica um payload unicodePwd padrao do Active Directory (UTF-16LE entre aspas duplas)
        /// ou texto simples, gerando o hash criptografico seguro para armazenamento no NTDS.DIT.
        /// </summary>
        public static byte[] ComputePasswordHash(string rawPassword)
        {
            if (rawPassword == null) throw new ArgumentNullException(nameof(rawPassword));

            string normalized = TrimActiveDirectoryQuotes(rawPassword);
            byte[] utf16Bytes = Encoding.Unicode.GetBytes(normalized);

            using var hmac = new HMACSHA256(DomainSalt);
            byte[] hash = hmac.ComputeHash(utf16Bytes);

            /* Prefixo identificador de versao de hash PEK (4 bytes) + 32 bytes SHA-256 = 36 bytes */
            byte[] stored = new byte[36];
            stored[0] = (byte)'J';
            stored[1] = (byte)'P';
            stored[2] = (byte)'E';
            stored[3] = 0x01;
            Buffer.BlockCopy(hash, 0, stored, 4, 32);

            return stored;
        }

        /// <summary>
        /// Processa bytes brutos recebidos no atributo LDAP unicodePwd.
        /// </summary>
        public static byte[] ComputePasswordHashFromLdapBytes(byte[] ldapPasswordBytes)
        {
            if (ldapPasswordBytes == null || ldapPasswordBytes.Length == 0)
            {
                return ComputePasswordHash(string.Empty);
            }

            /* Verifica se esta codificado em UTF-16LE com aspas duplas (0x22 0x00 ... 0x22 0x00) */
            if (ldapPasswordBytes.Length >= 4 &&
                ldapPasswordBytes.Length % 2 == 0 &&
                ldapPasswordBytes[0] == 0x22 && ldapPasswordBytes[1] == 0x00 &&
                ldapPasswordBytes[ldapPasswordBytes.Length - 2] == 0x22 && ldapPasswordBytes[ldapPasswordBytes.Length - 1] == 0x00)
            {
                string decodedUtf16 = Encoding.Unicode.GetString(ldapPasswordBytes, 2, ldapPasswordBytes.Length - 4);
                return ComputePasswordHash(decodedUtf16);
            }

            string decodedUtf8 = Encoding.UTF8.GetString(ldapPasswordBytes);
            return ComputePasswordHash(decodedUtf8);
        }

        /// <summary>
        /// Valida uma senha candidata em tempo constante contra o hash armazenado no banco ESE.
        /// </summary>
        public static bool VerifyPassword(byte[]? storedBytes, string candidatePassword)
        {
            if (storedBytes == null || storedBytes.Length == 0 || candidatePassword == null)
            {
                return false;
            }

            /* Se o registro possui o cabecalho JPE\x01, compara com o hash HMAC-SHA256 */
            if (storedBytes.Length == 36 &&
                storedBytes[0] == (byte)'J' &&
                storedBytes[1] == (byte)'P' &&
                storedBytes[2] == (byte)'E' &&
                storedBytes[3] == 0x01)
            {
                byte[] expected = ComputePasswordHash(candidatePassword);
                return CryptographicOperations.FixedTimeEquals(storedBytes, expected);
            }

            /* Fallback para senhas gravadas em bytes brutos */
            byte[] candidateRaw = Encoding.UTF8.GetBytes(candidatePassword);
            return CryptographicOperations.FixedTimeEquals(storedBytes, candidateRaw);
        }

        public static string TrimActiveDirectoryQuotes(string input)
        {
            if (input.Length >= 2 && input.StartsWith("\"") && input.EndsWith("\""))
            {
                return input.Substring(1, input.Length - 2);
            }
            return input;
        }

        /// <summary>
        /// Constroi um identificador de seguranca binario Win32 (SID) no formato S-1-5-21-D1-D2-D3-RID.
        /// </summary>
        public static byte[] BuildBinarySid(uint rid)
        {
            /* 1 (Rev) + 1 (Count=5) + 6 (Authority=5) + 5 * 4 (SubAuthorities) = 28 bytes */
            byte[] sid = new byte[28];
            sid[0] = 0x01; /* Revision */
            sid[1] = 0x05; /* SubAuthorityCount */
            sid[2] = 0x00;
            sid[3] = 0x00;
            sid[4] = 0x00;
            sid[5] = 0x00;
            sid[6] = 0x00;
            sid[7] = 0x05; /* SECURITY_NT_AUTHORITY (5) */

            BinaryPrimitives.WriteUInt32LittleEndian(sid.AsSpan(8, 4), 21u);
            BinaryPrimitives.WriteUInt32LittleEndian(sid.AsSpan(12, 4), DomainSubAuth1);
            BinaryPrimitives.WriteUInt32LittleEndian(sid.AsSpan(16, 4), DomainSubAuth2);
            BinaryPrimitives.WriteUInt32LittleEndian(sid.AsSpan(20, 4), DomainSubAuth3);
            BinaryPrimitives.WriteUInt32LittleEndian(sid.AsSpan(24, 4), rid);

            return sid;
        }

        /// <summary>
        /// Converte um SID binario para a representacao textual padrao S-1-5-21-...
        /// </summary>
        public static string FormatSidString(byte[]? sid)
        {
            if (sid == null || sid.Length < 8)
            {
                return string.Empty;
            }

            byte revision = sid[0];
            byte subAuthCount = sid[1];
            ulong authority = 0;
            for (int i = 2; i < 8; i++)
            {
                authority = (authority << 8) | sid[i];
            }

            var sb = new StringBuilder();
            sb.Append($"S-{revision}-{authority}");

            int offset = 8;
            for (int i = 0; i < subAuthCount && offset + 4 <= sid.Length; i++)
            {
                uint subAuth = BinaryPrimitives.ReadUInt32LittleEndian(sid.AsSpan(offset, 4));
                sb.Append($"-{subAuth}");
                offset += 4;
            }

            return sb.ToString();
        }
    }
}
