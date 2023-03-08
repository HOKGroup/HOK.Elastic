using Nest;
using System;
using System.IO;
using System.Linq;

namespace HOK.Elastic.DAL.Models
{
    public class FSO : IFSO
    {
        public static string indexname = StaticIndexPrefix.Prefix + "fso";
        private string _Id, _name, _parent;
        private string _contentPath, _crawlPath, _dfsPath;
        private string _commonPathComponent;

        /// <summary>
        /// Id is stored in lower-case and is the filename(to prevent duplicates so we have a 1-1 mapping with the fileystem) not auto-generated Id's are slower however.
        /// </summary>
        ///
        public string Id { get { return _Id; } set { _Id = value.ToLowerInvariant(); } }
        [Ignore]
        public string PathForCrawling => _crawlPath ?? GetCrawlPath();
        [Ignore]
        public string PublishedPath => _dfsPath ?? GetPublishedPath();
        [Ignore]
        public string PathForCrawlingContent => _contentPath ?? GetContentPath();


        public string Name { get { return _name; } set { _name = value.ToLowerInvariant(); } }
        /// <summary>
        /// Parent folder stored in lower-case. Supports easily querying contents of folder, while we do incremental crawl. example, find all the children and see if it matches what's on disk.
        /// </summary>
        //[Keyword(Normalizer = InitializationIndex.LOWERCASE, IgnoreAbove = 512)]overridden by fluentapi
        public string Parent { get { return _parent; } set { _parent = value.ToLowerInvariant(); } }
        /// <summary>
        /// Security ACL for this item
        /// </summary>
        public ACLs Acls { get; set; }
        [Keyword]
        public string Version { get; set; }//we will store the version, to support recrawls when processing engine changes? 
        public DateTime Last_write_timeUTC { get; set; }
        public DateTime Create_write_timeUTC { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Hidden { get; set; }
        public int FailureCount { get; set; }
        public string FailureReason { get; set; }
        public string Reason { get; set; }
        [Keyword]
        public string MachineName { get; set; }
        [Keyword(Normalizer = InitializationIndex.LOWERCASE, IgnoreAbove = 50)]
        public string Office { get; set; }
        public ProjectId Project { get; set; }
       //configured in initialization [Keyword(Normalizer = InitializationIndex.LOWERCASE, IgnoreAbove = 50)]
        public string Category { get; set; }
        [Keyword(Normalizer = InitializationIndex.LOWERCASE, IgnoreAbove = 50)]
        public string Department { get; set; }//empty if non-departmental
        [Ignore]
        public int RetryAttemtps { get; set; } = 0;
        [Ignore]//IndexName is a convenience property we use for manually setting/checking what index name the document should be inserted into.
        public string IndexName { get; set; }
        [Flattened]
        public object Metadata { get; set; }//for future use as a scratchpad.

        public FSO()
        {

        }
        /// <summary>
        /// pass any path (DFS or crawling path) and it will build a fso for dfs path.
        /// </summary>
        /// <param name="Path">lowercase</param>
        public FSO(string path)
        {
            SetPaths(path.ToLowerInvariant());
            if (File.Exists(PathForCrawling))
            {
                GetFsopropertiesAndExtract(new FileInfo(PathForCrawling));
            }
            else
            {
                GetFsopropertiesAndExtract(new DirectoryInfo(PathForCrawling));
            }
        }
        public FSO(IFSO source) : base()
        {
            CopyProperties(source, this);
            SetFileSystemInfoFromId();
        }
        public FSO(FileSystemInfo fsi) : this(fsi, default, default)
        {
        }
        public FSO(FileSystemInfo fsi, string office) : this(fsi, office, default)
        {
        }
        public FSO(FileSystemInfo fsi, string office, ProjectId project = default)
        {
            Office = office;
            Project = project;
            SetIDPropertiesFromFileSystemInfo(fsi);
        }

        private void SetIDPropertiesFromFileSystemInfo(FileSystemInfo fileSystemInfo)
        {
            SetPaths(fileSystemInfo.FullName.ToLowerInvariant());
            Id = PublishedPath;//here's if we set the ID 
            GetFsopropertiesAndExtract(fileSystemInfo);
        }


        public void SetFileSystemInfoFromId(FileSystemInfo fileSystemInfo = default)
        {
            SetPaths(Id);
            GetFsopropertiesAndExtract(fileSystemInfo);
        }


        /// <summary>
        /// Create a filesysteminfo temporarily and populate properties...maybe we could return the filesysteminfoobject if req'd
        /// </summary>
        /// 
        private void GetFsopropertiesAndExtract(FileSystemInfo fileSystemInfo = default)
        {
            if (fileSystemInfo == null)
            {
                if (!string.IsNullOrEmpty(PathForCrawling) && Directory.Exists(PathForCrawling))
                {
                    fileSystemInfo = new DirectoryInfo(PathForCrawling);
                }
                else
                {
                    fileSystemInfo = new FileInfo(PathForCrawling);
                }
            }
            Parent = Path.GetDirectoryName(PublishedPath) ?? PublishedPath;
            Last_write_timeUTC = fileSystemInfo.LastWriteTimeUtc;
            Create_write_timeUTC = fileSystemInfo.CreationTimeUtc;
            Name = fileSystemInfo.Name;

            if (string.IsNullOrEmpty(Office))
            {
                Extract();
            }
            //added missing N/A check so that as we traverse a folder and discover a projectnumber, 'N/A' will be replaced by the correct projectid. 
            //and we are crawling projects...TODO we could skip this when crawling DEPTS?
            else if (Project == null || Project.Number.Equals("N/A"))
            {
                ExtractProjectInfo();
            }
        }

        /// <summary>
        /// Pass any path and it will allow ID,PublishedPath/CrawlPath/ContentPath property to be read
        /// </summary>
        /// <param name="lowercasePath"></param>
        internal void SetPaths(string lowercasePath)
        {
            _crawlPath = null;
            _dfsPath = null;
            _contentPath = null;
            if (lowercasePath.StartsWith(PathHelper.PublishedRoot))//sometimes legacy long paths are passed to this method and currently PublishedRoot is not-legacylongpath...so never matches...
            {
                _commonPathComponent = lowercasePath.Substring(PathHelper.PublishedRoot.Length);
            }
            else if (lowercasePath.StartsWith(PathHelper.ContentRoot))
            {
                _commonPathComponent = lowercasePath.Substring(PathHelper.ContentRoot.Length);
            }
            else if (lowercasePath.StartsWith(PathHelper.CrawlRoot))
            {
                _commonPathComponent = lowercasePath.Substring(PathHelper.CrawlRoot.Length);
            }
            else
            {
                throw new ArgumentException($"{lowercasePath} didn't match {nameof(PathHelper)} structure.");
            }
        }

        private string GetContentPath()
        {
            if (_commonPathComponent == null) SetPaths(Id);
            _contentPath = string.Concat(PathHelper.ContentRoot, _commonPathComponent);
            return _contentPath;
        }

        private string GetCrawlPath()
        {
            if (_commonPathComponent == null) SetPaths(Id);
            _crawlPath = string.Concat(PathHelper.CrawlRoot, _commonPathComponent);
            return _crawlPath;
        }

        private string GetPublishedPath()
        {
            if (_commonPathComponent == null) SetPaths(Id);
            _dfsPath = string.Concat(PathHelper.PublishedRoot, _commonPathComponent);
            return _dfsPath;
        }


        private void Extract()
        {
            var match = PathHelper.OfficeExtractRgx.Match(PublishedPath);
            if (match.Success)
            {
                Office = match.Groups[1].Value;
            }
            ExtractProjectInfo();
        }
        private void ExtractProjectInfo()
        {
            var match = PathHelper.ProjectExtractRgx.Match(PublishedPath);
            if (match.Success)
            {
                if (match.Groups[2].Success)
                {
                    Project = new ProjectId(
                       match.Groups[2].Value?.Replace('-', '.'),
                       "+".Equals(match.Groups[3].Value),
                       match.Groups[4].Value?.ToLowerInvariant()
                   );
                }
                else if (match.Groups[5].Success)//non standard projectname
                {
                    Project = new ProjectId(
                      "N/A",
                      false,
                      (match.Groups[5].Value?.Replace('\\', ',') + " " + match.Groups[6].Value?.ToString()).Trim()
                      );
                }
            }
            else
            {
                Project = new ProjectId("N/A", false, "N/A");//ensure there's a default projectID...
            }
        }

        public static void CopyProperties(IFSO source, IFSO target)
        {
            //TODO: look at this pattern:  https://www.automatetheplanet.com/optimize-csharp-reflection-using-delegates/
            var type = source.GetType();
            var thistype = target.GetType();// typeof(FSOdocumentv2);
            foreach (var property in type.GetProperties().Where(x => x.CanWrite == true))
            {
                var propertyInfo = type.GetProperty(property.Name);
                propertyInfo.SetValue(target, property.GetValue(source), null);
            }
        }
    }
}
