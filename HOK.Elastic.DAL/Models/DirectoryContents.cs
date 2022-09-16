using System;
using System.Collections.Generic;

namespace HOK.Elastic.DAL.Models
{
    public class DirectoryContents : FSO, IFSO
    {
        public HashSet<Content> Contents { get; set; }
        public class Content : Tuple<string, string, ACLs, DateTime, int>
        {
            public Content(string Id, string index, ACLs acls, DateTime lastwriteutc, int failureCount) : base(Id, index, acls, lastwriteutc, failureCount)
            {
            }
        }
    }
}