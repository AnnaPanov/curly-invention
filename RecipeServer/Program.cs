using System;
using System.Configuration;
using Newtonsoft.Json;
using System.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Web;
using System.IO;
using Recipes;

namespace RecipeServer
{
    class Program
    {
        static UserRepo users_ = null;
        static PreferenceRepo preferences_ = null;
        static readonly List<ClassifiedRecipe> recipes_ = new List<ClassifiedRecipe>();
        static List<KeyValuePair<string, SortedSet<string>>>[] ingredientsAsColumns_ = null;
        static readonly SortedList<string, SortedSet<string>> ingredientTypes_ = new SortedList<string, SortedSet<string>>();

        static string DispatchWebPage(HttpListenerRequest request, HttpListenerResponse result, SessionInfo session)
        {
            if (request.Url.LocalPath == "/recipes_api")
                return RecipeJsonPage(request, result, session);
            if (request.Url.LocalPath == "/recognize_preferences_api")
                return RecognizePreferencePage(request, result, session);
            if (request.Url.LocalPath == "/login")
                return LoginPage(request, result, session);
            // if not logged in, redirect to login
            if (null == session.User)
                return "<html><head><meta http-equiv=refresh content='0;URL=/login'/></head></html>";

            // otherwise, display something
            switch (request.Url.LocalPath)
            {
                case "/recipes":
                    return RecipePage(request, result, session);
                case "/preferences":
                default:
                    return PreferencePage(request, result, session);
            }
        }

        static string LoginPage(HttpListenerRequest request, HttpListenerResponse result, SessionInfo session)
        {
            // try to authenticate
            bool authenticationFailed = false;
            if (null == session.User && request.HttpMethod.ToUpper() == "POST")
            {
                string username, password;
                NameValueCollection postArgs = GetPostArgs(request);
                GetUsernamePassword(postArgs, out username, out password);
                if (username != null)
                {
                    UserInfo user = users_[username];
                    if (user != null && user.Password == password)
                        session.User = user; // authentication succeeded
                    else
                        authenticationFailed = true;
                }
            }
            if (session.User != null)
                return "<html><head><meta http-equiv=refresh content='0;URL=/'/></head></html>";

            // sign in
            StringBuilder buffer = new StringBuilder();
            buffer.Append(
@"
<html>
<title>Sign In</title>
<body>
<form method=post>
<fieldset>
<legend>Sign In</legend>
<label>Username:</label>&nbsp;<input type=text name=uname id=uname value=''/><br>
<label>Password:</label>&nbsp;<input type=password name=pswd id=pswd value=''/><br>
<input type=submit value='Sign In!'/>
</fieldset>
");
            if (authenticationFailed)
                buffer.AppendLine("<script>alert(\"Hmm, I don't see anyone with this name and password. Try again?\");</script>");
            buffer.Append(
@"
</body>
</html>
");
            return buffer.ToString();
        }

        static string RecognizePreferencePage(HttpListenerRequest request, HttpListenerResponse result, SessionInfo session)
        {
            Console.WriteLine("RecognizePreferencePage request");
            if (request.ContentType != null)
            {
                SaveFile(request.ContentEncoding, GetBoundary(request.ContentType), request.InputStream);
                return "http://blah.com/" + Guid.NewGuid().ToString();
            }
            return
                "<html><body><br>\n" +
                "<form method=post>\n" +
                "test results:<input name=testResults type=file>" +
                "<input type=submit>\n" +
                "</form>\n" +
                "</body></html>";
        }

        static string RecipeJsonPage(HttpListenerRequest request, HttpListenerResponse result, SessionInfo session)
        {
            var args = GetPostArgs(request);
            try
            {
                var preferences = args["preferences"];
                if (null == preferences)
                    throw new ArgumentException("request does not contain post argument 'preferences'");
                var strictnessStr = args["strictness"];
                if (null == strictnessStr)
                    throw new ArgumentException("request does not contain post argument 'strictness'");
                var limitStr = args["limit"];
                if (null == limitStr)
                    throw new ArgumentException("request does not contain post argument 'limit'");
                int limit;
                if (!int.TryParse(limitStr, out limit) || !(0 < limit))
                    throw new ArgumentException("limit " + limitStr+ " is not a positive integer");
                double strictness;
                if (!double.TryParse(strictnessStr, out strictness))
                    throw new ArgumentException("strictness " + strictnessStr + " is not a floating-point number");
                strictness = Math.Max(1e-4, strictness);
                var prefs = JsonConvert.DeserializeXNode(preferences);
                Dictionary<string, int> preferenceLookup = new Dictionary<string, int>();
                foreach (var element in prefs.Root.Elements())
                {
                    if (element.HasAttributes)
                        throw new ArgumentException("preferences:" + element.Name + " has some kind of attributes inside");
                    if (element.HasElements)
                        throw new ArgumentException("preferences:" + element.Name + " has some kind of elements inside");
                    if (element.IsEmpty)
                        throw new ArgumentException("preferences:" + element.Name + " is empty");
                    int score;
                    if (!int.TryParse(element.Value, out score))
                        throw new ArgumentException("preferences:" + element.Name + " has a score of " + element.Value + ", which is not an integer");
                    preferenceLookup[element.Name.ToString()] = score;
                }
                var rankedRecipes = new List<PreferenceScore>();
                foreach (var recipe in recipes_)
                    rankedRecipes.Add(GetPreferenceScore(recipe, preferenceLookup, strictness));
                rankedRecipes.Sort();
                StringBuilder buffer = new StringBuilder();
                int nResults = 0;
                foreach (var recipe in rankedRecipes)
                {
                    ++nResults;
                    if (nResults > limit) break;
                    buffer.AppendLine("{");
                    buffer.AppendLine("  \"title\" : \"" + RecipeDownloader.RemoveSpecialCharacters(recipe.Recipe.Recipe.OriginalWebPage.Name) + "\",");
                    buffer.AppendLine("  \"url\" : \"" + recipe.Recipe.Recipe.OriginalWebPage.UrlRoot + recipe.Recipe.Recipe.OriginalWebPage.Url + "\",");
                    buffer.AppendLine("  \"positives\" : [");
                    if (recipe.Positives != null)
                    {
                        foreach (string entry in recipe.Positives)
                            buffer.Append("    \"").Append(entry).Append("\",").AppendLine();
                    }
                    buffer.AppendLine("  ],");
                    buffer.AppendLine("  \"neutrals\" : [");
                    if (recipe.Neutrals != null)
                    {
                        foreach (string entry in recipe.Neutrals)
                            buffer.Append("    \"").Append(entry).Append("\",").AppendLine();
                    }
                    buffer.AppendLine("  ],");
                    buffer.AppendLine("  \"negatives\" : [");
                    if (recipe.Negatives != null)
                    {
                        foreach (string entry in recipe.Negatives)
                            buffer.AppendLine("    \"").Append(entry).Append("\",").AppendLine();
                    }
                    buffer.AppendLine("  ],");
                    buffer.AppendLine("},");
                }
                return "{ \"recipes\" : [\n" + buffer.ToString() + "]\n }\n";
            }
            catch (Exception e)
            {
                StringBuilder preferenceBlob = new StringBuilder();
                if (session.User != null)
                {
                    PreferenceInfo preferences = preferences_[session.User.Username];
                    if (null != preferences)
                    {
                        preferenceBlob.AppendLine("{ \"preferences\" : {");
                        foreach (var p in preferences.IngredientPreferences)
                            preferenceBlob.AppendLine("    \"" + p.Ingredient + "\" : \"" + p.Score.ToString() + "\",");
                        preferenceBlob.AppendLine("} }");
                    }
                }
                return
                    "<html><body>" + e.ToString() + "<br>\n" +
                    "<form method=post>\n" +
                    "strictness:<input type='text' name='strictness' value='1'><br>\n" +
                    "limit:<input type='text' name='limit' value='15'><br>\n" +
                    "preferences:<br><textarea name=preferences rows=20 cols=80>" + preferenceBlob.ToString() + "</textarea><br>\n" +
                    "<input type=submit>\n" +
                    "</form>\n" +
                    "</body></html>";
            }
        }

        static string RecipePage(HttpListenerRequest request, HttpListenerResponse result, SessionInfo session)
        {
            PreferenceInfo preferences = preferences_[session.User.Username];
            if (null == preferences)
                preferences = new PreferenceInfo();
            Dictionary<string, int> preferenceLookup = new Dictionary<string, int>();
            foreach (var p in preferences.IngredientPreferences)
                preferenceLookup[p.Ingredient] = p.Score;
            var getArgs = HttpUtility.ParseQueryString(request.Url.Query);
            double strictness = Math.Max(1e-4, double.Parse(getArgs["strictness"]));
            var rankedRecipes = new List<PreferenceScore>();
            foreach (var recipe in recipes_)
                rankedRecipes.Add(GetPreferenceScore(recipe, preferenceLookup, strictness));
            rankedRecipes.Sort();

            StringBuilder buffer = new StringBuilder();
            buffer.Append(
@"
<html>
<title>")
                .Append(session.User.Username)
                .Append(
@"'s personalized recipes</title>
<body style='font-family:tahoma,arial,sans-serif;'>
<fieldset>
");
            AddLegend(session, buffer);
            foreach (var recipe in rankedRecipes)
            {
                var owp = recipe.Recipe.Recipe.OriginalWebPage;
                buffer.AppendFormat("<br><a href='{2}' target='_blank' title='score: {0:0%}'>{1}</a><br>\n",
                    recipe.Score, RecipeDownloader.RemoveSpecialCharacters(owp.Name), owp.UrlRoot + owp.Url);
                if (recipe.Positives != null) 
                {
                    buffer.Append("<table border=0 cellpadding=0 cellspacing=0><tr><td bgcolor='lightcyan'>");
                    foreach (string entry in recipe.Positives)
                        buffer.Append("<li>").Append(entry).Append("</li>\n");
                    buffer.Append("</table>");
                }
                if (recipe.Neutrals != null)
                {
                    buffer.Append("<table border=0 cellpadding=0 cellspacing=0><tr><td bgcolor='ivory'>");
                    foreach (string entry in recipe.Neutrals)
                        buffer.Append("<li>").Append(entry).Append("</li>\n");
                    buffer.Append("</table>");
                }
                if (recipe.Negatives != null)
                {
                    buffer.Append("<table border=0 cellpadding=0 cellspacing=0><tr><td bgcolor='peachpuff'>");
                    foreach (string entry in recipe.Negatives)
                        buffer.Append("<li>").Append(entry).Append("</li>\n");
                    buffer.Append("</table>");
                }
            }
            buffer.AppendLine(
@"
</fieldset>
</body>
</html>
");
            return buffer.ToString();
        }

        class PreferenceScore : IComparable<PreferenceScore>
        {
            public double Score = 0.0;
            public ClassifiedRecipe Recipe = null;
            public List<string> Positives = null;
            public List<string> Neutrals = null;
            public List<string> Negatives = null;
            public int CompareTo(PreferenceScore other) { return other.Score.CompareTo(Score); }
        }

        private static Regex specialChars_ = new Regex("[^a-zA-Z0-9_/\\.,:;&#\\\"\\'\\- \\t]+", RegexOptions.Compiled);
        private static string RemoveSpecialCharacters(string str)
        {
            return specialChars_.Replace(str, "");
        }

        private static PreferenceScore GetPreferenceScore(ClassifiedRecipe recipe, Dictionary<string, int> preferenceLookup, double strictness)
        {
            double result = 0.0;
            List<string> positives = null;
            List<string> neutrals = null;
            List<string> negatives = null;
            foreach (var ingredient in recipe.Recipe.Ingredients)
            {
                string ingredientType;
                if (!recipe.Classification.Classified.TryGetValue(ingredient.Name, out ingredientType))
                    continue;
                int score;
                if (!preferenceLookup.TryGetValue(ingredientType, out score))
                    continue; // equivalent to score = 0
                double contribution = score;
                /*
                if (0 == score)
                    contribution = -0.05; // still penalize for neutral ingredients (fewer ingredients => better)
                */
                if (contribution < 0)
                    contribution *= strictness;
                result += contribution;

                if (0 < contribution)
                {
                    if (null == positives)
                        positives = new List<string>();
                    if (!positives.Contains(ingredient.Declaration))
                        positives.Add(RemoveSpecialCharacters(ingredient.Declaration));
                }
                if (0 == contribution)
                {
                    if (null == neutrals)
                        neutrals = new List<string>();
                    if (!neutrals.Contains(ingredient.Declaration))
                        neutrals.Add(RemoveSpecialCharacters(ingredient.Declaration));
                }
                if (0 > contribution)
                {
                    if (null == negatives)
                        negatives = new List<string>();
                    if (!negatives.Contains(ingredient.Declaration))
                        negatives.Add(RemoveSpecialCharacters(ingredient.Declaration));
                }
            }
            result = result / recipe.Recipe.Ingredients.Count;
            return new PreferenceScore { Score = result, Recipe = recipe, Positives = positives, Neutrals = neutrals, Negatives = negatives };
        }

        static string PreferencePage(HttpListenerRequest request, HttpListenerResponse result, SessionInfo session)
        {
            PreferenceInfo preferences = preferences_[session.User.Username];
            if (null == preferences)
                preferences = new PreferenceInfo();

            // saving preferences?
            if (request.HttpMethod.ToUpper() == "POST")
                return TrySavePreferences(request, session, preferences);

            // if logged in, discuss preferences
            StringBuilder buffer = new StringBuilder();
            buffer.Append(
@"
<html>
<title>Preferences</title>
<body style='font-family:tahoma,arial,sans-serif;'>
");
            buffer.AppendLine(
@"
<form method=post>
<fieldset>");
            AddLegend(session, buffer);
            buffer.Append(@"
<input type=submit id='savePreferences' name='savePreferences' value='Save Preferences' disabled/>
<script>
var nChanges = 0
function onPrefModified(which,label) {
  ++nChanges;
  document.getElementById('savePreferences').disabled = false;
  document.getElementById('savePreferences').value = 'Save Preferences (' + nChanges + ' changes)';
  var label = document.getElementById(label);
  if (label != null)
    label.style.backgroundColor = 'yellow';
}
</script>
<p>
");
            {
                Dictionary<string, int> preferenceLookup = new Dictionary<string, int>();
                foreach (var p in preferences.IngredientPreferences)
                    preferenceLookup[p.Ingredient] = p.Score;
                buffer.AppendLine("<table cellpadding=2 border=0><tr>");
                foreach (var column in ingredientsAsColumns_)
                {
                    if (0 == column.Count)
                        continue;
                    buffer.AppendLine("<td valign=top align=left><table cellpadding=2>");
                    foreach (var ingredientGroup in column)
                    {
                        buffer.AppendLine("<tr><td colspan=4 aligh=left><u>").AppendLine(ingredientGroup.Key).AppendLine("</u></td></tr>");
                        foreach (var ingredient in ingredientGroup.Value)
                        {
                            int score;
                            if (!preferenceLookup.TryGetValue(ingredient, out score))
                                score = 0;
                            buffer.AppendFormat(
    @"
  <tr>
    <td bgcolor=limegreen title='like'><input type=radio name='pref_{0}' value='+1'{1} onchange='onPrefModified(this,{4});'/></td>
    <td bgcolor=moccasin title='ok'><input type=radio name='pref_{0}' value='0'{2} onchange='onPrefModified(this,{4});'/></td>
    <td bgcolor=salmon title='avoid!'><input type=radio name='pref_{0}' value='-1'{3} onchange='onPrefModified(this,{4});'/></td>
    <td id={4}>{0}</td>
  </tr>
",
                                ingredient,
                                score == +1 ? "checked=checked" : " ",
                                score == 0 ? "checked=checked" : " ",
                                score == -1 ? "checked=checked" : " ",
                                '"' + "lbl_" + ingredient + '"');
                            buffer.AppendLine();
                        }
                        buffer.AppendLine("  <tr><td colspan=4 aligh=left>&nbsp;</td></tr>");
                    }
                    buffer.AppendLine("</table></td>");
                }
                buffer.AppendLine("</td></table>");
            }
            buffer.Append(@"
</fieldset>
</form>
</body>
</html>
");
            return buffer.ToString();
        }

        private static void AddLegend(SessionInfo session, StringBuilder buffer)
        {
            buffer.Append(@"
<legend>")
.AppendLine("<table border=0><tr><td nowrap>")
.AppendLine(
@"[
<a href=/recipes?strictness=3>balanced recipes</a> |
<a href=/recipes?strictness=0.3>fun recipes</a> |
<a href=/preferences>preferences</a> |
<a href=/logout>logout (" + session.User + @")</a>
 ]")
.AppendLine("</td></tr></table>")
.AppendLine(@"</legend>");
        }

        private static List<KeyValuePair<string, SortedSet<string>>>[] PreferenceColumnLayout(int nColumns)
        {
            var columnContents = new List<KeyValuePair<string, SortedSet<string>>>[nColumns];
            for (int index = 0; index != nColumns; ++index)
                columnContents[index] = new List<KeyValuePair<string, SortedSet<string>>>();
            int nLines = 0;
            foreach (var ingredientGroup in ingredientTypes_)
                nLines += ingredientGroup.Value.Count + 2;
            int linesPerColumn = nLines / nColumns;
            int columnIndex = 0;
            int columnSize = 0;
            foreach (var ingredientGroup in ingredientTypes_)
            {
                // switch to another column?
                if (columnIndex < (nColumns - 1))
                {
                    bool exceededColumnSize = columnSize > linesPerColumn;
                    bool wouldReallyExceedColumnSize = (columnSize + ingredientGroup.Value.Count) >= 1.25 * linesPerColumn;
                    if (exceededColumnSize || (0 < columnSize && wouldReallyExceedColumnSize))
                    {
                        ++columnIndex;
                        columnSize = 0;
                    }
                }
                // add this information into the current column
                columnContents[columnIndex].Add(ingredientGroup);
                columnSize += (ingredientGroup.Value.Count + 2);
            }
            return columnContents;
        }

        private static string TrySavePreferences(HttpListenerRequest request, SessionInfo session, PreferenceInfo preferences)
        {
            var args = GetPostArgs(request);
            try
            {
                var savingPreferences = args["savePreferences"];
                Dictionary<string, int> preferenceLookup = new Dictionary<string, int>();
                foreach (var p in preferences.IngredientPreferences)
                    preferenceLookup[p.Ingredient] = p.Score;
                foreach (string key in args.Keys)
                {
                    if (!key.StartsWith("pref_"))
                        continue;
                    string ingredientName = key.Substring(5);
                    preferenceLookup[ingredientName] = int.Parse(args[key]);
                }
                preferences.IngredientPreferences = new List<Preference>();
                foreach (var p in preferenceLookup)
                    preferences.IngredientPreferences.Add(new Preference { Ingredient = p.Key, Score = p.Value });
                preferences_[session.User.Username] = preferences;
                return "<html><head><meta http-equiv=refresh content='0'/></head></html>";
            }
            catch (Exception e)
            {
                return "<html><body><script>alert('" + e.ToString() + "');</script></body></html>";
            }
        }

        private static void GetUsernamePassword(NameValueCollection postArgs, out string username, out string password)
        {
            try
            {
                username = postArgs["uname"];
                password = postArgs["pswd"];
            }
            catch (Exception)
            {
                username = null;
                password = null;
            }
        }

        private static NameValueCollection GetPostArgs(HttpListenerRequest request)
        {
            /*
            byte[] buffer = new byte[1024 * 1024];
            long already = 0;
            using (System.IO.FileStream o = new FileStream("lastpost.txt", FileMode.Create, FileAccess.Write))
            {
                while (true)
                {
                    int got = request.InputStream.Read(buffer, 0, buffer.Length);
                    already += got;
                    o.Write(buffer, 0, got);
                    if (already >= request.ContentLength64)
                        break;
                }
            }
            if (request.ContentLength64 > 0)
                System.Environment.Exit(1);
            */
            NameValueCollection postArgs;
            byte[] buf = new byte[request.ContentLength64];
            int bytesRead = request.InputStream.Read(buf, 0, (int)request.ContentLength64);
            string s = Encoding.UTF8.GetString(buf, 0, bytesRead);
            postArgs = HttpUtility.ParseQueryString(s);
            return postArgs;
        }


        private static String GetBoundary(String ctype)
        {
            return "--" + ctype.Split(';')[1].Split('=')[1];
        }

        private static void SaveFile(Encoding enc, String boundary, Stream input)
        {
            Byte[] boundaryBytes = enc.GetBytes(boundary);
            Int32 boundaryLen = boundaryBytes.Length;

            using (FileStream output = new FileStream("data.jpg", FileMode.Create, FileAccess.Write))
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

        static void Main(string[] args)
        {
            // load the ingredient settings
            string[] webServerPrefixes = ConfigurationManager.AppSettings["WebServerPrefixes"].Split(',', ' ', ';', '\t');
            string spiderPath = ConfigurationManager.AppSettings["SpiderPath"];
            string classificationFile = ConfigurationManager.AppSettings["ClassificationFile"];
            string ingredientTypesFile = ConfigurationManager.AppSettings["TypesFile"];
            IIngredientClassifier classifier = new RegexIngredientClassifier(classificationFile);
            IIngredientTypes ingredientTypes = new TypesBySensitivity(ingredientTypesFile);
            {
                StringBuilder notFound = new StringBuilder();
                foreach (string ingredientClass in classifier.KnownClassNames)
                {
                    if (!ingredientTypes.ClassToType.ContainsKey(ingredientClass))
                        notFound.Append(ingredientClass).Append(", ");
                }
                string notFoundStr = notFound.ToString();
                if (0 < notFoundStr.Length)
                    throw new Exception("Can't find an ingredient type for class(es) '" + notFoundStr + "' in types from '" + ingredientTypesFile);
            }

            // load the recipe spider data
            foreach (var i in ingredientTypes.ClassToType)
            {
                SortedSet<string> types;
                if (!ingredientTypes_.TryGetValue(i.Value.Group, out types))
                    ingredientTypes_[i.Value.Group] = (types = new SortedSet<string>());
                types.Add(i.Value.Type);
            }
            IngregientClassifierSetup.ParseRecipesImpl(
                "ingregients.parse.log.xls",
                spiderPath,
                classifier,
                ingredientTypes,
                recipes_);
            ingredientsAsColumns_ = PreferenceColumnLayout(5);
            // load the preferences and users
            preferences_ = new PreferenceRepo(new DirectoryInfo("./Preferences"));
            users_ = new UserRepo(new DirectoryInfo("./Users"));
            users_["gene"] = new UserInfo { Username = "gene", Password = "12345" };
            users_["anna"] = new UserInfo { Username = "anna", Password = "12345" };

            // run the web server
            WebServer s = new WebServer(DispatchWebPage, webServerPrefixes);
            s.Run();

            // and don't exit
            while (true) Thread.Sleep(1);
        }
    }
}
