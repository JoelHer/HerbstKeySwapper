using System.Configuration;
using System.Collections.Specialized;
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
using KeySwapper;
using static KeySwapper.ServerInfoGatherer;

namespace AsyncTest
{
    public class Program
    {
        public int simultaneousConnections;
        public SemaphoreSlim gate;

        public static async Task Main(string[] args)
        {
            ConfigValidator configValidator = new ConfigValidator();
            configValidator.ValidateConfig();

            var _sC = ConfigurationManager.AppSettings["SimultaneousConnections"];
            Program program = new Program(int.Parse(_sC));

            Console.Write("Enter Database Password: ");

            ConsoleKeyInfo keyboardKey;

            string passwd = "";
            do
            {
                keyboardKey = Console.ReadKey(true); // true parameter hides the pressed key
                if (keyboardKey.Key != ConsoleKey.Enter)
                {
                    passwd += keyboardKey.KeyChar;
                }
            } while (keyboardKey.Key != ConsoleKey.Enter);
            await Console.Out.WriteLineAsync("\n");


            var builder = new MySqlConnectionStringBuilder
            {
                Server = ConfigurationManager.AppSettings["DatabaseServerAdress"],
                Database = ConfigurationManager.AppSettings["DatabaseName"],
                UserID = ConfigurationManager.AppSettings["DatabaseUsername"],
                Password = passwd,
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
                            _eS.ServerID = reader.IsDBNull("ServerID") ? 0 : reader.GetInt32(ConfigurationManager.AppSettings["DatabaseFieldName_ServerID"]);
                            _eS.Hostname = reader.IsDBNull("Hostname") ? "" : reader.GetString(ConfigurationManager.AppSettings["DatabaseFieldName_Hostname"]);
                            _eS.Username = reader.IsDBNull("Username") ? "" : reader.GetString(ConfigurationManager.AppSettings["DatabaseFieldName_Username"]);
                            _eS.PublicKey = reader.IsDBNull("PublicKey") ? "" : reader.GetString(ConfigurationManager.AppSettings["DatabaseFieldName_PublicKey"]);
                            _eS.PrivateKey = reader.IsDBNull("PrivateKey") ? "" : reader.GetString(ConfigurationManager.AppSettings["DatabaseFieldName_PrivateKey"]);
                            _eS.Passphrase = reader.IsDBNull("Passphrase") ? "" : reader.GetString(ConfigurationManager.AppSettings["DatabaseFieldName_Passphrase"]);
                            _eS.Password = reader.IsDBNull("Password") ? "" : reader.GetString(ConfigurationManager.AppSettings["DatabaseFieldName_Password"]);
                            _eS.JumphostIP = reader.IsDBNull("JumphostIP") ? "" : reader.GetString(ConfigurationManager.AppSettings["DatabaseFieldName_JumphostIP"]);
                            _eS.LastChanged = reader.IsDBNull("LastChanged") ? 0 : reader.GetInt32(ConfigurationManager.AppSettings["DatabaseFieldName_LastChanged"]);

                            _hostnames.Add(_eS);
                        }
                    }
                }
                
                Console.WriteLine($"Changing Keys for {_hostnames.Count()} server(s) with max. {program.gate.CurrentCount} simultaneous connections");
                await program.StartWorkAsync(_hostnames, conn);

                Console.WriteLine("Disconnecting from Database...");
            }
        }

        public async Task StartWorkAsync(List<EndServer> _hostnames, MySqlConnection _db)
        {
            List<Task<ConnectionResult>> tasks = [.. CreateWork(_hostnames)];
            await Task.WhenAll(tasks);

            // Access results
            foreach (var result in tasks)
            {
                Console.WriteLine($"[Worker {result.Result.GetId()}] ({result.Result.GetHost().Hostname}): {result.Result.GetResult()} + {result.Result.GetGatheredData().Hostname}, IPV4DNS:{result.Result.GetGatheredData().Dns4}");
                if (result.Result.GetResult() == "Keyswap Success")
                {
                    using (var command = _db.CreateCommand())
                    {
                        command.CommandText = "UPDATE ServerKeys SET PublicKey = @PublicKey, PrivateKey = @PrivateKey, Passphrase = @Passphrase, LastChanged = @LastChanged WHERE ServerID = @ServerID;";
                        command.Parameters.AddWithValue("@PublicKey", result.Result.GetNewPubKey());
                        command.Parameters.AddWithValue("@PrivateKey", result.Result.GetNewPrivKey());
                        command.Parameters.AddWithValue("@Passphrase", result.Result.GetPassphrase());
                        command.Parameters.AddWithValue("@ServerID", result.Result.GetHost().ServerID);
                        command.Parameters.AddWithValue("@LastChanged", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

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
                        command.CommandText = "UPDATE ServerKeys SET Passphrase = @Passphrase, PrivateKey = @Passphrase, PublicKey = @Passphrase, LastChanged = @LastChanged WHERE ServerID = @ServerID;";
                        command.Parameters.AddWithValue("@Passphrase", "");
                        command.Parameters.AddWithValue("@ServerID", result.Result.GetHost().ServerID);
                        command.Parameters.AddWithValue("@LastChanged", DateTimeOffset.UtcNow.ToUnixTimeSeconds());


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
            return new ConnectionResult(_server, _workerID, result.GetResult(), result.GetGatheredData(), result.GetNewPubKey(), result.GetNewPrivKey(), result.GetPassphrase());
        }

        public async Task<ConnectionResult> SwapKeyAsync(EndServer _endServer, string _host, string _username)
        {
            if (_username == "")
            {
                return new ConnectionResult(_endServer, 0, "Data Error: No username provided", new ServerInfo(false,_host, _username), "", "");
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

            try
            {
                if (bool.Parse(ConfigurationManager.AppSettings["SaveKeyOnSwap"]))
                {
                    string privateKeyFilePath = Path.Combine(ConfigurationManager.AppSettings["SaveKeyDirPath"], $"private_key-{_host.Replace(".", "_")}.{(bool.Parse(ConfigurationManager.AppSettings["SaveKeyInPuttyFormat"])? "ppk":"pem")}");
                    if (bool.Parse(ConfigurationManager.AppSettings["SaveKeyInPuttyFormat"]))
                    {
                        await File.WriteAllTextAsync(privateKeyFilePath, _key.ToPuttyFormat(keyInfo.Encryption));
                    } else
                    {
                        await File.WriteAllTextAsync(privateKeyFilePath, _key.ToOpenSshFormat(keyInfo.Encryption));
                    }
                }
            } catch
            {
                await Console.Out.WriteLineAsync("Error in config: SaveKeyOnSwap, SaveKeyInPuttyFormat or SaveKeyDirPath unparsable");
            }

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
                        return new ConnectionResult(_endServer, 0, "Data error: Error with Private Key", new ServerInfo(false, _host, _username), "", "");
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
                        return new ConnectionResult(_endServer, 0, "Data error: Error with Private Key", new ServerInfo(false, _host, _username), "", "");
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
                        ServerInfoGatherer serverInfoGatherer = new ServerInfoGatherer();
                        ServerInfo serverInfo;
                        try
                        {
                            serverInfo = serverInfoGatherer.GatherInfo(_endServer, client).Result;
                        }
                        catch (Exception _ex)
                        {
                            await Console.Out.WriteLineAsync($"Error in gathering server info: {_ex}");
                            serverInfo = new ServerInfo(false, _host, _username);
                        }

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
                                        List<KeySwapper.Key> keys = ParseAuthorizedKeys(command.Result);

                                        foreach (KeySwapper.Key key in keys)
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
                                                        keys = ParseAuthorizedKeys(command.Result);

                                                        foreach (KeySwapper.Key key in keys)
                                                        {
                                                            if (key.KeyValue == ParseAuthorizedKeys(@publicSshKeyWithComment)[0].KeyValue)
                                                            {
                                                                _foundkey = true;
                                                            }
                                                        }
                                                        if (_foundkey)
                                                        {
                                                            return new ConnectionResult(_endServer, 0, "Keyswap Success", serverInfo, publicSshKeyWithComment, _key.ToOpenSshFormat(keyInfo.Encryption), _GeneratedPassphrase);
                                                        } else
                                                        {
                                                            return new ConnectionResult(_endServer, 0, "KeySwap2 error: not found after copy", serverInfo, "", "");
                                                            throw new Exception("KeySwap2 error: not found after copy");
                                                        }
                                                    } else
                                                    {
                                                        return new ConnectionResult(_endServer, 0, "Keytest2 Failed to connect.", serverInfo, "", "");
                                                        throw new Exception("Keytest2 Failed: to connect.");
                                                    }
                                                    } catch (Exception e)
                                                {
                                                    return new ConnectionResult(_endServer, 0, $"Keytest2 Failed.Error: { e.Message }", serverInfo, "", "");
                                                    throw new Exception($"Keytest2 Failed. Error: {e.Message}");
                                                } 
                                            }
                                        }
                                        else
                                        {
                                            return new ConnectionResult(_endServer, 0, "KeySwap error: not found after copy", serverInfo, "", "");
                                            throw new Exception("KeySwap error: not found after copy");
                                        }

                                        pclient.Disconnect();
                                    }
                                    else
                                    {
                                        return new ConnectionResult(_endServer, 0, "Keytest Failed to connect.", serverInfo, "", "");
                                        throw new Exception("Keytest Failed: to connect.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    return new ConnectionResult(_endServer, 0, $"Keytest Failed. Error: {ex.Message}", serverInfo, "", "");
                                    throw new Exception($"Keytest Failed. Error: {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        return new ConnectionResult(_endServer, 0, "Failed to connect to SSH server.", new ServerInfo(false, _host, _username), "", "");
                        throw new Exception("Failed to connect to SSH server.");

                    }
                }
                catch (Exception ex)
                {
                    return new ConnectionResult(_endServer, 0, $"Error 1: {ex.Message}", new ServerInfo(false, _host, _username), "", "");
                }
            }
        }

        static List<KeySwapper.Key> ParseAuthorizedKeys(string authorizedKeysString)
        {
            List<KeySwapper.Key> keys = new List<KeySwapper.Key>();

            string[] lines = authorizedKeysString.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string type = parts[0];
                    string key = parts[1];
                    string comment = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "";

                    keys.Add(new KeySwapper.Key(type, key, comment));
                }
            }

            return keys;
        }

        public Program(int _sC)
        {
            this.simultaneousConnections = _sC;
            this.gate = new SemaphoreSlim(simultaneousConnections);
        }
    }
}
