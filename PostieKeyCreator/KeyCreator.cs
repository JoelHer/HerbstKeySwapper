using SshNet.Keygen;
using SshNet.Keygen.Extensions;
using SshNet.Keygen.SshKeyEncryption;

namespace PostieKeyCreator
{
    public class KeyCreator
    {
        public KeyPair CreateKeyPair(string passphrase)
        {
            var keyInfo = new SshKeyGenerateInfo { Encryption = new SshKeyEncryptionAes256(passphrase) };
            var privateKey = SshKey.Generate(keyInfo);
            var publicKey = privateKey.ToPublic();
            var fingerprint = privateKey.Fingerprint();

            return new KeyPair(privateKey.ToOpenSshFormat(), publicKey, fingerprint);
        }
    }

    public class KeyPair
    {
        public KeyPair(string privateKey, string publicKey, string fingerprint)
        {
            PrivateKey = privateKey;
            PublicKey = publicKey;
            Fingerprint = fingerprint;
        }

        public string Fingerprint { get; set; }
        public string PublicKey { get; set; }
        public string PrivateKey { get; set; }
    }
}
