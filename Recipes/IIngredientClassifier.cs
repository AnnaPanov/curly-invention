using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Recipes
{
    public class IngredientClassification
    {
        /// <summary>
        /// ingredients which were classified successfully
        /// </summary>
        public Dictionary<IngredientName, string> Classified = new Dictionary<IngredientName, string>();

        /// <summary>
        /// ingredients which weren's successfully classified
        /// </summary>
        public HashSet<IngredientName> Unclassified = new HashSet<IngredientName>();
    };

    public class ClassifiedRecipe
    {
        public bool Succeeded { get { return null == Error; } }
        public IngredientClassification Classification { get; set; }
        public Recipe Recipe { get; set; }
        public string Error { get; set; }
    }

    public class IngredientType
    {
        /// <summary> For example, "starchy veggies" </summary>
        public string Group = "[unassigned]";
        /// <summary> For example, "potatoes" </summary>
        public string Type = "[unassigned]";
    }

    public interface IIngredientTypes
    {
        Dictionary<string, IngredientType> ClassToType { get; }
    }

    public interface IIngredientClassifier
    {
        ClassifiedRecipe ClassifyIngredients(Recipe recipe);
        IEnumerable<string> KnownClassNames { get; }
    }

    public class IngregientClassifierSetup
    {
        public static void ParseRecipesImpl(
            string ingredientsLogFileName, string downloaderPath, IIngredientClassifier classifier, IIngredientTypes types,
            List<ClassifiedRecipe> recipes)
        {
            Dictionary<string, int> whyNotParsed = new Dictionary<string, int>();

            var sources = new List<KeyValuePair<IRecipeDownloader, IRecipeParser>>();

            // adding skinnytaste
            sources.Add(
                new KeyValuePair<IRecipeDownloader, IRecipeParser>(
                    new SkinnyTasteRecipeDownloader("/", downloaderPath),
                    new SkinnyTasteRecipeParser()
                ));

            // adding paleoleap
            sources.Add(
                new KeyValuePair<IRecipeDownloader, IRecipeParser>(
                    new PaleoLeapRecipeDownloader("/", downloaderPath),
                    new PaleoLeapRecipeParser()
                ));

            int recipesTotal = 0, recipesParsed = 0;
            HashSet<string> ingredientTypesUsed = new HashSet<string>();
            HashSet<string> ingredientGroupsUsed = new HashSet<string>();
            using (StreamWriter interpretedIngredients = new StreamWriter(ingredientsLogFileName))
            {
                interpretedIngredients.WriteLine("url\tdeclaration\tdetail\tquantity\tname\tclass\ttype");
                foreach (var source in sources)
                {
                    IRecipeDownloader d = source.Key;
                    IRecipeParser p = source.Value;
                    foreach (RawWebPage rawWebPage in d.DownloadedAlready)
                    {
                        ++recipesTotal;
                        rawWebPage.Name = RecipeDownloader.RemoveSpecialCharacters(rawWebPage.Name);
                        RecipeParsingResult parsed = p.TryParseRawWebPage(rawWebPage);
                        if (!parsed.Succeeded)
                        {
                            Console.Error.WriteLine("WARNING: failed to parse {0}:{1}:{2} ({3})",
                                rawWebPage.Source, rawWebPage.Url, rawWebPage.FileName, parsed.ErrorMessage);
                            if (!whyNotParsed.ContainsKey(parsed.ErrorMessage))
                                whyNotParsed[parsed.ErrorMessage] = 0;
                            ++whyNotParsed[parsed.ErrorMessage];
                            continue;
                        }
                        ++recipesParsed;
                        Recipe recipe = parsed.Result;
                        ClassifiedRecipe makingSenseOfIt = classifier.ClassifyIngredients(recipe);
                        if (makingSenseOfIt.Succeeded)
                            recipes.Add(makingSenseOfIt);
                        foreach (var ingredient in recipe.Ingredients)
                        {
                            // understand what kind of ingredient this is
                            string ingredientClass = null;
                            IngredientType ingredientType = null;
                            if (null != ingredient.Name && !makingSenseOfIt.Classification.Classified.TryGetValue(ingredient.Name, out ingredientClass))
                                ingredientClass = null;
                            if (null != ingredientClass && !types.ClassToType.TryGetValue(ingredientClass, out ingredientType))
                                ingredientType = null;
                            if (null == ingredientClass)
                                ingredientClass = "[unknown]";
                            if (null == ingredientType)
                                ingredientType = new IngredientType();
                            interpretedIngredients.WriteLine("{0}\t{1}\t{2}\t=\"{3}\"\t{4}\t{5}\t{6}",
                                d.UrlRoot + recipe.OriginalWebPage.Url,
                                ingredient.Declaration,
                                ingredient.Detail,
                                ingredient.Quantity,
                                ingredient.Name.Name,
                                ingredientClass,
                                ingredientType);
                            ingredientTypesUsed.Add(ingredientType.Type);
                            ingredientGroupsUsed.Add(ingredientType.Group);
                        }
                    }
                }
            }
            Console.WriteLine("Recipes: {0} total, {1} parsed, {2} distinct ingredient types, {3} distinct ingredient groups used",
                recipesTotal, recipesParsed, ingredientTypesUsed.Count, ingredientGroupsUsed.Count);
            List<KeyValuePair<string, int>> whyNot = new List<KeyValuePair<string, int>>(whyNotParsed);
            whyNot.Sort((KeyValuePair<string, int> x, KeyValuePair<string, int> y) => { return -x.Value.CompareTo(y.Value); });
            foreach (var whyNotElement in whyNot)
                Console.WriteLine("Failed to parse {0} recipe pages because {1}.", whyNotElement.Value, whyNotElement.Key);
        }
    }
}
