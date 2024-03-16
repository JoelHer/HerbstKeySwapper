using Renci.SshNet;
using SshNet.Keygen;
using SshNet.Keygen.Extensions;
using SshNet.Keygen.SshKeyEncryption;
using System;
using System.Collections.Generic;
using System.Text;
using AsyncTest;
using MySqlConnector;
using System.Data;
using static AsyncTest.Program;
using Renci.SshNet.Security;

namespace AsyncTest
{
    public class Program
    {
        public int simultaneousConnections;
        public SemaphoreSlim gate;

        public static async Task Main(string[] args)
        {
            Program program = new Program(10);


            var builder = new MySqlConnectionStringBuilder
            {
                Server = "192.168.178.57",
                Database = "KeyStore",
                UserID = "keyswapper",
                Password = "Herbst710",
            };

            /*
                Database structure:   
            
                +-------------+--------------+------+-----+---------+----------------+
                | Field       | Type         | Null | Key | Default | Extra          |
                +-------------+--------------+------+-----+---------+----------------+
                | ServerID    | int(11)      | NO   | PRI | NULL    | auto_increment |
                | Hostname    | varchar(255) | NO   |     | NULL    |                |
                | Username    | varchar(255) | YES  |     | NULL    |                |
                | PublicKey   | mediumtext   | YES  |     | NULL    |                |
                | PrivateKey  | mediumtext   | YES  |     | NULL    |                |
                | Password    | varchar(255) | YES  |     | NULL    |                |
                | Passphrase  | varchar(255) | YES  |     | NULL    |                |
                | JumphostIP  | varchar(255) | YES  |     | NULL    |                |
                | LastChanged | int(11)      | YES  |     | NULL    |                |
                +-------------+--------------+------+-----+---------+----------------+

             */

            
            using (var conn = new MySqlConnection(builder.ConnectionString))
            {
                Console.WriteLine("Connecting to Database...");
                try
                {
                    await conn.OpenAsync();
                } catch (Exception ex)
                {
                    Console.WriteLine($"Error connecting to database: ${ex}");
                    Environment.Exit(-1);
                }
                Console.WriteLine("Connected to Database.");
                List<EndServer> _hostnames = [];
                string query = "SELECT * FROM ServerKeys";
                using (var command = new MySqlCommand(query, conn))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            EndServer _eS = new EndServer();
                            _eS.ServerID = reader.IsDBNull("ServerID") ? 0 : reader.GetInt32("ServerID");
                            _eS.Hostname = reader.IsDBNull("Hostname") ? "" : reader.GetString("Hostname");
                            _eS.Username = reader.IsDBNull("Username") ? "" : reader.GetString("Username");
                            _eS.PublicKey = reader.IsDBNull("PublicKey") ? "" : reader.GetString("PublicKey");
                            _eS.PrivateKey = reader.IsDBNull("PrivateKey") ? "" : reader.GetString("PrivateKey");
                            _eS.Passphrase = reader.IsDBNull("Passphrase") ? "" : reader.GetString("Passphrase");
                            _eS.Password = reader.IsDBNull("Password") ? "" : reader.GetString("Password");
                            _eS.JumphostIP = reader.IsDBNull("JumphostIP") ? "" : reader.GetString("JumphostIP");
                            _eS.LastChanged = reader.IsDBNull("LastChanged") ? 0 : reader.GetInt32("LastChanged");

                            _hostnames.Add(_eS);
                        }
                    }
                }
                
                Console.WriteLine($"Changing Keys for {_hostnames.Count()} server(s) with max. {program.gate.CurrentCount} simultaneous connections");
                await program.StartWorkAsync(_hostnames, conn);

                Console.WriteLine("Disconnecting from Database...");
            }
            

            
            //List<string> hostnames = ["192.168.178.57","100.70.62.51"];
            //await program.StartWorkAsync(hostnames);
            //JumpHostTest jumpHostTest = new JumpHostTest();
            //jumpHostTest.connectWithPK();
        }

        public async Task StartWorkAsync(List<EndServer> _hostnames, MySqlConnection _db)
        {
            List<Task<ConnectionResult>> tasks = [.. CreateWork(_hostnames)];
            await Task.WhenAll(tasks);

            // Access results
            foreach (var result in tasks)
            {
                Console.WriteLine($"[Worker {result.Result.GetId()}] ({result.Result.GetHost().Hostname}): {result.Result.GetResult()}");
                if (result.Result.GetResult() == "Keyswap Success")
                {
                    using (var command = _db.CreateCommand())
                    {
                        command.CommandText = "UPDATE ServerKeys SET PublicKey = @PublicKey, PrivateKey = @PrivateKey, Passphrase = @Passphrase WHERE ServerID = @ServerID;";
                        command.Parameters.AddWithValue("@PublicKey", result.Result.GetNewPubKey());
                        command.Parameters.AddWithValue("@PrivateKey", result.Result.GetNewPrivKey());
                        command.Parameters.AddWithValue("@Passphrase", result.Result.GetPassphrase());
                        command.Parameters.AddWithValue("@ServerID", result.Result.GetHost().ServerID);

                        int rowCount = await command.ExecuteNonQueryAsync();
                        if (rowCount < 1) {
                            Console.WriteLine($"Error updating in Database: {rowCount} records updated");
                        }
                    }
                } else
                {
                    await Console.Out.WriteLineAsync($"Deleting Passphrase for {result.Result.GetHost().Hostname}...");
                    using (var command = _db.CreateCommand())
                    {
                        command.CommandText = "UPDATE ServerKeys SET Passphrase = @Passphrase, PrivateKey = @Passphrase, PublicKey = @Passphrase WHERE ServerID = @ServerID;";
                        command.Parameters.AddWithValue("@Passphrase", "");
                        command.Parameters.AddWithValue("@ServerID", result.Result.GetHost().ServerID);

                        int rowCount = await command.ExecuteNonQueryAsync();
                        if (rowCount < 1) {
                            Console.WriteLine($"Error updating in Database: {rowCount} records updated");
                        }
                    }
                }
            }
        }

        public IEnumerable<Task<ConnectionResult>> CreateWork(List<EndServer> _hostnames)
        {
            for (int i = 0; i < _hostnames.Count(); i++)
            {
                yield return work(i + 1, _hostnames[i]);
            }
        }

        public async Task<ConnectionResult> work(int _workerID, EndServer _server)
        {
            await gate.WaitAsync();
            await Console.Out.WriteLineAsync($"[{_workerID}] Working on {_server.Hostname}");
            ConnectionResult result = await SwapKeyAsync(_server, _server.Hostname, _server.Username);
            gate.Release();
            await Console.Out.WriteLineAsync($"[{_workerID}] {_server.Hostname} Finished");
            return new ConnectionResult(_server, _workerID, result.GetResult(), result.GetNewPubKey(), result.GetNewPrivKey(), result.GetPassphrase());
        }

        public class ConnectionResult
        {
            private int id;
            private string result;
            private string newPubKey;
            private string newPrivKey;
            private string passphrase;
            private EndServer host;

            public ConnectionResult(EndServer host, int id, string result, string newPubKey = "", string newPrivKey = "", string passphrase = "")
            {
                this.host = host;
                this.id = id;
                this.result = result;
                this.newPubKey = newPubKey;
                this.newPrivKey = newPrivKey;
                this.passphrase = passphrase;
            }
            public int GetId() { return id; }
            public EndServer GetHost() { return host; }
            public string GetResult() { return result; }
            public string GetNewPubKey() {  return newPubKey; }
            public string GetNewPrivKey() {  return newPrivKey; }
            public string GetPassphrase() {  return passphrase; }
        }

        public async Task<ConnectionResult> SwapKeyAsync(EndServer _endServer, string _host, string _username)
        {
            if (_username == "")
            {
                return new ConnectionResult(_endServer, 0, "Data Error: No username provided", "", "");
            }

            Random random = new Random();
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < 50; i++)
            {
                char ch = (char)random.Next(32, 127);
                builder.Append(ch);
            }

            string _GeneratedPassphrase = builder.ToString();
            var keyInfo = new SshKeyGenerateInfo { Encryption = new SshKeyEncryptionAes256(_GeneratedPassphrase) };
            var _key = SshKey.Generate("servery.ppk", FileMode.Create, keyInfo);
            var privateKey = SshKey.Generate(keyInfo);
            var publicSshKeyWithComment = _key.ToPublic();

            string host = _host;
            string username = _username;

            string privateKeyFilePath = Path.Combine("C:\\Users\\itsba\\.ssh\\keystore", $"private_key_{_host.Replace(".", "_")}.txt");

            await File.WriteAllTextAsync(privateKeyFilePath, _key.ToPuttyFormat(keyInfo.Encryption));

            ConnectionInfo ?_connectionInfo = null;

            if (_endServer.PrivateKey != "")
            {
                if (_endServer.Passphrase != "")
                {
                    Console.WriteLine($"({_endServer.Hostname}): Using PK+Passphrase to connect.");
                    try
                    {
                        _connectionInfo = new PrivateKeyConnectionInfo(_endServer.Hostname, _endServer.Username, new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(_endServer.PrivateKey)), _endServer.Passphrase));
                    } catch
                    {
                        return new ConnectionResult(_endServer, 0, "Data error: Error with Private Key", "", "");
                    }
                } else
                {
                    Console.WriteLine($"({_endServer.Hostname}): Using PK to connect.");
                    try
                    {
                        _connectionInfo = new PrivateKeyConnectionInfo(_endServer.Hostname, _endServer.Username, new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(_endServer.PrivateKey))));
                    }
                    catch
                    {
                        return new ConnectionResult(_endServer, 0, "Data error: Error with Private Key", "", "");
                    }
                }
            } else
            {
                _connectionInfo = new PasswordConnectionInfo(_endServer.Hostname, _endServer.Username, _endServer.Password);
                Console.WriteLine($"({_endServer.Hostname}): Using Password to connect.");
            }

            using (var client = new SshClient(_connectionInfo))
            {
                try
                {
                    CancellationToken cancellationToken = new CancellationToken();
                    await client.ConnectAsync(cancellationToken);

                    if (client.IsConnected)
                    {
                        var command = client.RunCommand("cat ~/.ssh/authorized_keys");
                        command = client.RunCommand("echo \"" + _key.ToPublic() + "\" >> ~/.ssh/authorized_keys");

                        client.Disconnect();

                        byte[] privateKeyBytes = Encoding.ASCII.GetBytes(_key.ToOpenSshFormat());
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
                                            pclient.RunCommand("echo \"" + publicSshKeyWithComment + "\" > ~/.ssh/authorized_keys");
                                            using (var kclient = new SshClient(connectionInfo))
                                            {
                                                try
                                                {
                                                    await kclient.ConnectAsync(cancellationToken);

                                                    if (kclient.IsConnected)
                                                    {
                                                        command = kclient.RunCommand("cat ~/.ssh/authorized_keys");

                                                        _foundkey = false;
                                                        keys = ParseAuthorizedKeys(@publicSshKeyWithComment);

                                                        foreach (Key key in keys)
                                                        {
                                                            if (key.KeyValue == ParseAuthorizedKeys(@publicSshKeyWithComment)[0].KeyValue)
                                                            {
                                                                _foundkey = true;
                                                            }
                                                        }
                                                        if (_foundkey)
                                                        {
                                                            return new ConnectionResult(_endServer, 0, "Keyswap Success", publicSshKeyWithComment, _key.ToOpenSshFormat(keyInfo.Encryption), _GeneratedPassphrase);
                                                        } else
                                                        {
                                                            return new ConnectionResult(_endServer, 0, "KeySwap2 error: not found after copy", "", "");
                                                            throw new Exception("KeySwap2 error: not found after copy");
                                                        }
                                                    } else
                                                    {
                                                        return new ConnectionResult(_endServer, 0, "Keytest2 Failed to connect.", "", "");
                                                        throw new Exception("Keytest2 Failed: to connect.");
                                                    }
                                                    } catch (Exception e)
                                                {
                                                    return new ConnectionResult(_endServer, 0, $"Keytest2 Failed.Error: { e.Message }", "", "");
                                                    throw new Exception($"Keytest2 Failed. Error: {e.Message}");
                                                } 
                                            }
                                        }
                                        else
                                        {
                                            return new ConnectionResult(_endServer, 0, "KeySwap error: not found after copy", "", "");
                                            throw new Exception("KeySwap error: not found after copy");
                                        }

                                        pclient.Disconnect();
                                    }
                                    else
                                    {
                                        return new ConnectionResult(_endServer, 0, "Keytest Failed to connect.", "", "");
                                        throw new Exception("Keytest Failed: to connect.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    return new ConnectionResult(_endServer, 0, $"Keytest Failed. Error: {ex.Message}", "", "");
                                    throw new Exception($"Keytest Failed. Error: {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        return new ConnectionResult(_endServer, 0, "Failed to connect to SSH server.", "", "");
                        throw new Exception("Failed to connect to SSH server.");

                    }
                }
                catch (Exception ex)
                {
                    return new ConnectionResult(_endServer, 0, $"Error 1: {ex.Message}", "", "");
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

        public class EndServer
        {
            public int ServerID { get; set; }
            public string Hostname { get; set; }
            public string? Username { get; set; }
            public string? PublicKey { get; set; }
            public string? PrivateKey { get; set; }
            public string? Password { get; set; }
            public string? Passphrase { get; set; }
            public string? JumphostIP { get; set; }
            public int? LastChanged { get; set; }

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

        public Program(int _sC)
        {
            this.simultaneousConnections = _sC;
            this.gate = new SemaphoreSlim(simultaneousConnections);
        }
    }
}
