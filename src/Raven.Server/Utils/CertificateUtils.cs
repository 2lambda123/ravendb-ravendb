﻿using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using JetBrains.Annotations;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;
using Sparrow.Platform;
using BigInteger = Org.BouncyCastle.Math.BigInteger;
using X509Certificate = Org.BouncyCastle.X509.X509Certificate;

namespace Raven.Server.Utils
{
    internal class CertificateUtils
    {
        public static X509Certificate2 CreateSelfSignedCertificate(string commonNameValue, string issuerName)
        {
            CreateCertificateAuthorityCertificate(commonNameValue + " CA", out var ca, out var caSubjectName);
            var selfSignedCertificateBasedOnPrivateKey = CreateSelfSignedCertificateBasedOnPrivateKey(commonNameValue, caSubjectName, ca, false, false, 1, out _);
            selfSignedCertificateBasedOnPrivateKey.Verify();
            return selfSignedCertificateBasedOnPrivateKey;
        }

        public static void RegisterCertificateInOperatingSystem(X509Certificate2 cert)
        {
            if (cert.HasPrivateKey) // the check if made anyway, to ensure we never fail on just these environments
                throw new InvalidOperationException("When registering the certificate for the purpose of TRUSTED_ISSUERS, we don't want the private key");

            if (PlatformDetails.RunningOnPosix == false)
            {
                if (Environment.OSVersion.Version.Major >= 6 &&
                    Environment.OSVersion.Version.Minor > 1)
                    return; // windows 8 does not need this
            }

            // due to the way TRUSTED_ISSUERS work in Linux and Windows previous to Win 7
            // we need to register the certificate in the operating system so the SSL impl
            // will send the appropriate signers.
            // At least on Linux, this is done by looking at the _issuers_ of certs in the 
            // root store
            using (var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadWrite);

                foreach (var certificate in store.Certificates)
                {
                    if (certificate.Issuer == cert.Issuer)
                        return; // something with the same issuer is already there, can skip
                }

                store.Add(cert);
            }
        }

        public static X509Certificate2 CreateSelfSignedClientCertificate(string commonNameValue, RavenServer.CertificateHolder certificateHolder, out byte[] certBytes)
        {
            var serverCertBytes = certificateHolder.Certificate.Export(X509ContentType.Cert);
            var readCertificate = new X509CertificateParser().ReadCertificate(serverCertBytes);
            CreateSelfSignedCertificateBasedOnPrivateKey(
                commonNameValue,
                readCertificate.SubjectDN,
                (certificateHolder.PrivateKey.Key, readCertificate.GetPublicKey()),
                true,
                false,
                5,
                out certBytes);


            ValidateNoPrivateKeyInServerCert(serverCertBytes);

            var collection = new X509Certificate2Collection();
            collection.Import(certBytes, null, X509KeyStorageFlags.Exportable);
            collection.Import(serverCertBytes);

            certBytes = collection.Export(X509ContentType.Pfx);

            RegisterCertificateInOperatingSystem(new X509Certificate2(collection.Export(X509ContentType.Cert)));
            return new X509Certificate2(certBytes, (string)null, X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
        }

        private static void ValidateNoPrivateKeyInServerCert(byte[] serverCertBytes)
        {
            var collection = new X509Certificate2Collection();
            // without the server private key here
            collection.Import(serverCertBytes);

            if (new X509Certificate2Collection().OfType<X509Certificate2>().FirstOrDefault(x => x.HasPrivateKey) != null)
                throw new InvalidOperationException("After export of CERT, still have private key from signer in certificate, should NEVER happen");
        }

        public static X509Certificate2 CreateSelfSignedExpiredClientCertificate(string commonNameValue, RavenServer.CertificateHolder certificateHolder)
        {
            var readCertificate = new X509CertificateParser().ReadCertificate(certificateHolder.Certificate.Export(X509ContentType.Cert));
            
            return CreateSelfSignedCertificateBasedOnPrivateKey(
                commonNameValue,
                readCertificate.SubjectDN,
                (certificateHolder.PrivateKey.Key, readCertificate.GetPublicKey()),
                true,
                false,
                -1,
                out _);
        }

        public static X509Certificate2 CreateSelfSignedCertificateBasedOnPrivateKey(string commonNameValue, 
            X509Name issuer, 
            (AsymmetricKeyParameter PrivateKey, AsymmetricKeyParameter PublicKey) key,
            bool isClientCertificate,
            bool isCaCertificate,
            int yearsUntilExpiration,
            out byte[] certBytes)
        {
            const int keyStrength = 2048;

            // Generating Random Numbers
            var random = GetSeededSecureRandom();
            ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA512WITHRSA", key.PrivateKey, random);

            // The Certificate Generator
            X509V3CertificateGenerator certificateGenerator = new X509V3CertificateGenerator();
            var authorityKeyIdentifier = new AuthorityKeyIdentifierStructure(key.PublicKey);
            certificateGenerator.AddExtension(X509Extensions.AuthorityKeyIdentifier.Id, false, authorityKeyIdentifier);

            if (isClientCertificate)
            {
                certificateGenerator.AddExtension(X509Extensions.ExtendedKeyUsage.Id, true, new ExtendedKeyUsage(KeyPurposeID.IdKPClientAuth));
            }
            else
            {
                certificateGenerator.AddExtension(X509Extensions.ExtendedKeyUsage.Id, true,
                    new ExtendedKeyUsage(KeyPurposeID.IdKPServerAuth, KeyPurposeID.IdKPClientAuth));
            }

            if (isCaCertificate)
            {
                certificateGenerator.AddExtension(X509Extensions.BasicConstraints.Id, true, new BasicConstraints(0));
                certificateGenerator.AddExtension(X509Extensions.KeyUsage.Id, false,
                    new X509KeyUsage(X509KeyUsage.KeyCertSign | X509KeyUsage.CrlSign));
            }

            // Serial Number
            BigInteger serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(Int64.MaxValue), random);
            certificateGenerator.SetSerialNumber(serialNumber);

            // Issuer and Subject Name
         
            X509Name subjectDN = new X509Name("CN=" + commonNameValue);
            certificateGenerator.SetIssuerDN(issuer);
            certificateGenerator.SetSubjectDN(subjectDN);

            // Valid For
            DateTime notBefore = DateTime.UtcNow.Date.AddDays(-7);
            DateTime notAfter = notBefore.AddYears(yearsUntilExpiration);
            certificateGenerator.SetNotBefore(notBefore);
            certificateGenerator.SetNotAfter(notAfter);

            // Subject Public Key
            var keyGenerationParameters = new KeyGenerationParameters(random, keyStrength);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);
            var subjectKeyPair = keyPairGenerator.GenerateKeyPair();

            certificateGenerator.SetPublicKey(subjectKeyPair.Public);

            X509Certificate certificate = certificateGenerator.Generate(signatureFactory);
            var store = new Pkcs12Store();
            string friendlyName = certificate.SubjectDN.ToString();
            var certificateEntry = new X509CertificateEntry(certificate);
            store.SetCertificateEntry(friendlyName, certificateEntry);
            store.SetKeyEntry(friendlyName, new AsymmetricKeyEntry(subjectKeyPair.Private), new[] { certificateEntry });
            var stream = new MemoryStream();
            store.Save(stream, new char[0], random);
            certBytes = stream.ToArray();
            var convertedCertificate =
                new X509Certificate2(
                    certBytes, (string)null,
                    X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            stream.Position = 0;

            return convertedCertificate;
        }

        public static void CreateCertificateAuthorityCertificate(string commonNameValue, 
            out (AsymmetricKeyParameter PrivateKey, AsymmetricKeyParameter PublicKey) ca,
            out X509Name name)
        {
            const int keyStrength = 2048;

            var random = GetSeededSecureRandom();

            // The Certificate Generator
            X509V3CertificateGenerator certificateGenerator = new X509V3CertificateGenerator();

            // Serial Number
            BigInteger serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(Int64.MaxValue), random);
            certificateGenerator.SetSerialNumber(serialNumber);

            // Issuer and Subject Name
            X509Name subjectDN = new X509Name("CN=" + commonNameValue);
            X509Name issuerDN = subjectDN;
            certificateGenerator.SetIssuerDN(issuerDN);
            certificateGenerator.SetSubjectDN(subjectDN);

            // Valid For
            DateTime notBefore = DateTime.UtcNow.Date.AddDays(-7);
            DateTime notAfter = notBefore.AddYears(2);

            certificateGenerator.SetNotBefore(notBefore);
            certificateGenerator.SetNotAfter(notAfter);

            // Subject Public Key
            var keyGenerationParameters = new KeyGenerationParameters(random, keyStrength);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);
            var subjectKeyPair = keyPairGenerator.GenerateKeyPair();

            certificateGenerator.SetPublicKey(subjectKeyPair.Public);

            // Generating the Certificate
            var issuerKeyPair = subjectKeyPair;
            ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA512WITHRSA", issuerKeyPair.Private, random);

            // selfsign certificate
            var certificate = certificateGenerator.Generate(signatureFactory);

            ca = (issuerKeyPair.Private, issuerKeyPair.Public);
            name = certificate.SubjectDN;
        }

        public static SecureRandom GetSeededSecureRandom()
        {
            var buffer = new byte[32];
            using (var cryptoRandom = RandomNumberGenerator.Create())
            {
                cryptoRandom.GetBytes(buffer);
            }
            var randomGenerator = new VmpcRandomGenerator();
            randomGenerator.AddSeedMaterial(buffer);
            SecureRandom random = new SecureRandom(randomGenerator);
            return random;
        }
    }
}
