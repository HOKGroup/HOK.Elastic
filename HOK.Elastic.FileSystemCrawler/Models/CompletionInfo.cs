using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    public class CompletionInfo : CrawlMetrics, ISettingsJobPathArgs
    {
        public CompletionInfo(string jobName)
        {
            StartTime = DateTime.Now;
            JobName = jobName;
        }
        public CompletionInfo(ISettingsJobArgs args)
        {
            if (args != null)
            {
                JobName = args.JobName;
                ReadFileContents = args.ReadFileContents??false;
                CrawlMode = args.CrawlMode;
                CPUCoreThreadMultiplier = args.CPUCoreThreadMultiplier??1;
                StartTime = DateTime.Now;
                JobNotes = args.JobNotes;
                InputPaths = args.InputPaths;
            }
        }
        public string JobName { get; private set; }
        public string JobNotes { get; private set; }
        public bool ReadFileContents { get; set; }
        public CrawlMode CrawlMode { get; set; }
        public InputPathCollectionBase InputPaths { get; set; }
        public int? InputPathCount { get { return InputPaths?.Count; } }
        public decimal CPUCoreThreadMultiplier { get; set; }
        public string AppVersion { get { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(); } }
        public ExitCode exitCode { get; set; }
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; internal set; }
        public string Duration
        {
            get
            {
                TimeSpan endtime = DateTime.Now - StartTime;
                return string.Format(System.Globalization.DateTimeFormatInfo.InvariantInfo, "{0:dd} days {0:hh} hours {0:mm} minutes {0:ss} seconds total duration", endtime);
            }
        }
        public double DurationSeconds
        {
            get
            {
                TimeSpan endtime = DateTime.Now - StartTime;
                return endtime.TotalSeconds;
            }
        }
       
        public enum ExitCode
        {
            None = 0,
            OK = 1,
            Cancel = 2,
            Fatal = 3
        }
    }
}
