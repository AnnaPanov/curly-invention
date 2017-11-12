using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Net;

namespace Recipes
{
    public class BettyRecipeDownloader : RecipeDownloader
    {
        public BettyRecipeDownloader(string nextUrl, string path) : base(nextUrl, path, "BettyCrocker", "http://www.bettycroker.com") { }

        // kraft-specific stuff
        protected override void DownloadRawPage(string url, out string title, out string text, out HashSet<string> referencedUrls)
        {
            using (WebClient client = new System.Net.WebClient())
            {
                client.Headers.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 7.0; Windows NT 5.2; .NET CLR 1.0.3705;)");

                title = null;
                referencedUrls = new HashSet<string>();
                text = client.DownloadString("http://www.bettycrocker.com" + url);

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
                        .RemoveStart("http://", "www.", "bettycrocker.com");
                    linksFound = linksFound.NextMatch();
                    if (!(candidate.StartsWith("/recipes/") ||
                          candidate.StartsWith("/everyday-meals/") ||
                          candidate.StartsWith("/special-occasions/") ||
                          candidate.StartsWith("/menus-holidays-parties/")))
                        continue;
                    candidate = TrimUrlArgs(candidate);
                    referencedUrls.Add(candidate);
                }
            }
        }
    }
}
