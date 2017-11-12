using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Recipes
{
    public class PaleoLeapRecipeParser : IRecipeParser
    {
        static readonly Regex isRecipeUrl_ = new Regex("^/[^/]+/$");
        static readonly Regex isListElement_ = new Regex("<li[^>]*>");
        static readonly Regex isListElementEnd_ = new Regex("</li>");
        static readonly Regex startsWithNumbers_ = new Regex("^[0-9]");
        static readonly Regex bodyTag_ = new Regex("<body[^>]*>", RegexOptions.IgnoreCase);

        public RecipeParsingResult TryParseRawWebPage(RawWebPage rawWebPage)
        {
            if (!isRecipeUrl_.Match(rawWebPage.Url).Success)
                return RecipeParsingResult.Error("not a recipe web page");
            string text = rawWebPage.RawText;
            string[] headAndBody = bodyTag_.Split(text);
            if (headAndBody.Length < 2)
                return RecipeParsingResult.Error("body tag not found");
            if (headAndBody.Length > 2)
                return RecipeParsingResult.Error("more than one body tag found");
            text = headAndBody[1];
            string[] splitByIngredients = text.Split(new string[] { ">Ingredients<" }, StringSplitOptions.RemoveEmptyEntries);
            if (splitByIngredients.Length < 2)
            {
                // if the hail mary thing didn't work, then complain
                if (startsWithNumbers_.Match(rawWebPage.Name).Success)
                    return RecipeParsingResult.Error("didn't find ingredients, but the title starts with numbers");
                else if (rawWebPage.Url.StartsWith("/all-about-"))
                    return RecipeParsingResult.Error("didn't find ingredients, but this is an 'all about' web page");
                else if (rawWebPage.Url.StartsWith("/about-"))
                    return RecipeParsingResult.Error("didn't find ingredients, but this is an 'about' web page");
                else if (rawWebPage.Url.StartsWith("/are-"))
                    return RecipeParsingResult.Error("didn't find ingredients, but this is an 'are' web page");
                else if (rawWebPage.Url.StartsWith("/is-"))
                    return RecipeParsingResult.Error("didn't find ingredients, but this is an 'is' web page");
                else
                    return RecipeParsingResult.Error("didn't find ingredients");
            }
            if (splitByIngredients.Length > 2)
            {
                // hail mary #2
                string[] splitByIngredientsLt = text.Split(new string[] { "ngredients:<" }, StringSplitOptions.RemoveEmptyEntries);
                if (splitByIngredientsLt.Length == 2)
                    splitByIngredients = splitByIngredientsLt;
            }
            if (splitByIngredients.Length > 2)
                return RecipeParsingResult.Error("more than one ingredient section found");
            text = splitByIngredients[1];
            string[] splitByEndOfListOrDirections = text.Contains("irections:") ?
                text.Split(new string[] { "irections:" }, StringSplitOptions.RemoveEmptyEntries) :
                text.Split(new string[] { "</ul>" }, StringSplitOptions.RemoveEmptyEntries);
            if (splitByEndOfListOrDirections.Length < 2)
                return RecipeParsingResult.Error("end-of-list not found");
            text = splitByEndOfListOrDirections[0];
            if (splitByEndOfListOrDirections.Length > 1 && RecipeParsingUtils.LooksLikeMoreIngredients(splitByEndOfListOrDirections[1], isListElement_, isListElementEnd_))
                text = splitByEndOfListOrDirections[0] + "</ul>" + splitByEndOfListOrDirections[1];
            Match element = isListElement_.Match(text);
            if (!element.Success)
                return RecipeParsingResult.Error("not a single ingredient found");

            List<string> ingredients = new List<string>();
            while (element.Success)
            {
                string ingredient = RecipeParsingUtils.ExtractIngredient(text, element, isListElementEnd_);
                if (null == ingredient)
                    break;
                ingredients.Add(ingredient);
                element = element.NextMatch();
            }

            var quantityFilter = new Regex(
                "^"
                + "([0-9/â„]+\\sto\\s)?"
                + "(Â½|[0-9/\\- â„]+|0\\.5|0\\.25|0\\.75|1\\.5|half|one third|one quarter|one|two|three|four|five|six|seven|eight|nine|ten|a few)?"
                + "(\\s?(lbs?|pounds?|grams?|quarts?|oz|ounces?|cups?|tsps?|tbsp?|tbsps?|tbsps?|half|halves|thirds?|quarters?|pinch(es)?|cloves?|links?|sprigs?|tea\\s?spoons?|table\\s?spoons?|inch(es)?)\\.?)?"
                + "(\\s?\\([^)]+\\))?"
                + "(\\s?(bag|pouch|box|bottle|pack)[^a-zA-Z]+)?"
                + "(\\s?of)?"
                , RegexOptions.IgnoreCase);

            Recipe result = new Recipe { OriginalWebPage = rawWebPage };
            foreach (string ingredientText in ingredients)
            {
                string quantity = "", detail = "";
                string ingredient = ingredientText;
                var quantityFound = quantityFilter.Match(ingredient);
                if (quantityFound.Success)
                {
                    quantity = quantityFound.Value.Trim();
                    ingredient = ingredientText.Substring(quantity.Length, ingredientText.Length - quantity.Length).Trim();
                }
                uint commaFound = (uint)ingredient.IndexOf(',');
                uint braceFound = (uint)ingredient.IndexOf('(');
                uint detailFound = Math.Min(commaFound, braceFound);
                if (-1 != (int)detailFound)
                {
                    detail = ingredient.Substring((int)detailFound, ingredient.Length - (int)detailFound);
                    ingredient = ingredient.Substring(0, (int)detailFound);
                    detail = detail.Trim(',', '\t', ' ').Trim();
                }
                result.Ingredients.Add(new Ingredient()
                {
                    Declaration = ingredientText,
                    Quantity = quantity,
                    Name = new IngredientName() { Name = RecipeDownloader.RemoveSpecialCharacters(ingredient) },
                    Detail = detail,
                });
            }
            if (ingredients.Count < 2)
                return RecipeParsingResult.Error("less than three ingredients");
            return RecipeParsingResult.Success(result); // no errors
        }
    }
}
