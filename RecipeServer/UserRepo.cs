using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.IO;

namespace RecipeServer
{
    public class UserInfo : IEquatable<UserInfo>
    {
        public string Username;
        public string Password;

        public bool Equals(UserInfo other)
        {
            return null != other && other.Username == Username;
        }

        public override int GetHashCode()
        {
            return Username.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as UserInfo);
        }

        public override string ToString()
        {
            return Username;
        }
    }

    public class UserRepo
    {
        /// <summary>
        /// Users by username
        /// </summary>
        public UserInfo this[string username]
        {
            get
            {
                lock (users_)
                {
                    UserInfo result;
                    if (!users_.TryGetValue(username, out result))
                        result = null;
                    return result;
                }
            }
            set
            {
                if (username != value.Username)
                    throw new ArgumentException("username provided as a key doesn't match the username in UserInfo given");
                lock (users_)
                {
                    users_[username] = value;
                }
                using (StreamWriter s = new StreamWriter(location_.FullName + "\\" + username, false, Encoding.Unicode))
                    serializer_.Serialize(s, value);
            }
        }

        /// <summary>
        /// When constructed, will read the information from all the user files
        /// </summary>
        public UserRepo(DirectoryInfo location)
        {
            location_ = location;
            if (!location_.Exists)
                location_.Create();
            foreach (FileInfo i in location_.GetFiles())
            {
                try
                {
                    using (FileStream f = i.OpenRead())
                    {
                        UserInfo u = serializer_.Deserialize(f) as UserInfo;
                        if (null != u)
                            users_[u.Username] = u;
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Cannot read user information from {0}: {1}", i.FullName, e.ToString());
                }
            }
        }

        private readonly DirectoryInfo location_;
        private readonly XmlSerializer serializer_ = new XmlSerializer(typeof(UserInfo));
        private readonly Dictionary<string, UserInfo> users_ = new Dictionary<string, UserInfo>();
    }
}
