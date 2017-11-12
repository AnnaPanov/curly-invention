using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Recipes
{
    internal class RecipeParsingUtils
    {
        public static bool LooksLikeMoreIngredients(string text, Regex isIngredientListElement, Regex listElementEnd)
        {
            Match beginMatch = isIngredientListElement.Match(text);
            string oneIngredient = ExtractIngredient(text, beginMatch, listElementEnd);
            if (null == oneIngredient)
                return false;
            // at this point, the rest of the list may look like directions or like ingredients, let's see...
            List<string> potentialMatches = new List<string>();
            potentialMatches.Add(oneIngredient);
            do
            {
                beginMatch = beginMatch.NextMatch();
                if (!beginMatch.Success)
                    break;
                oneIngredient = ExtractIngredient(text, beginMatch, listElementEnd);
                if (null == oneIngredient)
                    break;
                potentialMatches.Add(oneIngredient);
            } while (beginMatch.Success && (null != oneIngredient));
            bool looksLikeDirections = false;
            foreach (string element in potentialMatches)
            {
                if (element.Split(' ', '\t', '\n').Length > 10)
                    looksLikeDirections = true;
            }
            return !looksLikeDirections;
        }

        public static string ExtractIngredient(string text, Match element, Regex listElementEnd)
        {
            string ingredient = null;
            Match end = listElementEnd.Match(text, element.Index + element.Length);
            if (end.Success)
            {
                int start = element.Index + element.Length;
                string found = text.Substring(start, end.Index - start);
                ingredient = allowedTags_.Replace(found, "").Trim().Replace("\n", " ").Replace("\r", "").TrimEnd(';', ',')
                    .Replace("Â½", "1/2").Replace("Â⅓", "1/3").Replace("Â⅔", "2/3").Replace("Â¼", "1/4").Replace("Â", "");
                if (otherTags_.Match(ingredient).Success)
                    ingredient = null; // some bad tags are present
            }
            return ingredient;
        }

        private static readonly Regex allowedTags_ = new Regex("</?(a|p|b|u|div|span|img)\\s?[^>]*>");
        private static readonly Regex otherTags_ = new Regex("<[^>]+>");
    }
}
