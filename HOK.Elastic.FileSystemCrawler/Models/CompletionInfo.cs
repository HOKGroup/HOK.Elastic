using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    public class CompletionInfo : CrawlMetrics, ISettingsJobPathArgs
    {
        public CompletionInfo():this("default")
        {
            
        }
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
                EndTime = StartTime;
                JobNotes = args.JobNotes;
                InputPaths = args.InputPaths;
            }
        }
        public string JobName { get; set; }
        public string JobNotes { get; set; }
        public bool ReadFileContents { get; set; }
        public CrawlMode CrawlMode { get; set; }
        public InputPathCollectionBase InputPaths { get; set; }
        public int? InputPathCount { get { return InputPaths?.Count; } }
        public decimal CPUCoreThreadMultiplier { get; set; }
        public string AppVersion { get { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(); } }
        public ExitCode exitCode { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; } 

        private TimeSpan TimespanDuration
        {
            get
            {
                if (StartTime == DateTime.MinValue || EndTime == DateTime.MinValue) return TimeSpan.Zero;
                return EndTime - StartTime;
            }
        }
        public string Duration
        {
            get
            {
                return string.Format(System.Globalization.DateTimeFormatInfo.InvariantInfo, "{0:dd} days {0:hh} hours {0:mm} minutes {0:ss} seconds total duration", TimespanDuration);
            }
        }
        public double DurationSeconds
        {
            get
            {
                return TimespanDuration.TotalSeconds;
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
