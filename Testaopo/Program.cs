
using SshNet.Keygen.SshKeyEncryption;
using SshNet.Keygen;
using SshNet.Keygen.Extensions;
using Renci.SshNet;
using PostieKeyCreator;


namespace Testaopo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            KeyCreator keyCreator = new KeyCreator();
            KeyPair keyPair = keyCreator.CreateKeyPair("password");
            
            
        }
    }
}
