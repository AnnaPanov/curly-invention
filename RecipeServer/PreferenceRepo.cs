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
        public string HaveImage = null;
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
        /// Preferences with unrecognized-yet images
        /// </summary>
        public Dictionary<string, PreferenceInfo> WithUnrecognizedImages()
        {
            Dictionary<string, PreferenceInfo> result = new Dictionary<string, PreferenceInfo>();
            lock (preferences_)
            {
                foreach (var p in preferences_)
                    if (p.Value.IngredientPreferences == null) result[p.Key] = p.Value;
            }
            return result;
        }

        /// <summary>
        /// When constructed, will read the information from all the user files
        /// </summary>
        public PreferenceRepo(DirectoryInfo location)
        {
            location_ = location;
            if (!location_.Exists)
                location_.Create();
            List<string> images = new List<string>();
            foreach (FileInfo i in location_.GetFiles())
            {
                if (i.FullName.EndsWith(".jpg"))
                {
                    images.Add(i.FullName.Substring(0, i.FullName.Length - 4));
                    continue; // TODO: handle this separately
                }
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
            foreach (var i in images)
            {
                if (preferences_.ContainsKey(i))
                    continue;
                PreferenceInfo p = new PreferenceInfo();
                p.IngredientPreferences = null;
                p.HaveImage = i + ".jpg";
                FileInfo f = new FileInfo(p.HaveImage);
                string requestId = f.Name.Substring(0, f.Name.Length - 4);
                preferences_[requestId] = p;
            }
        }

        public string SaveImageFile(Encoding enc, String boundary, Stream input)
        {
            string username = null;
            string filename = null;
            do
            {
                username = Guid.NewGuid().ToString();
                filename = location_.FullName + "\\" + username + ".jpg";
            }
            while (System.IO.File.Exists(filename));

            Byte[] boundaryBytes = enc.GetBytes(boundary);
            Int32 boundaryLen = boundaryBytes.Length;

            using (FileStream output = new FileStream(filename, FileMode.Create, FileAccess.Write))
            {
                Byte[] buffer = new Byte[1024];
                Int32 len = input.Read(buffer, 0, 1024);
                Int32 startPos = -1;

                // Find start boundary
                while (true)
                {
                    if (len == 0)
                    {
                        throw new Exception("Start Boundaray Not Found");
                    }

                    startPos = IndexOf(buffer, len, boundaryBytes);
                    if (startPos >= 0)
                    {
                        break;
                    }
                    else
                    {
                        Array.Copy(buffer, len - boundaryLen, buffer, 0, boundaryLen);
                        len = input.Read(buffer, boundaryLen, 1024 - boundaryLen);
                    }
                }

                // Skip four lines (Boundary, Content-Disposition, Content-Type, and a blank)
                for (Int32 i = 0; i < 4; i++)
                {
                    while (true)
                    {
                        if (len == 0)
                        {
                            throw new Exception("Preamble not Found.");
                        }

                        startPos = Array.IndexOf(buffer, enc.GetBytes("\n")[0], startPos);
                        if (startPos >= 0)
                        {
                            startPos++;
                            break;
                        }
                        else
                        {
                            len = input.Read(buffer, 0, 1024);
                        }
                    }
                }

                Array.Copy(buffer, startPos, buffer, 0, len - startPos);
                len = len - startPos;

                while (true)
                {
                    Int32 endPos = IndexOf(buffer, len, boundaryBytes);
                    if (endPos >= 0)
                    {
                        if (endPos > 0) output.Write(buffer, 0, endPos - 2);
                        break;
                    }
                    else if (len <= boundaryLen)
                    {
                        throw new Exception("End Boundaray Not Found");
                    }
                    else
                    {
                        output.Write(buffer, 0, len - boundaryLen);
                        Array.Copy(buffer, len - boundaryLen, buffer, 0, boundaryLen);
                        len = input.Read(buffer, boundaryLen, 1024 - boundaryLen) + boundaryLen;
                    }
                }
            }

            lock (preferences_)
            {
                PreferenceInfo i = new PreferenceInfo();
                i.IngredientPreferences = null;
                i.HaveImage = filename;
                preferences_[username] = i;
            }
            return username;
        }

        private static Int32 IndexOf(Byte[] buffer, Int32 len, Byte[] boundaryBytes)
        {
            for (Int32 i = 0; i <= len - boundaryBytes.Length; i++)
            {
                Boolean match = true;
                for (Int32 j = 0; j < boundaryBytes.Length && match; j++)
                {
                    match = buffer[i + j] == boundaryBytes[j];
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        private readonly DirectoryInfo location_;
        private readonly XmlSerializer serializer_ = new XmlSerializer(typeof(PreferenceInfo));
        private readonly Dictionary<string, PreferenceInfo> preferences_ = new Dictionary<string, PreferenceInfo>();
    }
}
