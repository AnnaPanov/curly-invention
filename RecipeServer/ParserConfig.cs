using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;

namespace RecipeServer
{
    public class ParserConfig : ConfigurationSection
    {
        [ConfigurationProperty("ClassificationFile", DefaultValue = "", IsRequired = true)]
        public String ClassificationFile
        {
            get
            {
                return (String)this["ClassificationFile"];
            }
            set
            {
                this["ClassificationFile"] = value;
            }
        }

        [ConfigurationProperty("TypesFile", DefaultValue = "", IsRequired = true)]
        public String TypesFile
        {
            get
            {
                return (String)this["TypesFile"];
            }
            set
            {
                this["TypesFile"] = value;
            }
        }
    }
}
