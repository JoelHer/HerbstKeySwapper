using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AsyncTest
{
    public class ConfigValidator
    {
        public void ValidateConfig() {
            Console.Out.WriteLineAsync("Validating config...");

            Dictionary<string, Type> keysToCheck = new Dictionary<string, Type>
            {
                { "DatabaseServerAdress", typeof(string) },
                { "DatabaseName", typeof(string) },
                { "DatabaseUsername", typeof(string) },
                { "DatabaseFieldName_ServerID", typeof(string) },
                { "DatabaseFieldName_Hostname", typeof(string) },
                { "DatabaseFieldName_Username", typeof(string) },
                { "DatabaseFieldName_PublicKey", typeof(string) },
                { "DatabaseFieldName_PrivateKey", typeof(string) },
                { "DatabaseFieldName_Passphrase", typeof(string) },
                { "DatabaseFieldName_Password", typeof(string) },
                { "DatabaseFieldName_JumphostIP", typeof(string) },
                { "DatabaseFieldName_LastChanged", typeof(string) },
                { "SimultaneousConnections", typeof(int) },
                { "SaveKeyOnSwap", typeof(bool) },
                { "SaveKeyDirPath", typeof(string) },
                { "SaveKeyInPuttyFormat", typeof(bool) }
            };

            bool unsetValue = false;

            foreach (var kvp in keysToCheck)
            {
                string key = kvp.Key;
                Type expectedType = kvp.Value;

                if (ConfigurationManager.AppSettings.AllKeys.Contains(key))
                {
                    string value = ConfigurationManager.AppSettings[key];
                    if (!TryParseValue(value, expectedType))
                    {
                        unsetValue = true;
                        Console.Out.WriteLineAsync($"Config: Key {key} has an invalid value");
                    }
                }
                else
                {
                    unsetValue = true;
                    Console.Out.WriteLineAsync($"Config: Key {key} is unset or does not exist");
                }
            }

            if (unsetValue)
            {
                Console.Out.WriteLineAsync($"One or more unset or invalid values in config file found. Exiting...");
                Environment.Exit(-1);
            }
            else
            {
                Console.Out.WriteLineAsync("Config passed checks.");
            }

            static bool TryParseValue(string value, Type targetType)
            {
                try
                {
                    if (targetType == typeof(int))
                    {
                        return int.TryParse(value, out _);
                    }
                    else if (targetType == typeof(bool))
                    {
                        return bool.TryParse(value, out _);
                    }
                    else if (targetType == typeof(string))
                    {
                        return true;
                    }
                    return false;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
    }
}
