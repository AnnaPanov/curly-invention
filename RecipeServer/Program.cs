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
            try
            {
                if (request.Url.LocalPath == "/recipes_api")
                    return RecipeJsonPage(request, result, session);
                if (request.Url.LocalPath == "/recognize_preferences_api")
                    return RecognizePreferenceRequestPage(request, result, session);
                if (request.Url.LocalPath == "/recognized_preferences")
                    return RecognizePreferencesResultPage(request, result, session);
                if (request.Url.LocalPath == "/recognize_manually")
                    return RecognizePreferencesManualProcessPage(request, result, session);
                if (request.Url.LocalPath == "/preference_image")
                    return PreferenceImage(request, result, session);
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
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
                return e.ToString();
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

        static string PreferenceImage(HttpListenerRequest request, HttpListenerResponse result, SessionInfo session)
        {
            var getArgs = HttpUtility.ParseQueryString(request.Url.Query);
            string requestId = getArgs["requestId"];
            if (null == requestId)
                return "{ response : {\n error : \"requestId is not specified\"\n} }";
            PreferenceInfo p = preferences_[requestId];
            if (null == p)
                return "{ response : {\n error : \"requestId " + requestId + " is not found\"\n} }";
            if (null == p.HaveImage)
                return "{ response : {\n error : \"requestId " + requestId + " does not have an uploaded image\"\n} }";
            if (!System.IO.File.Exists(p.HaveImage))
                return "{ response : {\n error : \"requestId " + requestId + " does not have a file with uploaded image\"\n} }";
            byte[] content = File.ReadAllBytes(p.HaveImage);
            result.ContentType = "image/jpeg";
            result.ContentEncoding = Encoding.UTF8;
            result.ContentLength64 = content.Length;
            result.OutputStream.Write(content, 0, content.Length);
            return null;
        }

        static string RecognizePreferencesManualProcessPage(HttpListenerRequest request, HttpListenerResponse result, SessionInfo session)
        {
            var unrecognizedYet = preferences_.WithUnrecognizedImages();
            if (unrecognizedYet.Count == 0)
                return "No more preferences to recognize!";
            var recognizeMe = unrecognizedYet.FirstOrDefault();
            return PreferencePage(request, result, recognizeMe.Key, recognizeMe.Value);
        }

        static string RecognizePreferencesResultPage(HttpListenerRequest request, HttpListenerResponse result, SessionInfo session)
        {
            var getArgs = HttpUtility.ParseQueryString(request.Url.Query);
            string requestId = getArgs["requestId"];
            if (requestId == null)
                return "{ \"response\" : {\n \"error\" : \"requestId not specified\"\n} }";
            PreferenceInfo recognized = preferences_[requestId];
            if (recognized == null)
                return "{ \"response\" : {\n   \"status\" : \"requestId not found\",\n   \"requestId\" : \"" + requestId + "\"\n} }";
            if (recognized.IngredientPreferences == null)
                return "{ \"response\" : {\n   \"status\" : \"pending\",\n   \"requestId\" : \"" + requestId + "\"\n} }";
            StringBuilder prefs = new StringBuilder();
            prefs.Append("{ \"preferences\" = {\n");
            foreach (Preference p in recognized.IngredientPreferences)
                prefs.AppendFormat("  \"{0}\" : \"{1}\",\n", p.Ingredient, p.Score);
            prefs.Append("} }\n");
            return "{ \"response\" : {\n   \"status\" : \"completed\",\n   \"requestId\" : \"" + requestId + "\",\n   \"result\" : " + prefs.ToString() + "\n} }";
        }

        static string RecognizePreferenceRequestPage(HttpListenerRequest request, HttpListenerResponse result, SessionInfo session)
        {
            if (request.ContentType != null)
            {
                string username = preferences_.SaveImageFile(request.ContentEncoding, GetBoundary(request.ContentType), request.InputStream);
                return "/recognized_preferences?requestId=" + username;
            }
            return
                "<html><body><br>\n" +
                "<form method=post enctype=\"multipart/form-data\">\n" +
                "test results:<input name=testResults type=file>" +
                "<input type=hidden name='id' value='unset'>" +
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
            AddLegend(session.User.Username, buffer);
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
            return PreferencePage(request, result, session.User.Username, preferences);
        }

        static string PreferencePage(HttpListenerRequest request, HttpListenerResponse result, string username, PreferenceInfo preferences)
        {
            // saving preferences?
            if (request.HttpMethod.ToUpper() == "POST")
                return TrySavePreferences(request, username, preferences);

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
            if (preferences.IngredientPreferences != null)
                AddLegend(username, buffer);
            else buffer.Append("<legend>[ requestId : " + username + " ]</legend>\n");
            buffer.Append(@"
<input type=hidden id='username' value='" + username.Replace("'", "\'") + @"'>
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
                if (preferences.IngredientPreferences != null)
                {
                    foreach (var p in preferences.IngredientPreferences)
                        preferenceLookup[p.Ingredient] = p.Score;
                }
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
");
            if (preferences.HaveImage != null)
                buffer.Append("<img onclick='window.open(\"/preference_image?requestId=" + username + "\", \"_blank\", \"toolbar=no,scrollbars=yes,resizable=yes,top=10,left=10\");' src=\"/preference_image?requestId=" + username + "\">\n");
            buffer.Append(@"
</body>
</html>
");
            return buffer.ToString();
        }

        private static void AddLegend(string username, StringBuilder buffer)
        {
            buffer.Append(@"
<legend>")
.AppendLine("<table border=0><tr><td nowrap>")
.AppendLine(
@"[
<a href=/recipes?strictness=3>balanced recipes</a> |
<a href=/recipes?strictness=0.3>fun recipes</a> |
<a href=/preferences>preferences</a> |
<a href=/logout>logout (" + username + @")</a>
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

        private static string TrySavePreferences(HttpListenerRequest request, string username, PreferenceInfo preferences)
        {
            var args = GetPostArgs(request);
            try
            {
                var savingPreferences = args["savePreferences"];
                Dictionary<string, int> preferenceLookup = new Dictionary<string, int>();
                if (preferences.IngredientPreferences != null)
                {
                    foreach (var p in preferences.IngredientPreferences)
                        preferenceLookup[p.Ingredient] = p.Score;
                }
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
                preferences_[username] = preferences;
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
