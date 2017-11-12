using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.IO;

namespace RecipeServer
{
    public class SessionInfo
    {
        public UserInfo User { get; set; }
        public string SessionId { get; private set; }
        public readonly Dictionary<string, object> Cache = new Dictionary<string, object>();
        public SessionInfo(string sessionId) { SessionId = sessionId; }
    }
}
