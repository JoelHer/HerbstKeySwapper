using Renci.SshNet;
using SshNet.Keygen.SshKeyEncryption;
using SshNet.Keygen;
using System;
using SshNet.Keygen.Extensions;

namespace AsyncTest
{
    public class JumpHostTest
    {
        public void jumpConnect()
        {
            var firstClient = new SshClient("192.168.178.57", "herbst", "Herbst710");
            firstClient.Connect();

            var port = new ForwardedPortLocal("127.0.0.1", "81.16.61.19", 22);
            firstClient.AddForwardedPort(port);
            port.Start();

            var privateKeyFile = new PrivateKeyFile(@"C:\Users\itsba\.ssh\keystore\private_key_81_16_61_19.txt", "my passphrase");
            var keyFiles = new[] { privateKeyFile };

            var secondConnectionInfo = new ConnectionInfo("127.0.0.1", 22, "root", new PrivateKeyAuthenticationMethod("root", keyFiles));
            var secondClient = new SshClient(secondConnectionInfo);
            secondClient.Connect();

            var command = secondClient.CreateCommand("ls");
            var result = command.Execute();
            Console.WriteLine(result);

            secondClient.Disconnect();
            firstClient.Disconnect();
        }

        public void connectWithPK()
        {
            string host = "81.16.61.19";
            string username = "herbst";
            string privateKeyFilePath = @"C:\Users\itsba\.ssh\keystore\private_key_192_168_178_57.txt";
            string passphrase = "";

            var keyInfo = new SshKeyGenerateInfo
            {
                Encryption = new SshKeyEncryptionAes256("12345")
            };
            var key = SshKey.Generate("test.key", FileMode.Create, keyInfo);

            var publicKey = key.ToPublic();
            var fingerprint = key.Fingerprint();

            Console.WriteLine("Fingerprint: {0}", fingerprint);
            Console.WriteLine("Add this to your .ssh/authorized_keys on the SSH Server: {0}", publicKey);
            Console.ReadLine();

            using var client = new SshClient(host, "root", key);
            client.Connect();
            Console.WriteLine(client.RunCommand("hostname").Result);


        }
    }
}
