using System.IO;

namespace HOK.Elastic.DAL.Models
{
    public class FSOdirectory : FSO, IFSO
    {
        public static new string indexname = StaticIndexPrefix.Prefix + "dir";
        /// <summary>
        /// in V# possibly change this name to ProjectRoot
        /// </summary>
        public bool IsProjectRoot
        {
            get =>
Name.Equals(Project?.FullName, System.StringComparison.OrdinalIgnoreCase) &&
Parent.IndexOf(Project?.FullName, System.StringComparison.OrdinalIgnoreCase) < 0;
        }
        public FSOdirectory() : base()
        { }
        public FSOdirectory(DirectoryInfo di) : base(di)
        { }
        public FSOdirectory(DirectoryInfo di, string office) : base(di, office)
        { }
        public FSOdirectory(DirectoryInfo di, string office, ProjectId project) : base(di, office, project)
        { }
        public FSOdirectory(IFSO source) : base()
        {
            CopyProperties(source, this);
        }
    }
}
