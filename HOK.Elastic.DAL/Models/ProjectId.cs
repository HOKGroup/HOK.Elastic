using Nest;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace HOK.Elastic.DAL.Models
{
    public class ProjectId
    {
        public ProjectId() { }
        public ProjectId(string number, bool isRestricted, string name)
        {
            Number = number;
            IsRestricted = isRestricted;
            Name = name;
        }
        public bool IsRestricted { get; set; }
        [Text(Analyzer = InitializationIndex.NONWHITESPACEEDGE, SearchAnalyzer = InitializationIndex.NONWHITESPACEEDGESEARCH)]
        public string Name { get; set; }
        //overridden so that fluent mapping can specify wbs1 alias.//[Text(Analyzer = InitializationIndex.NONWHITESPACEEDGE, SearchAnalyzer = InitializationIndex.NONWHITESPACEEDGESEARCH)]
        public string Number { get; set; }
        //overridden in initializationIndex mapping settings to have keyword subfield.
        public string FullName { get => (Number.Equals(NotSet) || Name.Equals(NotSet)) ? NotSet : string.Join(IsRestricted ? "+" : " ", Number, Name).Trim(); }//Trim to empty string if unpopulated.

        public readonly static string NotSet = "N/A";
        
    }
}

