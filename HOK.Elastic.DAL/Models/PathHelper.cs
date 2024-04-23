using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HOK.Elastic.DAL.Models
{
    public static class PathHelper
    {
        /// <summary>
        /// Pass the paths in and they will be converted to lowercase
        /// </summary>
        /// <param name="DFSorUNCakaPublishedRoot">Path where file can be found</param>
        /// <param name="contentRoot">should be specified as longpath legacy format \\?\unc\</param>
        /// <param name="crawlRoot">should be specified as longpath legacy format \\?\unc\</param>
        public static void Set(string DFSorUNCakaPublishedRoot, string contentRoot, string crawlRoot)
        {
            PublishedRoot = DFSorUNCakaPublishedRoot.ToLowerInvariant();
            PublishedRootLongPath = LongPaths.GetLegacyLongPath(PublishedRoot);
            ContentRoot = LongPaths.GetLegacyLongPath(contentRoot).ToLowerInvariant();
            CrawlRoot = LongPaths.GetLegacyLongPath(crawlRoot).ToLowerInvariant();
        }
        /// <summary>
        /// dfs path component;always returned as lowercase
        /// </summary>
        public static string PublishedRoot { get; internal set; }
        /// <summary>
        /// dfs path component;always returned as lowercase
        /// </summary>
        public static string PublishedRootLongPath { get; internal set; }
        /// <summary>
        /// Root path for crawling filesystem content (reading file contents)
        /// </summary>
        public static string ContentRoot { get; internal set; }
        /// <summary>
        /// Root path for crawling filesystem (may be different than published path...could be path to a specific endpoint device)
        /// </summary>
        public static string CrawlRoot { get; internal set; }
        /// <summary>
        /// convenience method to be used occasionally when filesystemwrapper or directorywrapper's properties aren't available
        /// </summary>
        /// <param name="path">path as lowercase</param>
        /// <returns></returns>
        public static string GetPublishedPath(string path)
        {
            if (path.StartsWith(CrawlRoot, StringComparison.OrdinalIgnoreCase))
            {
                return path.Replace(CrawlRoot, PublishedRoot);
            }
            else if (path.StartsWith(ContentRoot, StringComparison.OrdinalIgnoreCase))
            {
                return path.Replace(ContentRoot, PublishedRoot);
            }
            else if (path.StartsWith(PublishedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
            else if (path.StartsWith(PublishedRootLongPath, StringComparison.OrdinalIgnoreCase))
            {
                return path.Replace(PublishedRootLongPath, PublishedRoot);
            }
            else
            {
                return path;//we sometimes get here when testing if the permissions acl guardian are shallower than the crawling or content path
            }
        }

        #region Office
        private static Regex _compiledOfficeRgx;
        public static Regex OfficeExtractRgx
        {
            get { return _compiledOfficeRgx; }//fail if not set.
        }
        /// <summary>
        /// A pattern with a single capture group that will extract the office/site from the path.
        /// </summary>
        /// <param name="pattern">example "^\\\\contoso\\sites\\([\w]{2,4})"</param>
        /// <returns></returns>
        public static Regex SetOfficeExtractRgx(string pattern)
        {
            _compiledOfficeRgx = MakeCompiledRegex(pattern);
            return _compiledOfficeRgx;
        }

        #endregion
        #region Project
        private static Regex _compiledProjectRgx;
        //TODO externally define this. We could also possibly make the project fields a metadata construct configured by regex with named capture groups for example.
        public static Regex ProjectExtractRgx
        {
            get { return _compiledProjectRgx; }//fail if not set
        }
        /// <summary>
        /// A pattern with capture groups (expand documentation possibly) that will extract the projectdetails from the path.
        /// </summary>
        /// <param name="pattern">example "^\\\\contoso\\sites\\([\w]{2,4})"</param>
        /// <returns></returns>
        public static Regex SetProjectExtractRgx(string pattern)
        {
            _compiledProjectRgx = MakeCompiledRegex(pattern);
            return _compiledProjectRgx;
        }


        #endregion
        #region Exclusions
        /// <summary>
        /// always exclude at least these:
        /// </summary>
        private readonly static HashSet<string> BadExtensionList = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".log", ".slog", ".rws", ".dat", ".ds_store", ".appdisk", ".tmp", ".bak", ".skb", ".sv$", ".dwl", ".dwl2", ".err", ".dmp", ".idlk", "" };//added "" to skip files without extensions (like autocad temp files(that at other times end in .tmp sometimes)//we remove digits from extensions and compare with removed digits
        private static Regex _compiledFileNameExclusionRegex;
        private static Regex _compiledPathInclusionRegex;
        public static Regex FileNameExclusion
        {
            get { return _compiledFileNameExclusionRegex ?? SetFileNameExclusion(@"^(?:thumbs.db|desktop.ini|~.*|\._)|\.(?:tmp.*|bk[0-9]$)"); }//default fallback for regex if custom regex not provided
        }

        public static Regex PathInclusion
        {
            get { return _compiledPathInclusionRegex ?? SetPathInclusion(@"^\\?\\"); }//default fallback for regex if custom regex not provided
        }
        public static HashSet<string> IgnoreExtensions { get { return BadExtensionList; } set { value?.Distinct().Where(x => !BadExtensionList.Contains(x)).Select(x => BadExtensionList.Add(x)); } }

        /// <summary>
        /// Method to filter out common temporary extensions or ignore as well as the containing folder
        /// </summary>
        /// <param name="path">Pass full path to file</param>
        /// <returns></returns>

        public static Regex SetFileNameExclusion(string regex)
        {
            _compiledFileNameExclusionRegex = MakeCompiledRegex(regex);
            return _compiledFileNameExclusionRegex;
        }


        /// <summary>
        ///  ^\\\\\\\\\\?\\\\c\\:\\\\([^\\\\|$\\r\\n]*\\\\){0,2}[^\\\\]+$ would crawl c drive up to a couple levels.
        ///  @"^\\?\\" to crawl anything
        ///this is temporary until we crawl departmental data; otherwise event crawler will match non-projects. Must match the following examples:
        ///\\contoso\houston\projects OR 
        ///\\?\unc\contoso\houston\projects
        ///\internal\houston\projects OR 
        ///\\?\unc\contoso-server1\volume\external\newyork\projects
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static Regex SetPathInclusion(string regex)
        {
            _compiledPathInclusionRegex = MakeCompiledRegex(regex);
            return _compiledPathInclusionRegex;
        }

        public static bool ShouldIgnoreFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            return CheckShouldIgnoreFile(System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileName(path), System.IO.Path.GetExtension(path));
        }
        public static bool ShouldIgnoreDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            return CheckShouldIgnoreDirectoryPath(path);
        }
        public static bool ShouldIgnoreFile(System.IO.FileInfo fi)
        {
            if (fi is null) return true;
            return (CheckShouldIgnoreFile(fi.DirectoryName, fi.Name, fi.Extension));
        }

        private static bool CheckShouldIgnoreFile(string directoryPath, string fileName, string extension)
        {
            if (CheckShouldIgnoreDirectoryPath(directoryPath))
            {
                return true;
            }
            else
            {
                if (BadExtensionList.Contains(extension)) return true;
                return FileNameExclusion?.IsMatch(fileName) ?? false;
            }
        }
        private static bool CheckShouldIgnoreDirectoryPath(string path)
        {
            if (!PathInclusion?.IsMatch(path) ?? false) return true;
            if (System.Text.Encoding.UTF8.GetByteCount(path) > 512) return true;//detect long paths and skip them as the path is too long for Id field and unsupported.
            return false;
        }

        #endregion
        private static Regex MakeCompiledRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return null;
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
    }
}
