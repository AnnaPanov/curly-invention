using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Recipes
{
    public class TypesBySensitivity : IIngredientTypes
    {
        public Dictionary<string, IngredientType> ClassToType
        {
	        get { return classToType_; }
        }

        private Dictionary<string, IngredientType> classToType_ = new Dictionary<string, IngredientType>();

        private Dictionary<string, string> typeToGroup_ = new Dictionary<string, string>();

        public TypesBySensitivity(string configFileName)
        {
            if (!File.Exists(configFileName))
                throw new ArgumentException("file does not exist: " + configFileName);
            string fileName = System.IO.Path.GetTempFileName();
            try
            {
                File.Copy(configFileName, fileName, true);
                HashSet<string> classNamesUsed = new HashSet<string>();
                using (StreamReader r = new StreamReader(fileName))
                {
                    string line;
                    bool once = true;
                    int lineNo = 0;
                    while (null != (line = r.ReadLine()))
                    {
                        ++lineNo;
                        if (once)
                        {
                            once = false;
                            continue;
                        }
                        line = line.Trim();
                        if (0 == line.Length)
                            continue;
                        string[] columns = line.Split(new char[] { '\t' }, StringSplitOptions.None);
                        if (3 != columns.Length)
                            throw new Exception("in " + configFileName + " at line #" + lineNo + " number of columns is not 3");
                        string groupName = Unquote(columns[2]);
                        string typeName = Unquote(columns[1]);
                        string className = Unquote(columns[0]);
                        if (!classNamesUsed.Add(className))
                            throw new Exception("in " + configFileName + " at line #" + lineNo + " duplicate ingredient class name '" + className + "'");
                        if (typeToGroup_.ContainsKey(typeName) && typeToGroup_[typeName] != groupName)
                            throw new Exception("in " + configFileName + " at line #" + lineNo + " type '" + typeName + "' already belongs to group '" + typeToGroup_[typeName] + "', but for class '" + className + "' we are trying to put '" + typeName + "' into '" + groupName + "' group");
                        classToType_[className] = new IngredientType { Type = typeName, Group = groupName };
                        typeToGroup_[typeName] = groupName;
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                throw;
            }
        }

        static string Unquote(string s)
        {
            if (1 < s.Length && s[0] == '"' && s[s.Length - 1] == '"')
                return s.Substring(1, s.Length - 2);
            else return s;
        }
    }
}
