// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Renci.SshNet;
using SshNet;

namespace Postie.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject
    {
        [ObservableProperty]
        private int _counter = 0;

        [ObservableProperty]
        private string _username = "";

        [ObservableProperty]
        private string _password = "";

        [ObservableProperty]
        private string _outputLog = "";

        [ObservableProperty]
        private string _connectGridVisibility = "Visible";

        [ObservableProperty]
        private string _outputLogVisibility = "Hidden";

        [ObservableProperty]
        private string _loadingVisibility = "Hidden";

        [ObservableProperty]
        private string _serverPageVisibility = "Hidden";

        [ObservableProperty]
        private string _backBtnVisibility = "Hidden";

        [ObservableProperty]
        private string _dataVisVisibility = "Hidden";

        [ObservableProperty]
        private string _hostName = "";

        [ObservableProperty]
        private List<DataEntity> _DataEntitiyList = [];

        [RelayCommand]
        private void OnCounterIncrement()
        {
            Counter++;
        }

        [RelayCommand]
        private void OnBackButton()
        {
            ConnectGridVisibility = "Visible";
            OutputLogVisibility = "Hidden";
            LoadingVisibility = "Hidden";
            ServerPageVisibility = "Hidden";
            BackBtnVisibility = "Hidden";
            OutputLog = "";
        }

        [RelayCommand]
        private void OnConnectButtonClick()
        {
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(HostName);
                foreach (IPAddress address in addresses)
                {
                    HostName = address.ToString();
                }
                ConnectGridVisibility = "Hidden";
                LoadingVisibility = "Visible";

        Task connectTask = ConnectToSshHost();
            }
            catch (Exception ex)
            {
                OutputLog += $"Error resolving hostname: {ex.Message}";
                HostName = "Error in DNS resolution";
            }
        }

        private async Task ConnectToSshHost()
        {
            ServerPageVisibility = "Visible";

            await Task.Run(() =>
            {
                using (var client = new SshClient(HostName, Username, Password))
                {
                    try
                    {
                        client.Connect();
                    } catch (Exception ex)
                    {
                        OutputLogVisibility = "Visible";
                        LoadingVisibility = "Hidden";
                        OutputLog += $"Error connecting to SSH server: {ex.Message}";
                        BackBtnVisibility = "Visible";
                        return;
                    }

                    if (client.IsConnected)
                    {
                        LoadingVisibility = "Hidden";
                        OutputLogVisibility = "Visible";
                        OutputLog += "Connected to SSH server.\n";
                        var commandResult = client.RunCommand("uname -a\n");
                        OutputLog += "uname result: ";
                        OutputLog += commandResult.Result;
                        commandResult = client.RunCommand("pwd \n");
                        OutputLog += "pwd result: ";
                        OutputLog += commandResult.Result;
                        commandResult = client.RunCommand("ls -l \n");
                        OutputLog += "ls result: ";
                        OutputLog += commandResult.Result;
                        DataEntitiyList = ParseLsOutput(commandResult.Result);
                        OutputLogVisibility = "Hidden";
                        DataVisVisibility = "Visible";
                        client.Disconnect();
                    }
                    else
                    {
                        LoadingVisibility = "Hidden";
                        OutputLogVisibility = "Visible";
                        BackBtnVisibility = "Visible";
                        OutputLog += "Failed to connect to SSH server.\n";
                    }
                }
            });
        }

        static List<DataEntity> ParseLsOutput(string output)
        {
            List<DataEntity> entities = new List<DataEntity>();

            // Regular expression pattern to match each line of ls -l output
            string pattern = @"^([dl\-])([rwxsStT\-]+)\s+(\d+)\s+(\w+)\s+(\w+)\s+(\d+)\s+(\w{3}\s+\d{1,2}\s+(?:\d{4}|\d{2}:\d{2}))\s+(.+)$";

            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Match match = Regex.Match(line, pattern);
                if (match.Success)
                {
                    string type = GetTypeFromSymbol(match.Groups[1].Value);
                    string permissions = match.Groups[2].Value;
                    string name = match.Groups[8].Value;

                    entities.Add(new DataEntity
                    {
                        Type = type,
                        Name = name,
                        Permissions = permissions
                    });
                }
            }

            return entities;
        }


        static string GetTypeFromSymbol(string symbol)
        {
            switch (symbol)
            {
                case "d": return "Directory";
                case "-": return "File";
                case "l": return "SymbolicLink";
                case "r": return "RegularFile"; // This could be extended based on the output's structure
                default: return "Unknown";
            }
        }
    }
    
    public class DataEntity
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string Permissions { get; set; }
        public List<DataEntity> EntityItems { get; set; }
    }
}
