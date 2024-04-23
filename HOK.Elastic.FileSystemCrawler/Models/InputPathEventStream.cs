using HOK.Elastic.DAL.Models;
using Newtonsoft.Json;
using System;
using System.Text;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    public class InputPathEventStream : InputPathBase, IInputPathBase
    {
        private string _pathFrom;
        /// <summary>
        /// Always store path as lowercase
        /// </summary>
        public string PathFrom { get => LongPaths.GetLegacyLongPath(_pathFrom); set => _pathFrom = value?.ToLowerInvariant(); }//could be file or directory or empty...also note case changes will trigger a nasuni rename event but we should ignore.

        public ActionContent ContentAction { get; set; }
        public ActionPresence PresenceAction { get; set; }
        public DateTime TimeStampUtc { get; set; }
        public bool IsDir { get; set; }//maps to Nasuni audit stream.

        public InputPathEventStream() : base()
        {
        }
        public override string ToString()
        {
           return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }
    public enum ActionContent
    {
        None = 0,
        ACLSet = 1,
        Write = 2
    }
    public enum ActionPresence
    {
        None = 0,
        Delete = 1,
        Move = 2,
        Copy = 3
    }
   
}
