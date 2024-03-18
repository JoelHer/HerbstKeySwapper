using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using static AsyncTest.Program;
using static KeySwapper.ServerInfoGatherer;

namespace KeySwapper
{
    public class ServerInfoGatherer
    {
        public class ServerInfo {
            public string IP { get; set; }
            public string Hostname { get; set; }
            public string Dns4 { get; set; }
            public bool success { get; set; }

            public ServerInfo(bool success, string IP, string Hostname="", string Dns4 = "") {
                this.IP = IP;
                this.Hostname = Hostname;
                this.Dns4 = Dns4;
                this.success = success;
            }
        }

        public async Task<ServerInfo> GatherInfo(EndServer server)
        {
            ConnectionInfo? _connectionInfo = null;

            
            if (server.PrivateKey != "")
            {
                if (server.Passphrase != "")
                {
                    Console.WriteLine($"({server.Hostname}): Using PK+Passphrase to connect.");
                    try
                    {
                        _connectionInfo = new PrivateKeyConnectionInfo(server.Hostname, server.Username, new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(server.PrivateKey)), server.Passphrase));
                    }
                    catch
                    {
                        return new ServerInfo(false, server.Hostname);
                    }
                }
                else
                {
                    Console.WriteLine($"({server.Hostname}): Using PK to connect.");
                    try
                    {
                        _connectionInfo = new PrivateKeyConnectionInfo(server.Hostname, server.Username, new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(server.PrivateKey))));
                    }
                    catch
                    {
                        return new ServerInfo(false, server.Hostname);
                    }
                }
            }
            else
            {
                _connectionInfo = new PasswordConnectionInfo(server.Hostname, server.Username, server.Password);
                Console.WriteLine($"({server.Hostname}): Using Password to connect.");
            }

            string remHash(string input)
            {
                StringBuilder output = new StringBuilder();
                string[] lines = input.Split('\n');
                foreach (string line in lines)
                {
                    if (!line.Trim().StartsWith("#"))
                    {
                        output.AppendLine(line);
                    }
                }
                return output.ToString().Replace("\n", "");
            }

            using (var client = new SshClient(_connectionInfo))
            {
                CancellationToken cancellationToken = new CancellationToken();
                await client.ConnectAsync(cancellationToken);

                if (client.IsConnected)
                {
                    var _hostname = client.RunCommand("hostname");
                    var _dns4 = client.RunCommand("cat /etc/resolv.conf");
                    
                    return new ServerInfo(true, server.Hostname, _hostname.Result.Replace("\n", ""), remHash(_dns4.Result));
                } else
                {
                    return new ServerInfo(false, server.Hostname);
                }
            }
            
        }
    }
}
