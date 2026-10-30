using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System;
using System.Linq;
using System.Collections.Generic;

namespace MelonLoader.Installer.Core.Utilities.Signing
{
    public class APKSigner
    {
        private X509Certificate _xCert;
        private AsymmetricKeyParameter _privateKey;
        private readonly SHA256 _sha;

        private readonly IPatchLogger _logger;

        public APKSigner(string pemData, IPatchLogger patchLogger)
        {
            _sha = SHA256.Create();
            _logger = patchLogger;

            (_xCert, _privateKey) = LoadCerts(pemData);
        }

        private (X509Certificate, AsymmetricKeyParameter) LoadCerts(string pemData)
        {
            _logger.Log("Reading certificates");
            X509Certificate? certificate = null;
            AsymmetricKeyParameter? privateKey = null;

            using (var reader = new StringReader(pemData))
            {

                var pemReader = new PemReader(reader);
                object? pemObject;
                while ((pemObject = pemReader.ReadObject()) != null)
                {
                    certificate ??= pemObject as X509Certificate;
                    privateKey ??= (pemObject as AsymmetricCipherKeyPair)?.Private;
                }
            }

            if (certificate == null)
                throw new System.Security.SecurityException("Certificate could not be loaded from PEM data.");

            if (privateKey == null)
                throw new System.Security.SecurityException("Private Key could not be loaded from PEM data.");
            return (certificate, privateKey);
        }

        public void Sign(string apkPath)
        {

            _logger.Log("Aligning");
            APKAligner.AlignApk(apkPath);

            _logger.Log("Signing with V2");
            SignV2(apkPath);

            _logger.Log("Done");
        }

        #region V2

        private void SignV2(string path)
        {
            FileStream fs = new(path, FileMode.Open);
            using FileMemory memory = new(fs);
            using MemoryStream ms = new();
            using FileMemory outMemory = new(ms);
            memory.Position = memory.Length() - 22;
            while (memory.ReadInt() != EndOfCentralDirectory.SIGNATURE)
            {
                memory.Position -= 4 + 1;
            }
            memory.Position -= 4;
            var eocdPosition = memory.Position;
            EndOfCentralDirectory eocd = new(memory);
            var cd = eocd.OffsetOfCD;
            var section1 = GetSectionDigests(fs, 0, cd);
            var section3 = GetSectionDigests(fs, cd, eocdPosition);
            var section4 = GetSectionDigests(fs, eocdPosition, fs.Length);

            var digestChunks = section1.Concat(section3).Concat(section4).ToList();

            byte[] bytes = new byte[1 + 4];
            bytes[0] = 0x5a;
            byte[] sizeBytes = BitConverter.GetBytes((uint)digestChunks.Count);
            bytes[1] = sizeBytes[0];
            bytes[2] = sizeBytes[1];
            bytes[3] = sizeBytes[2];
            bytes[4] = sizeBytes[3];
            var digest = _sha.ComputeHash([.. bytes, .. digestChunks.Aggregate((a, b) => a.Concat(b).ToArray())]);

            uint algorithm = 0x0103;

            APKSignatureSchemeV2 block = new();
            APKSignatureSchemeV2.Signer signer = new();

            using MemoryStream signedDataMs = new();
            using FileMemory memorySignedData = new(signedDataMs);
            var signedData = new APKSignatureSchemeV2.Signer.BlockSignedData();
            signedData.Digests.Add(new APKSignatureSchemeV2.Signer.BlockSignedData.Digest(algorithm, digest));

            signedData.Certificates.Add(_xCert.GetEncoded());

            signedData.Write(memorySignedData);
            signer.SignedData = signedDataMs.ToArray();

            ISigner signerType = SignerUtilities.GetSigner("SHA256WithRSA");
            signerType.Init(true, _privateKey);
            signerType.BlockUpdate(signer.SignedData, 0, signer.SignedData.Length);

            signer.Signatures.Add(new APKSignatureSchemeV2.Signer.BlockSignature(algorithm, signerType.GenerateSignature()));
            signer.PublicKey = _xCert.CertificateStructure.SubjectPublicKeyInfo.GetDerEncoded();
            block.Signers.Add(signer);

            APKSigningBlock signingBlock = new();
            signingBlock.Values.Add(block.ToIDValuePair());

            fs.Position = 0;
            outMemory.WriteBytes(memory.ReadBytes(cd));
            signingBlock.Write(outMemory);
            eocd.OffsetOfCD = (int)ms.Position;
            outMemory.WriteBytes(memory.ReadBytes((int)(eocdPosition - cd)));
            eocd.Write(outMemory);

            fs.SetLength(0);
            ms.Position = 0;
            ms.CopyTo(fs);
            fs.Close();
        }

        private List<byte[]> GetSectionDigests(FileStream fs, long startOffset, long endOffset)
        {
            var digests = new List<byte[]>();
            int chunkSize = 1024 * 1024;
            for (long i = startOffset; i < endOffset; i += chunkSize)
            {
                fs.Position = i;
                var size = Math.Min(endOffset - i, chunkSize);
                byte[] bytes = new byte[1 + 4 + size];
                bytes[0] = 0xa5;
                byte[] sizeBytes = BitConverter.GetBytes((uint)size);
                bytes[1] = sizeBytes[0];
                bytes[2] = sizeBytes[1];
                bytes[3] = sizeBytes[2];
                bytes[4] = sizeBytes[3];
                fs.ReadExactly(bytes, 5, (int)size);
                digests.Add(_sha.ComputeHash(bytes));
            }
            return digests;
        }

        #endregion
    }
}
