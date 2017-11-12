using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.Threading.Tasks;

namespace Recipes
{
    public class RawWebPage
    {
        public string[] ReferencedUrls = new string[] {};

        public string Url { get; set; }

        public string UrlRoot { get; set; }

        public DateTime UtcTimeStamp { get; set; }

        public string RawText { get; set; }

        public string Source { get; set; }

        public string Name { get; set; }

        [XmlAttribute("Optional")]
        public string FileName { get; set; }

        public override string ToString()
        {
            return "[" + Source + "] " + Name;
        }
    }
}
