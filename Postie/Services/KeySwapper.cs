using Renci.SshNet;
using Renci.SshNet.Security;
using SshNet.Keygen.SshKeyEncryption;
using SshNet.Keygen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SshNet.Keygen.Extensions;

namespace Postie.Services
{
    public class KeySwapper
    {
        public async Task<string> SwapKeyAsync(string _host, string _authkey, string _username, string _passphrase)
        {
            var keyInfo = new SshKeyGenerateInfo { Encryption = new SshKeyEncryptionAes256("my passphrase") };
            var privateKey = SshKey.Generate(keyInfo);
            var publicSshKeyWithComment = privateKey.ToPublic();
            var fingerprint = privateKey.Fingerprint();

            Console.WriteLine($"Fingerprint: {fingerprint}");

            string host = _host;
            string username = _username;
            string password = _passphrase;

            using (var client = new SshClient(host, username, password))
            {
                try
                {
                    CancellationToken cancellationToken = new CancellationToken();
                    await client.ConnectAsync(cancellationToken);

                    if (client.IsConnected)
                    {
                        Console.WriteLine("Connected to SSH server.");
                        Console.WriteLine("Replacing old keys and inserting new one.");
                        var command = client.RunCommand("cat ~/.ssh/authorized_keys");
                        command = client.RunCommand("echo \"" + publicSshKeyWithComment + "\" > ~/.ssh/authorized_keys");

                        client.Disconnect();
                        Console.WriteLine("Disconnected from SSH server.");

                        byte[] privateKeyBytes = Encoding.ASCII.GetBytes(privateKey.ToOpenSshFormat());
                        using (MemoryStream privateKeyStream = new MemoryStream(privateKeyBytes))
                        {
                            PrivateKeyFile privateKeyFile = new PrivateKeyFile(privateKeyStream);
                            ConnectionInfo connectionInfo = new ConnectionInfo(host, username, new PrivateKeyAuthenticationMethod(username, privateKeyFile));
                            using (var pclient = new SshClient(connectionInfo))
                            {
                                try
                                {
                                    await pclient.ConnectAsync(cancellationToken);

                                    if (pclient.IsConnected)
                                    {
                                        Console.WriteLine("Authtest Successful.");
                                        command = pclient.RunCommand("cat ~/.ssh/authorized_keys");

                                        bool _foundkey = false;
                                        List<Key> keys = ParseAuthorizedKeys(@publicSshKeyWithComment);

                                        foreach (Key key in keys)
                                        {
                                            if (key.KeyValue == ParseAuthorizedKeys(@publicSshKeyWithComment)[0].KeyValue)
                                            {
                                                _foundkey = true;
                                            }
                                        }

                                        if (_foundkey)
                                        {
                                            Console.WriteLine("KeySwap Successful");
                                            return "KeySwap Successful";
                                        }
                                        else
                                        {
                                            Console.WriteLine("KeySwap error: not found after copy");
                                            return "KeySwap error: not found after copy";
                                            throw new Exception("KeySwap error: not found after copy");
                                        }

                                        pclient.Disconnect();
                                    }
                                    else
                                    {
                                        Console.WriteLine("Authtest Failed to connect.");
                                        return "Authtest Failed to connect.";
                                        throw new Exception("Authtest Failed: to connect.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Authtest Failed. Error: {ex.Message}");
                                    return $"Authtest Failed. Error: {ex.Message}";
                                    throw new Exception($"Authtest Failed. Error: {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Failed to connect to SSH server.");
                        return "Failed to connect to SSH server.";
                        throw new Exception("Failed to connect to SSH server.");

                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    return $"Error: {ex.Message}";
                }
            }
        }

        static List<Key> ParseAuthorizedKeys(string authorizedKeysString)
        {
            List<Key> keys = new List<Key>();

            string[] lines = authorizedKeysString.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string type = parts[0];
                    string key = parts[1];
                    string comment = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "";

                    keys.Add(new Key(type, key, comment));
                }
            }

            return keys;
        }

        class Key
        {
            public string Type { get; }
            public string KeyValue { get; }
            public string Comment { get; }

            public Key(string type, string keyValue, string comment)
            {
                Type = type;
                KeyValue = keyValue;
                Comment = comment;
            }
        }
    }
}
