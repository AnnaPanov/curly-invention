using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using System.Net;
using System.IO;

namespace Recipes
{
    public class KraftRecipeDownloader : RecipeDownloader
    {
        public KraftRecipeDownloader(string nextUrl, string path) : base(nextUrl, path, "KraftRecipes", "http://www.kraftrecipes.com") { }

        // kraft-specific stuff
        protected override void DownloadRawPage(string url, out string title, out string text, out HashSet<string> referencedUrls)
        {
            using (WebClient client = new System.Net.WebClient())
            {
                client.Headers.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 7.0; Windows NT 5.2; .NET CLR 1.0.3705;)");

                title = null;
                referencedUrls = new HashSet<string>();
                text = client.DownloadString("http://www.kraftrecipes.com" + url);

                Regex findTitle = new Regex("<title>([^<]+)</title>");
                Match foundTitle = findTitle.Match(text);
                if (foundTitle.Success)
                    title = foundTitle.Groups[1].Value.Trim();
                //System.IO.File.ReadAllText("text.txt");

                // discover all the links in this url
                Regex findLinks = new Regex("href=\"(/[^\"]+)\"");
                Match linksFound = findLinks.Match(text);
                while (linksFound.Success)
                {
                    string candidate = linksFound.Groups[1].Value
                        .RemoveStart("http://", "www.", "kraftrecipes.com");
                    linksFound = linksFound.NextMatch();
                    if (!(candidate.StartsWith("/recipes/") ||
                          candidate.StartsWith("/everyday-meals/")
                        ))
                        continue;
                    candidate = TrimUrlArgs(candidate);
                    referencedUrls.Add(candidate);
                }
            }
        }
    }
}
