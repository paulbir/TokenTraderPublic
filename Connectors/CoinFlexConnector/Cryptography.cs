using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using SharedTools;

namespace CoinFlexConnector
{
    static class Cryptography
    {
        public static (string, string, string) CreateAuthStrings(long userId, string passPhrase, string serverNonce)
        {
            byte[] serverNonceBytes = Convert.FromBase64String(serverNonce);
            byte[] clientNonceBytes = MakeClientNonce(out string clientNonce);
            byte[] userIdBytes = MakeUserIdBigEndianBytes(userId);

            IDigest digest = new Sha224Digest();
            byte[] toSignBytes = MakeToSignBytes(userIdBytes, serverNonceBytes, clientNonceBytes);
            byte[] toSignHashBytes = HashToSignBytes(digest, toSignBytes);

            byte[] passPhraseBytes = Encoding.UTF8.GetBytes(passPhrase);
            byte[] seedBytes = MakeSeedBytes(userIdBytes, passPhraseBytes);
            byte[] privateKeyBytes = HashSeedToPrivateKeyBytes(digest, seedBytes);

            ECDsaSigner signer = CreateSigner(digest, privateKeyBytes);
            (string r, string s) = MakeSignature(signer, toSignHashBytes);

            return (clientNonce, r, s);
        }

        static byte[] MakeClientNonce(out string clientNonce)
        {
            var rnd = new RNGCryptoServiceProvider();
            var clientNonceBytes = new byte[16];
            rnd.GetBytes(clientNonceBytes);
            clientNonce = Convert.ToBase64String(clientNonceBytes);

            return clientNonceBytes;
        }

        static byte[] MakeUserIdBigEndianBytes(long userId)
        {
            DataConverter enc = DataConverter.BigEndian;
            byte[] userIdBytes = enc.GetBytes(userId);

            return userIdBytes;
        }

        static byte[] MakeToSignBytes(byte[] userIdBytes, byte[] serverNonceBytes, byte[] clientNonceBytes)
        {
            var toSignBytes = new byte[userIdBytes.Length + serverNonceBytes.Length + clientNonceBytes.Length];
            Buffer.BlockCopy(userIdBytes, 0, toSignBytes, 0, userIdBytes.Length);
            Buffer.BlockCopy(serverNonceBytes, 0, toSignBytes, userIdBytes.Length, serverNonceBytes.Length);
            Buffer.BlockCopy(clientNonceBytes, 0, toSignBytes, userIdBytes.Length + serverNonceBytes.Length, clientNonceBytes.Length);

            return toSignBytes;
        }

        static byte[] HashToSignBytes(IDigest digest, byte[] toSignBytes)
        {
            var toSignHashBytes = new byte[digest.GetDigestSize()];
            digest.BlockUpdate(toSignBytes, 0, toSignBytes.Length);
            digest.DoFinal(toSignHashBytes, 0);

            return toSignHashBytes;
        }

        static byte[] MakeSeedBytes(byte[] userIdBytes, byte[] passPhraseBytes)
        {
            byte[] seedBytes = new byte[userIdBytes.Length + passPhraseBytes.Length];
            Buffer.BlockCopy(userIdBytes, 0, seedBytes, 0, userIdBytes.Length);
            Buffer.BlockCopy(passPhraseBytes, 0, seedBytes, userIdBytes.Length, passPhraseBytes.Length);

            return seedBytes;
        }

        static byte[] HashSeedToPrivateKeyBytes(IDigest digest, byte[] seedBytes)
        {
            var privateKeyBytes = new byte[digest.GetDigestSize()];
            digest.BlockUpdate(seedBytes, 0, seedBytes.Length);
            digest.DoFinal(privateKeyBytes, 0);

            return privateKeyBytes;
        }

        static ECDsaSigner CreateSigner(IDigest digest, byte[] privateKeyBytes)
        {
            X9ECParameters curve = ECNamedCurveTable.GetByName("secp224k1");
            var domain = new ECDomainParameters(curve.Curve, curve.G, curve.N);

            var privateKeyBigInt = new BigInteger(1, privateKeyBytes);
            var signerParameters = new ECPrivateKeyParameters(privateKeyBigInt, domain);

            var signer = new ECDsaSigner();
            signer.Init(true, signerParameters);

            return signer;
        }

        static (string r, string s) MakeSignature(ECDsaSigner signer, byte[] toSignHashBytes)
        {
            BigInteger[] signature = signer.GenerateSignature(toSignHashBytes);

            byte[] rBytes = signature[0].ToByteArray().SkipWhile(b => b == 0x00).ToArray();
            byte[] sBytes = signature[1].ToByteArray().SkipWhile(b => b == 0x00).ToArray();

            string r = Convert.ToBase64String(rBytes);
            string s = Convert.ToBase64String(sBytes);

            return (r, s);
        }
    }
}