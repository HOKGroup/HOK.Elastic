using Nest;
using System;

namespace HOK.Elastic.DAL.Models
{
    //No point in decorating these interfaces (I think) as they can't be used to set up indicies(automap properties)
    public interface IFSO:IFSOPathBase
    {
        /// <summary>
        /// published path to the file always stored as lowercase
        /// </summary>
        string Id { get; set; }
        //[Ignore]

        //[Keyword(Normalizer = InitializationIndex.LOWERCASE, IgnoreAbove = 256)]
        string Name { get; set; }
        string Parent { get; set; }
        ACLs Acls { get; set; }
        DateTime Last_write_timeUTC { get; set; }
        DateTime Create_write_timeUTC { get; set; }
        DateTime Timestamp { get; set; }
        string Reason { get; set; }
        //[Keyword]
        string MachineName { get; set; }
        string Version { get; set; }
        int FailureCount { get; set; }
        string FailureReason { get; set; }
        bool Hidden { get; set; }
        //[Keyword(Normalizer = InitializationIndex.LOWERCASE, IgnoreAbove = 50)]
        string Office { get; set; }
        //[Text(Analyzer = InitializationIndex.NONWHITESPACEEDGE, SearchAnalyzer = InitializationIndex.NONWHITESPACEEDGESEARCH)]
        //string ProjectNumber { get; set; }
        //[Text(Analyzer = InitializationIndex.NONWHITESPACEEDGE, SearchAnalyzer = InitializationIndex.NONWHITESPACEEDGESEARCH)]
        ProjectId Project { get; set; }
        // string ProjectName { get; set; }
        //[Keyword(Normalizer = InitializationIndex.LOWERCASE, IgnoreAbove = 50)]
        string Category { get; set; }
        //[Ignore]
        string IndexName { get; set; }
        //[Ignore]
        int RetryAttemtps { get; set; }

        /// <summary>
        /// Will attempt to construct a filesysteminfo object based on the object's ID;if it the path doesn't exist it will throw an exception.
        /// </summary>
        /// <param name="fileSystemInfo"></param>
        /// <exception cref="System.IO.FileNotFoundException"/>
        void SetFileSystemInfoFromId(System.IO.FileSystemInfo fileSystemInfo = default);
    }
}