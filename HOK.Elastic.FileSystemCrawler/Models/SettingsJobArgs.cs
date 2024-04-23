using System;
using System.IO;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    /// <summary>
    /// This gets passed as an argument for a job to run.
    /// </summary>
    public class SettingsJobArgs : SettingsJob, ISettingsJobArgs
    {
        public int CrawlThreads { get { return Math.Max(1, (int)Math.Round(Environment.ProcessorCount * CPUCoreThreadMultiplier ?? 1, 0)); } }
        public int DocReadingThreads { get { return Math.Max(1, (int)Math.Round(Environment.ProcessorCount * CPUCoreThreadMultiplier ?? 1, 0)); } }
        public int DocInsertionThreads { get; set; } = 2;
        public bool RunningInteractively { get; set; } = true;
        public string JobNotes { get; set; } = "None Provided";
        public virtual string JobName { get; set; } = "Default";
        public string InputPathLocation { get; set; }
        public string JsonQueryString { get; set; }
    }
}