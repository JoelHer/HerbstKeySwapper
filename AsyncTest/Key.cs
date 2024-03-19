using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KeySwapper
{
    public class Key
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
