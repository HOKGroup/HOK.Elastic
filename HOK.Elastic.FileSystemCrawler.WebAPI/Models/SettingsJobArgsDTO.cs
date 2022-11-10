using HOK.Elastic.FileSystemCrawler.Models;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace HOK.Elastic.FileSystemCrawler.WebAPI.Models
{
    public class SettingsJobArgsDTO:FileSystemCrawler.Models.SettingsJobArgs
    {
        public new string JobName { get; set; }
        public new InputPathList InputPaths { get; set; }//TODO json serialization problem when posting

        public class InputPathList
        {
            public  IEnumerable<InputPathEventStream> Events { get; set; }
            public  IEnumerable<InputPathBase> Crawls { get; set; }
        }        
    }
}
