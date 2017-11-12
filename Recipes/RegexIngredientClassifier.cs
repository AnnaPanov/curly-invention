using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.IO;

namespace Recipes
{
    public class RegexIngredientClassifier : IIngredientClassifier
    {
        /// <param name="configFileName">has to be a tab-separated file with three columns: className, pattern, priority</param>
        public RegexIngredientClassifier(string configFileName)
        {
            if (!File.Exists(configFileName))
                throw new ArgumentException("file does not exist: " + configFileName);
            string fileName = System.IO.Path.GetTempFileName();
            try
            {
                File.Copy(configFileName, fileName, true);
                List<Class> classes = new List<Class>();
                HashSet<string> classNamesUsed = new HashSet<string>();
                using (StreamReader r = new StreamReader(fileName))
                {
                    string line;
                    bool once = true;
                    while (null != (line = r.ReadLine()))
                    {
                        if (once)
                        {
                            once = false;
                            continue;
                        }
                        line = line.Trim();
                        if (0 == line.Length)
                            continue;
                        string[] columns = line.Split(new char[] { '\t' }, StringSplitOptions.None);
                        string name = columns[0];
                        if (columns.Length > 3)
                            throw new ArgumentException("some lines in ingredient classification don't have three columns in " + configFileName);
                        string pattern = columns.Length > 1 && ("" != columns[1]) ?
                            columns[1] : name.ToLower().Replace(" ", "\\s+"); // name is the default pattern

                        // "almost whole-word maching": means that "gin" won't match "aubergine", even though "gin" is a substring of "aubergine"
                        pattern = "(^|[^A-Za-z])(" + pattern + ")([A-Za-z]{0,2})?($|[^A-Za-z])";

                        int priority = 0;
                        if (columns.Length >= 3 && !int.TryParse(columns[2], out priority))
                            throw new ArgumentException("column #3 has invalid priority (" + columns[2] + "): priority must be an integer (in " + configFileName);
                        Class c = new Class(name, pattern, priority);
                        if (!classNamesUsed.Add(c.className_))
                            throw new ArgumentException("duplicate declaration of class " + c.className_ + " in " + configFileName);
                        classes.Add(c);
                    }
                }
                classes.Sort();
                classes_ = classes.ToArray();
            }
            finally
            {
                System.IO.File.Delete(fileName);
            }
        }

        #region How we define ingedient classes

        private class Class : IComparable<Class>
        {
            public readonly string className_;
            public readonly Regex pattern_;
            public readonly int priority_;

            public Class(string className, string pattern, int priority)
            {
                className_ = className;
                pattern_ = new Regex(pattern, RegexOptions.IgnoreCase);
                priority_ = priority;
            }

            public override string ToString()
            {
                return className_ == null ? "null" : className_;
            }

            public int CompareTo(Class other)
            {
                return className_.CompareTo(other.className_);
            }
        }
        private readonly Class[] classes_;

        #endregion

        #region How we match

        private class MatchResult : IComparable<MatchResult>
        {
            public Match Match;
            public Class Class;

            public static MatchResult TryMatch(Class c, IngredientName i)
            {
                var match = c.pattern_.Match(i.Name);
                if (!match.Success)
                    return null;
                return new MatchResult { Match = match, Class = c};
            }

            public int CompareTo(MatchResult other)
            {
                int prioritiesCompared = Class.priority_.CompareTo(other.Class.priority_);
                if (0 != prioritiesCompared)
                    return -prioritiesCompared; // higher prioroity is preferable

                // but if priority is the same, then compare the length of the match (longer match preferred)
                int lengthsComared = Match.Value.Length.CompareTo(other.Match.Value.Length);
                if (0 != lengthsComared)
                    return -lengthsComared; // higher match length is preferable ("winter root vegetables" is a better match than "root vegetables")

                // finally, determinism: if priorities are the same and lengths are the same, then the class with alphabetically lower name wins
                return Class.className_.CompareTo(other.Class.className_);
            }

            public override string ToString()
            {
                return Class == null ? "null" : Class.ToString();
            }
        }

        #endregion

        public ClassifiedRecipe ClassifyIngredients(Recipe recipe)
        {
            ClassifiedRecipe result = new ClassifiedRecipe()
            {
                Classification = new IngredientClassification(),
                Recipe = recipe,
            };
            // add the classified ingredients
            foreach (var ingredient in recipe.Ingredients)
            {
                var matchedDefinitions = new List<MatchResult>();
                foreach (Class c in classes_)
                {
                    var match = MatchResult.TryMatch(c, ingredient.Name);
                    if (null == match)
                        continue;
                    matchedDefinitions.Add(match);
                }
                matchedDefinitions.Sort();
                if (0 < matchedDefinitions.Count)
                    result.Classification.Classified[ingredient.Name] = matchedDefinitions[0].Class.className_;
            }
            // add the unclassified ingredients
            foreach (var ingredient in recipe.Ingredients)
            {
                if (!result.Classification.Classified.ContainsKey(ingredient.Name))
                    result.Classification.Unclassified.Add(ingredient.Name);
            }
            // done
            return result;
        }


        public IEnumerable<string> KnownClassNames
        {
            get
            {
                foreach (Class c in classes_)
                    yield return c.className_;
            }
        }
    }
}
