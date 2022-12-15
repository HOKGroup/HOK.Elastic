using Nest;
using System;
using System.IO;

namespace HOK.Elastic.DAL.Models
{
    public class FSOfile : FSO, IFSO, IFSOfile
    {
        public static new string indexname = StaticIndexPrefix.Prefix + "fsofile";
        /// <summary>
        /// stored as lowercase
        /// </summary>
        [Keyword(Normalizer = InitializationIndex.LOWERCASE)]//for V3....we should do text.
        public string Extension { get; set; }

        [Keyword(Normalizer = InitializationIndex.LOWERCASE)]
        public string Owner { get; set; }

        public double LengthKB { get; set; }


        public FSOfile() : base()
        {
        }
        public FSOfile(FileInfo fi) : base(fi)//will populate other filesysteminfo properties.
        {
            SetMetadataFromFileInfo(fi);//will populate lengthKB
        }
        /// <summary>
        /// When we construct without passing a FileInfo object, fileinfo properties are null and need to be populated explicitly by calling 'setFileSystemInfo'
        /// </summary>
        /// <param name="source"></param>
        public FSOfile(IFSO source) : base(source)
        {
        }

        private static readonly double KBConversion = 1024;
        internal void SetMetadataFromFileInfo(FileInfo fi)
        {
            Extension = fi.Extension.ToLowerInvariant();
            LengthKB = fi.Length / KBConversion;
        }

        public new void SetFileSystemInfoFromId(FileSystemInfo fileSystemInfo = default)
        {
            base.SetFileSystemInfoFromId(fileSystemInfo);
            FileInfo fi;
            if (fileSystemInfo is FileInfo)
            {
                fi = (FileInfo)fileSystemInfo;
            }
            else if (File.Exists(PathForCrawling))
            {
                fi = new FileInfo(PathForCrawling);
            }
            else
            {
                throw new FileNotFoundException(string.Format("Couldn't create fileinfo for '{0}'", PathForCrawling));
            }
            SetMetadataFromFileInfo(fi);

        }
    }
}
