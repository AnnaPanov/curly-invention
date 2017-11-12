using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Text.RegularExpressions;
using System.IO;
using Recipes;

namespace RecipeTests
{
    class Program
    {
        static void Main(string[] args)
        {
            IIngredientClassifier classifier = new RegexIngredientClassifier("classification.xls");
            IIngredientTypes ingredientTypes = new TypesBySensitivity("types.by.sensitivity.xls");
            ParseRecipes(classifier, ingredientTypes);
            //DownloadRecipes();
        }

        private static void ParseRecipes(IIngredientClassifier classifier, IIngredientTypes types)
        {
            List<ClassifiedRecipe> result = new List<ClassifiedRecipe>();
            IngregientClassifierSetup.ParseRecipesImpl("ingredients.xls", "./", classifier, types, result);
            string tempFile = System.IO.Path.GetTempFileName();
            File.Copy("ingredients.xls", tempFile, true);
            System.Diagnostics.Process.Start("excel.exe", tempFile);
        }

        private static void DownloadRecipes()
        {
            // start the downloaders
            List<Thread> downloaders = new List<Thread>();
            downloaders.Add(DownloadAsync(new SkinnyTasteRecipeDownloader("/", ".")));
            downloaders.Add(DownloadAsync(new PaleoLeapRecipeDownloader("/", ".")));
            downloaders.Add(DownloadAsync(new KraftRecipeDownloader("/", ".")));
            // wait until they all finish
            foreach (Thread t in downloaders)
                t.Join();
        }

        private static Thread DownloadAsync(IRecipeDownloader rd)
        {
            var t = new Thread(() => { DownloadAll(rd); });
            t.IsBackground = true;
            t.Start();
            return t;
        }

        private static void DownloadAll(IRecipeDownloader rd)
        {
            Random r = new Random();
            while (!rd.Finished)
            {
                try
                {
                    // download
                    RawWebPage rwp = rd.DownloadNext();
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("{0}: {1}", DateTime.Now, e.ToString());
                }
                // sleep
                int delay = 1000 + (int)(4000 * r.NextDouble());
                System.Threading.Thread.Sleep(delay);
            }
        }
    }
}
