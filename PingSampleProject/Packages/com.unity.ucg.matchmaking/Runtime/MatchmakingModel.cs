using System.Collections.Generic;
using System.Text;
using Google.Protobuf;

namespace UnityEngine.Ucg.Matchmaking
{
    public sealed class CreateTicketRequest
    {
        public Dictionary<string, double> Attributes { get; set; } = new Dictionary<string, double>();

        public Dictionary<string, string> Properties { get; set; }
    }

    public sealed class Assignment
    {
        public string Connection { get; }

        public string Error { get; }

        public string Properties { get; }

        public Assignment(string connection, string error, string properties)
        {
            Connection = connection;
            Error = error;
            Properties = properties;
        }
    }
}
