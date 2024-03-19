using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static KeySwapper.ServerInfoGatherer;

namespace KeySwapper
{
    public class ConnectionResult
    {
        public int id;
        public string result;
        public string newPubKey;
        public string newPrivKey;
        public string passphrase;
        public EndServer host;
        public ServerInfo gatheredInfo;

        public ConnectionResult(EndServer host, int id, string result, ServerInfo gatheredInfo, string newPubKey = "", string newPrivKey = "", string passphrase = "")
        {
            this.host = host;
            this.id = id;
            this.result = result;
            this.newPubKey = newPubKey;
            this.newPrivKey = newPrivKey;
            this.passphrase = passphrase;
            this.gatheredInfo = gatheredInfo;
        }
        public int GetId() { return id; }
        public EndServer GetHost() { return host; }
        public string GetResult() { return result; }
        public string GetNewPubKey() { return newPubKey; }
        public string GetNewPrivKey() { return newPrivKey; }
        public string GetPassphrase() { return passphrase; }
        public ServerInfo GetGatheredData() { return gatheredInfo; }
    }
}
