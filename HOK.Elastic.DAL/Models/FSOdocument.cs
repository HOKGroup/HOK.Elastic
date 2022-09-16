using Nest;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HOK.Elastic.DAL.Models
{
    public class FSOdocument : FSOfile, IFSOdocument
    {
        private static readonly HashSet<string> _supportedExtensions = new HashSet<string> { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".ppt", ".pptx", ".wpd", ".rtf", ".txt" };//excluded .log file per teams converstation 2021-02-08

        public new static string indexname = StaticIndexPrefix.Prefix + "fsodoc";
        [Text(Ignore = true)]
        public static HashSet<string> SupportedExts { get { return _supportedExtensions; } }
        public byte[] Content { get; set; }
        public Attachment Attachment { get; set; }
        public FSOdocument() : base()
        {
        }
        public FSOdocument(FileInfo fi) : base(fi)
        {
            //SetMetadataFromFileInfo(fi);
        }
        public FSOdocument(IFSO source) : base(source)
        {
            //CopyProperties(source, this);
            //SetPaths(this.Id);
        }

        public static bool CanBeMadeFrom(FileInfo fi)
        {
            return SupportedExts.Contains(fi.Extension, StringComparer.OrdinalIgnoreCase);
        }
        public byte[] GetContent()
        {
            //TODO:it would be ideal to not read this all into memory but would need to explore sending to elastic ingestion node in pages.
            //TODO: figure out if content limit is global or is per file extension/ or do we need evaluation code / filetype further up the chain...
            //todo see how this behaves with files that aren't in the local nasuni cache?
            //try
            //{ 
            byte[] bytes;
            using (FileStream fsSource = new FileStream(PathForCrawlingContent, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bytes = new byte[fsSource.Length];
                int numBytesToRead = (int)fsSource.Length;
                int numBytesRead = 0;
                while (numBytesToRead > 0)
                {
                    // Read may return anything from 0 to numBytesToRead.
                    int n = fsSource.Read(bytes, numBytesRead, numBytesToRead);
                    if (n == 0)
                    {
                        break;
                    }
                    numBytesRead += n;
                    numBytesToRead -= n;
                }
                numBytesToRead = bytes.Length;
            }
            return bytes;
            //TODO should we check if the length equals the fsSource.length and also possibly put error handling in here or outside (in case of network IO errors)
        }
    }
}
