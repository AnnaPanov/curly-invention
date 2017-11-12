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
    public interface IRecipeDownloader
    {
        bool Finished { get; }
        string UrlRoot { get; }
        RawWebPage DownloadNext();
        IEnumerable<RawWebPage> DownloadedAlready { get; }
    }

    public static class MyExtensions
    {
        public static string RemoveStart(this string candidate, params string[] what)
        {
            string result = candidate;
            foreach (string w in what)
            {
                if (result.StartsWith(w))
                {
                    int size = w.Length;
                    result = result.Substring(size, result.Length - size);
                }
            }
            return result;
        }
    }

    public abstract class RecipeDownloader : IRecipeDownloader
    {
        public RecipeDownloader(string nextUrl, string path, string source, string urlRoot)
        {
            // things not specific to Kraft
            nextUrl_ = nextUrl;
            source_ = source;
            urlRoot_ = urlRoot;
            if (!Directory.Exists(path + "\\" + source_))
                Directory.CreateDirectory(path + "\\" + source_);
            DirectoryInfo downloaded = new DirectoryInfo(path + "\\" + source_);
            foreach (FileInfo file in downloaded.GetFiles())
            {
                using (StreamReader input = file.OpenText())
                {
                    try
                    {
                        RawWebPage rwp = (RawWebPage)serializer_.Deserialize(input);
                        if (null != rwp)
                        {
                            for (int index = 0; index != rwp.ReferencedUrls.Length; ++index)
                                rwp.ReferencedUrls[index] = TrimUrlArgs(rwp.ReferencedUrls[index]);
                            rwp.Url = TrimUrlArgs(rwp.Url);
                            discoveredUrls_.Add(rwp.Url);
                            downloadedRecipes_[rwp.Url] = file.FullName;
                            foreach (var referencedUrl in rwp.ReferencedUrls)
                                discoveredUrls_.Add(referencedUrl);
                            rwp.UrlRoot = urlRoot_;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine("Failed to deserialize the contents of {0}: {1}", file.FullName, e.ToString());
                    }
                }
            }
            foreach (var discovered in discoveredUrls_)
                urlsToDownload_.Add(discovered);
            foreach (var already in downloadedRecipes_)
                urlsToDownload_.Remove(already.Key);

            // if a given url was already downloaded, skip it
            if (downloadedRecipes_.ContainsKey(nextUrl_))
                PickNextUrl();
        }

        internal static string TrimUrlArgs(string url)
        {
            int questionMark = url.IndexOf('?');
            if (0 < questionMark)
                url = url.Substring(0, questionMark);
            int pound = url.IndexOf('#');
            if (0 < pound)
                url = url.Substring(0, pound);
            if (url == "")
                url = "/";
            return url;
        }

        public bool Finished { get { return null == nextUrl_; } }

        public string UrlRoot { get { return urlRoot_; } }

        public RawWebPage DownloadNext()
        {
            Console.WriteLine("{0}: Downloaded {1} out of {2} discovered recipes ({3})", DateTime.Now, downloadedRecipes_.Count, discoveredUrls_.Count, source_);
            Console.WriteLine("{0}: Downloading {1} ({2})...", DateTime.Now, nextUrl_, source_);

            string url = nextUrl_;
            urlsToDownload_.Remove(nextUrl_);
            PickNextUrl();

            string title, text;
            HashSet<string> referencedUrls;
            try
            {
                DownloadRawPage(url, out title, out text, out referencedUrls);
            }
            catch (Exception e)
            {
                Console.WriteLine("Problem when downloading from {0} (will try later): {1}", url, e.ToString());
                downloadLater_.Add(url);
                throw;
            }

            RawWebPage result = new RawWebPage();
            result.Url = url;
            result.UrlRoot = urlRoot_;
            result.Source = source_;
            result.RawText = text;
            result.UtcTimeStamp = DateTime.UtcNow;
            result.Name = title;
            result.ReferencedUrls = referencedUrls.ToArray();
            
            string filename = title.Replace(' ', '_') + ".raw";
            string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            foreach (char c in invalidChars)
                filename = filename.Replace(c.ToString(), "");
            filename = source_ + "/" + filename;

            downloadedRecipes_[url] = filename;

            // very important to do this after storing the new page
            foreach (var discovered in referencedUrls)
            {
                discoveredUrls_.Add(discovered);
                if (!downloadedRecipes_.ContainsKey(discovered))
                    urlsToDownload_.Add(discovered);
            }

            // in case we now have more urls to choose from, choose again
            PickNextUrl();

            // save to disk now (because even if this results in an exception, we already advanced the iterator)
            using (TextWriter w = new StreamWriter(filename))
                serializer_.Serialize(w, result);
            return result;
        }

        public static string RemoveSpecialCharacters(string ingredientName)
        {
            return ingredientName.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("Ã©", "e");
        }

        public IEnumerable<RawWebPage> DownloadedAlready
        {
            get
            {
                foreach (var recipeFile in downloadedRecipes_)
                {
                    RawWebPage result = null;
                    try
                    {
                        using (StreamReader r = new StreamReader(recipeFile.Value))
                        {
                            result = (RawWebPage)serializer_.Deserialize(r);
                            result.FileName = recipeFile.Value;
                            result.UrlRoot = urlRoot_;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine("Problem in RecipeDownloaderBase::DownloadedAlready: {0}", e.ToString());
                        continue;
                    }
                    yield return result;
                }
            }
        }

        private void PickNextUrl()
        {
            if (0 == urlsToDownload_.Count)
            {
                foreach (var url in downloadLater_)
                    urlsToDownload_.Add(url);
                downloadLater_.Clear();
            }
            nextUrl_ = 0 < urlsToDownload_.Count ? urlsToDownload_.First() : null;
        }

        // kraft-specific stuff
        protected abstract void DownloadRawPage(
            string url, out string title, out string text, out HashSet<string> referencedUrls);

        private string nextUrl_;
        private readonly string source_;
        private HashSet<string> downloadLater_ = new HashSet<string>();
        private HashSet<string> discoveredUrls_ = new HashSet<string>();
        private HashSet<string> urlsToDownload_ = new HashSet<string>();
        private Dictionary<string, string> downloadedRecipes_ = new Dictionary<string, string>();
        private XmlSerializer serializer_ = new XmlSerializer(typeof(Recipes.RawWebPage));
        private readonly string urlRoot_;
    }
}
