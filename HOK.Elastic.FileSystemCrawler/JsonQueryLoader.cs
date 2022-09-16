using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace  HOK.Elastic.FileSystemCrawler
{
   public static partial class JsonQueryLoader 
   {
	public const string RECRAWLJSONQUERY = "missingcontentquery.json";

        /// <summary>
        /// 
        /// </summary>
        /// <param name="source">source folder to load missing content query from</param>
        /// <returns></returns>
        public static string LoadMissingContentQuery(string source)
        {
            string content = File.ReadAllText(Path.Combine(source, RECRAWLJSONQUERY));
            var obj = JsonSerializer.Deserialize<object>(content); // Deserialize to check for valid JSON
            content = JsonSerializer.Serialize(obj);  // Re-serialize to remove any whitespace without affecting values
            return content;
        }
   }
}