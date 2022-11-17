using HOK.Elastic.DAL;
using HOK.Elastic.DAL.Models;
using HOK.Elastic.FileSystemCrawler.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
//using RtfPipe;

namespace HOK.Elastic.FileSystemCrawler
{
    public abstract partial class WorkerBase : IWorkerBase
    {
        internal long _dircount = 0;
        internal long _filesmatched = 0;
        internal long _filesskipped = 0;
        internal long _filesnotfound = 0;
        internal long _deleted = 0;
        internal int FileSystemRetryAttempts = 3;
        internal bool ildebug, ilinfo, ilwarn, ilerror, ilfatal;
        internal ISettingsJobArgs _args;
        internal HOK.Elastic.Logger.Log4NetLogger _il;
        internal DAL.IIndex _indexEndPoint;
        internal DAL.IDiscovery _discoveryEndPoint;
        internal DocumentHelper _documentHelper;
        internal SecurityHelper _securityHelper;
        internal CancellationToken _ct;
        /// <summary>
        /// This block is linked to document insertTransform which will take a FSO or FSOfile and convert to FSOemail or FSOdocument if required.
        /// </summary>
        internal TransformBlock<IFSO, IFSO> docInsertTranformBlock;
        internal TransformBlock<IFSO, IFSO> docReindexTransformBlock;
        /// <summary>
        /// This block 
        /// </summary>
        internal TransformBlock<IFSO, IFSO> docUpdateExistingTransformBlock;
        internal BatchBlock<IFSO> docInsertBatch;
        internal ActionBlock<IFSO> docInsert;
        internal ActionBlock<IFSO> docInsertReindex;
        internal ActionBlock<IFSO[]> docInsertArray;
        internal ActionBlock<IFSO> docUpdate;
        public SecurityHelper SecurityHelper => _securityHelper;
        public DocumentHelper DocumentHelper => _documentHelper;

        public WorkerBase(IIndex elasticIngest, IDiscovery elasticDiscovery, DocumentHelper Dh, SecurityHelper Sh, HOK.Elastic.Logger.Log4NetLogger logger)
        {
            _il = logger;
            _documentHelper = Dh;
            _securityHelper = Sh;
            _indexEndPoint = elasticIngest;
            _discoveryEndPoint = elasticDiscovery;
            ildebug = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug);
            ilinfo = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information);
            ilwarn = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Warning);
            ilerror = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error);
            ilfatal = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Critical);
        }

        public abstract CompletionInfo Run(ISettingsJobArgs args, CancellationToken ct = default);
        public abstract Task<CompletionInfo> RunAsync(ISettingsJobArgs args, CancellationToken ct = default);

        internal List<FSOdirectory> GetDirectoriesFromInputPaths(List<InputPathBase> setofPaths, ISettingsJobArgs args)///TODO why not just use args.inputpaths?
        {
             var inputs = new List<FSOdirectory>();
            foreach (var item in setofPaths)
            {
                var di = new DirectoryInfo(item.Path);
                var fsodir = new FSOdirectory(di, item.Office);
                if (fsodir != null)
                {
                    inputs.Add(fsodir);
                }
                else
                {
                    if (ilerror) _il.LogErr($"Error {nameof(FSOdirectory)} was null", item.Path, null, null);
                }
            }
            return inputs;
        }

        internal List<FSOdirectory> GetDirectoriesFromInputPaths(ISettingsJobArgs args,string office=null)
        {
            var inputs = new List<FSOdirectory>();
            var inputPaths = string.IsNullOrEmpty(office)?args.InputPaths:args.InputPaths.Where(x => x.Office == office);
            foreach (var item in inputPaths)
            {
                var di = new DirectoryInfo(item.Path);
                var fsodir = new FSOdirectory(di, item.Office);
                if (fsodir != null)
                {
                    inputs.Add(fsodir);
                }
                else
                {
                    if (ilerror) _il.LogErr($"Error {nameof(FSOdirectory)} was null", item.Path, null, null);
                }
            }
            return inputs;
        }

        private void PopulateExclusionRegex(ISettingsJobArgs args,List<string> exclusions)///I don't think we need exclusions variable.
        {
          
            if (exclusions != null && exclusions.Any())
            {
                HOK.Elastic.DAL.Models.PathHelper.IgnoreExtensions = new HashSet<string>(exclusions.Distinct());
            }
            if (args.IgnoreExtensions != null && args.IgnoreExtensions.Any())
            {
                HOK.Elastic.DAL.Models.PathHelper.IgnoreExtensions = new HashSet<string>(args.IgnoreExtensions.Distinct());
            }
            if (exclusions != null && exclusions.Any())//TODO add notes here as I can't recall why we do either of these two cases...I think it's when we resume from a previous incomplete job, we exlcude the parent directory?????
            {
                exclusions.Distinct().Where(x => !HOK.Elastic.DAL.Models.PathHelper.IgnoreExtensions.Contains(x)).Select(x => HOK.Elastic.DAL.Models.PathHelper.IgnoreExtensions.Add(x));
            }
            if (!string.IsNullOrEmpty(args.PathInclusionRegex))
            {
                HOK.Elastic.DAL.Models.PathHelper.SetPathInclusion(args.PathInclusionRegex);
            }
            if (!string.IsNullOrEmpty(args.FileNameExclusionRegex))
            {
                HOK.Elastic.DAL.Models.PathHelper.SetFileNameExclusion(args.FileNameExclusionRegex);
            }
        }

        /// <summary>
        /// Basic test to make sure the file server is online. Perhaps We could cache the answer for a short period if needed.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public bool IsCrawlServerOnline(string path)
        {
            if (Directory.Exists(PathHelper.ContentRoot))
            {
                return true;
            }
            else
            {
                if (ilwarn) _il.LogWarn("Offline file server; Won't delete", PathHelper.ContentRoot, path);
                return false;
            }
        }
    }
}
