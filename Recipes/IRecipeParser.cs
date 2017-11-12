using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Recipes
{
    public struct RecipeParsingResult
    {
        public Recipe Result;
        public string ErrorMessage;
        public bool Succeeded { get { return null == ErrorMessage; } }

        public static RecipeParsingResult Error(string errorMessage) { return new RecipeParsingResult { ErrorMessage = errorMessage, Result = null }; }
        public static RecipeParsingResult Success(Recipe recipe) { return new RecipeParsingResult { ErrorMessage = null, Result = recipe }; }
    }

    public interface IRecipeParser
    {
        RecipeParsingResult TryParseRawWebPage(RawWebPage rawWebPage);
    }
}
