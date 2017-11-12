using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Recipes
{
    /// <summary>
    /// Name of an ingredient (each ingredient has Name and Quantity)
    /// </summary>
    public class IngredientName : IEquatable<IngredientName>
    {
        public string Name { get; set; }
        // making it a key
        public bool Equals(IngredientName other) { return null != other && other.Name == Name; }
        public override int GetHashCode() { return Name.GetHashCode(); }
        public override bool Equals(object obj) { return Equals(obj as IngredientName); }
        public override string ToString() { return Name; }
    }

    /// <summary>
    /// Ingredient (Name and Quantity)
    /// </summary>
    public class Ingredient
    {
        // how it was declared
        public string Declaration { get; set; }

        // how it was interpreted
        public IngredientName Name { get; set; }
        public string Quantity { get; set; }
        public string Detail { get; set; }

        // how we want it displayed
        public override string ToString()
        {
            return Declaration;
        }
    }

    /// <summary>
    /// Recipe is a list of ingredients along with the original web page (and probably a picture later on)
    /// </summary>
    public class Recipe
    {
        public List<Ingredient> Ingredients = new List<Ingredient>();
        public RawWebPage OriginalWebPage { get; set; }
    }
}
