using HOK.Elastic.DAL.Models;
using HOK.Elastic.FileSystemCrawler.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HOK.Elastic.FileSystemCrawler
{
    public static partial class InputPathLoader
    {
        public const string UNFINISHEDPATHS = "unfinished.txt";

        /// <summary>
        /// easy way to write out where in the process the job is, should we need to terminate and resume the job.
        /// </summary>
        /// /// <param name="filepath">folder to write file in</param>
        /// <param name="stack"></param>
        /// <param name="directory">The directory currently being processed and not present in the stack</param>
        public static void WriteOutUnfinishedPaths(string filepath, ConcurrentStack<FSOdirectory> stack, FSOdirectory directory)
        {
            var entries = stack.Where(x => x != null).Select(x => x.Office + "\t" + x.PathForCrawling).ToList();
            if (directory != null)
            {
                entries.Add(directory.Office + "\t" + directory.PathForCrawling);
            }
            File.WriteAllLines(Path.Combine(filepath, UNFINISHEDPATHS), entries);
        }

        public static void ClearUnfinishedPaths(string filepath)
        {
            File.Delete(Path.Combine(filepath, UNFINISHEDPATHS));
        }

        public static bool HasUnfinishedPaths(string filepath)
        {
            return System.IO.File.Exists(Path.Combine(filepath, UNFINISHEDPATHS));
        }

        /// <summary>
        /// any unfinished paths are added to jobSettings's path collection by office and removing existing paths that are the parent (as we have already crawled that location)
        /// </summary>
        /// <param name="filepath"></param>
        /// <param name="jobSettings"></param>
        public static void LoadUnfinishedPaths(string filepath,  ref InputPathCollectionCrawl<InputPathBase> paths)
        {
            if (HasUnfinishedPaths(filepath) && paths != null)
            {
                var lines = System.IO.File.ReadAllLines(Path.Combine(filepath, UNFINISHEDPATHS));
                foreach (var line in lines)
                {
                    var pathsAndOffices = line.Split('\t');
                    if (pathsAndOffices.Length == 2)
                    {
                        //do not check if path exists! just add the paths and let them fail.
                        if (!string.IsNullOrEmpty(pathsAndOffices[1]))
                        {
                            //if there were any unfinished paths from previous run, clear out any of the unstarted,default paths that we have already traversed...
                            //for example if the default paths contains two entries:
                            //'\\contoso\projects\'
                            //'\\contoso\departments\'
                            //If we loaded an unfinished path of '\\contoso\projects\2000\2000.12345', we should remove the entry for '\\contoso\projects' from already loaded default paths as we have already crawled that root folder.
                            paths.RemoveWhere(x => (pathsAndOffices[1] + '\\').StartsWith(x.Path + '\\',StringComparison.OrdinalIgnoreCase));
                            paths.Add(new InputPathBase(pathsAndOffices[1], pathsAndOffices[0], PathStatus.Resume));
                        }
                    }
                }
            }
        }
    }
}
