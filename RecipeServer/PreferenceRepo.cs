using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.IO;

namespace RecipeServer
{
    public struct Preference
    {
        public int Score;
        public string Ingredient;
    }

    public class PreferenceInfo
    {
        public List<Preference> IngredientPreferences = new List<Preference>();
    }

    public class PreferenceRepo
    {
        /// <summary>
        /// Users by username
        /// </summary>
        public PreferenceInfo this[string username]
        {
            get
            {
                lock (preferences_)
                {
                    PreferenceInfo result;
                    if (!preferences_.TryGetValue(username, out result))
                        result = null;
                    return result;
                }
            }
            set
            {
                lock (preferences_)
                {
                    preferences_[username] = value;
                }
                using (StreamWriter s = new StreamWriter(location_.FullName + "\\" + username, false, Encoding.Unicode))
                    serializer_.Serialize(s, value);
            }
        }

        /// <summary>
        /// When constructed, will read the information from all the user files
        /// </summary>
        public PreferenceRepo(DirectoryInfo location)
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
                        PreferenceInfo p = serializer_.Deserialize(f) as PreferenceInfo;
                        if (null != p)
                            preferences_[i.Name] = p;
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Cannot read user information from {0}: {1}", i.FullName, e.ToString());
                }
            }
        }

        private readonly DirectoryInfo location_;
        private readonly XmlSerializer serializer_ = new XmlSerializer(typeof(PreferenceInfo));
        private readonly Dictionary<string, PreferenceInfo> preferences_ = new Dictionary<string, PreferenceInfo>();
    }
}
