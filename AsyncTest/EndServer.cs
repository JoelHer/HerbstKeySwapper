using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KeySwapper
{
    public class EndServer
    {
        public int? ServerID { get; set; }
        public string? Hostname { get; set; }
        public string? Username { get; set; }
        public string? PublicKey { get; set; }
        public string? PrivateKey { get; set; }
        public string? Password { get; set; }
        public string? Passphrase { get; set; }
        public string? JumphostIP { get; set; }
        public int? LastChanged { get; set; }

        public EndServer(int? ServerID = null, string? Hostname = null, string? Username = null, string? PublicKey = null, string? PrivateKey = null, string? Password = null, string? Passphrase = null, string? JumphostIP = null, int? LastChanged = null)
        {
            this.ServerID = ServerID;
            this.Hostname = Hostname;
            this.Username = Username;
            this.PublicKey = PublicKey;
            this.PrivateKey = PrivateKey;
            this.Password = Password;
            this.Passphrase = Passphrase;
            this.JumphostIP = JumphostIP;
            this.LastChanged = LastChanged;
        }
    }
}
