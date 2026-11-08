using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Jade.Server.Security
{
    public static class TlsCertificateManager
    {
        private const string CertPassword = "JadeDcCertKeyPassword";

        /// <summary>
        /// Obtem ou gera um certificado digital X.509 RSA-2048 autoassinado compativel com o SChannel do Windows
        /// para operacoes LDAPS (Porta 636) e StartTLS (Porta 389).
        /// </summary>
        public static X509Certificate2 GetOrCreateServerCertificate(string storageDirectory, string dnsHostName = "dc01.jade.local")
        {
            string certDir = Path.Combine(storageDirectory, "certs");
            Directory.CreateDirectory(certDir);
            string pfxPath = Path.Combine(certDir, "jade_dc_ldaps.pfx");

            if (File.Exists(pfxPath))
            {
                try
                {
                    var existing = new X509Certificate2(pfxPath, CertPassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                    if (existing.NotAfter > DateTime.UtcNow.AddDays(7))
                    {
                        return existing;
                    }
                    existing.Dispose();
                }
                catch
                {
                    /* Regenera o certificado caso o arquivo existente esteja invalido */
                }
            }

            using var rsa = RSA.Create(2048);
            var subject = new X500DistinguishedName($"CN={dnsHostName}, OU=Domain Controllers, DC=jade, DC=local");
            var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, true));

            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    true));

            /* EKU: Server Authentication (1.3.6.1.5.5.7.3.1) */
            var ekuOids = new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(ekuOids, false));

            /* Subject Alternative Names (SAN) para validacao de hostname e loopback */
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(dnsHostName);
            sanBuilder.AddDnsName("jade.local");
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);
            request.CertificateExtensions.Add(sanBuilder.Build());

            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            DateTimeOffset notAfter = DateTimeOffset.UtcNow.AddYears(5);

            using X509Certificate2 ephemeralCert = request.CreateSelfSigned(notBefore, notAfter);
            byte[] pfxBytes = ephemeralCert.Export(X509ContentType.Pfx, CertPassword);

            try
            {
                File.WriteAllBytes(pfxPath, pfxBytes);
            }
            catch
            {
                /* Se o diretorio estiver bloqueado, prossegue em memoria */
            }

            /* No Windows SChannel, o certificado do servidor deve ser carregado a partir de PFX com chave privada persistivel */
            return new X509Certificate2(pfxBytes, CertPassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }
    }
}
