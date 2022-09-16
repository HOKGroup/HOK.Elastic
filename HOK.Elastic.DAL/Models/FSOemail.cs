using Nest;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HOK.Elastic.DAL.Models
{
    public class FSOemail : FSOdocument, IFSOdocument
    {
        private readonly static HashSet<string> _supportedExtensions = new HashSet<string> { ".msg", ".eml" };//we don't support PST unless we can restrict access only to original recipient.

        //overridden because we need to map alias[Text(Analyzer = InitializationIndex.EMAILADDRESS, SearchAnalyzer = InitializationIndex.EMAILADDRESSSEARCH)]
        public string From { get; set; }

        [Text(Analyzer = InitializationIndex.EMAILADDRESS, SearchAnalyzer = InitializationIndex.EMAILADDRESSSEARCH)]
        public List<string> AllRecipients { get; set; }
        [Text(Analyzer = InitializationIndex.EMAILADDRESS, SearchAnalyzer = InitializationIndex.EMAILADDRESSSEARCH)]
        public List<string> To { get; set; }

        [Text(Analyzer = InitializationIndex.NONWHITESPACEEDGE, SearchAnalyzer = InitializationIndex.NONWHITESPACEEDGESEARCH)]
        public string AttachmentNames { get; set; }

        [Keyword]
        public string ConversationIndex { get; set; }

        public bool HasAttachments { get { return string.IsNullOrEmpty(AttachmentNames) == false; } }

        public DateTime? SentUTC { get; set; }
        public new static string indexname = StaticIndexPrefix.Prefix + "fsomsg";

        [Text(Ignore = true)]
        public new static HashSet<string> SupportedExts { get { return _supportedExtensions; } }

        public new static bool CanBeMadeFrom(System.IO.FileInfo fi)
        {
            return SupportedExts.Contains(fi.Extension, StringComparer.OrdinalIgnoreCase);
        }
        public FSOemail() : base()
        {
        }
        public FSOemail(System.IO.FileInfo fi) : base(fi)
        {
        }
        public FSOemail(IFSO source) : base(source)
        {
        }
    }
}
