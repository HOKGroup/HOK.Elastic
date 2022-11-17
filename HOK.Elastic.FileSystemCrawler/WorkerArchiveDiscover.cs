using HOK.Elastic.DAL;
using HOK.Elastic.DAL.Models;
using HOK.Elastic.FileSystemCrawler.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace HOK.Elastic.FileSystemCrawler
{
    public class WorkerArchiveDiscover : WorkerBase
    {
        private double _desiredMaxMemoryKB = 2000000;//default to 2gb

        /// <summary>
        /// we might want to remove this constructor to ensure we always populate path substitutions from here.
        /// </summary>
        /// <param name="logger"></param>
        public WorkerArchiveDiscover(IIndex elasticIngest, IDiscovery elasticDiscovery, SecurityHelper sh, DocumentHelper dh, HOK.Elastic.Logger.Log4NetLogger logger) : base(elasticIngest, elasticDiscovery, dh, sh, logger)
        {
            //calculate desired free memory for future reference to avoid consuming too much memory
            ulong installedMemory;
            NativeMethods.MEMORYSTATUSEX memStatus = new NativeMethods.MEMORYSTATUSEX();
            if (NativeMethods.GlobalMemoryStatusEx(memStatus))
            {
                installedMemory = memStatus.ullTotalPhys;
                _desiredMaxMemoryKB = Math.Round((installedMemory / 1024) * 0.2);
            }
            if (ilinfo) _il.LogInfo("Max Ram limit before inserting documents", null, _desiredMaxMemoryKB);
        }
        /// <summary>
        ///Calls CrawlAsync and waits.
        /// </summary>
        /// <returns></returns>

        public override CompletionInfo Run(ISettingsJobArgs args, CancellationToken ct = default)
        {
            var awaiter = RunAsync(args, ct).GetAwaiter();
            return awaiter.GetResult();
        }
        /// <summary>
        /// Missing Content and ReCrawl by Query       
        /// </summary>
        /// <param name="args">Job Arguments</param>
        /// <param name="ct">Cancellation Token</param>
        /// <returns></returns>
        public override async Task<CompletionInfo> RunAsync(ISettingsJobArgs args, CancellationToken ct)
        {
            _ct = ct;
            _args = args;
            int boundedCapacity;
            int crawlthreads;
            int insertBoundedCapacity;
            if (args.ReadFileContents??false)
            {
                crawlthreads = args.CrawlThreads;
                boundedCapacity = args.CrawlThreads;
                insertBoundedCapacity = Math.Max(2, args.CrawlThreads / 2);
                docInsertBatch = new BatchBlock<IFSO>(crawlthreads * 4);//TODO make a TPL block limited on attachmentsize or lengthKB, bytes etc.
            }
            else
            {
                crawlthreads = args.CrawlThreads;
                boundedCapacity = args.CrawlThreads * 4;
                insertBoundedCapacity = args.CrawlThreads;
                docInsertBatch = new BatchBlock<IFSO>(args.BulkUploadSize??100);
            }
            CompletionInfo completionInfo = new CompletionInfo(args);
            var linkOptions = new DataflowLinkOptions() { PropagateCompletion = true };
            docReindexTransformBlock = new TransformBlock<IFSO, IFSO>(item => DocumentHelper.ReindexTransform(item), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = crawlthreads, BoundedCapacity = boundedCapacity });
            docInsertTranformBlock = new TransformBlock<IFSO, IFSO>(item => DocumentHelper.InsertTransform(item), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = crawlthreads, BoundedCapacity = boundedCapacity });
            docInsert = new ActionBlock<IFSO>(item => DocumentHelper.Insert(item), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1, BoundedCapacity = insertBoundedCapacity });
            docInsertArray = new ActionBlock<IFSO[]>(item => DocumentHelper.Insert(item), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1, BoundedCapacity = insertBoundedCapacity });
            docInsertBatch.LinkTo(docInsertArray, linkOptions);
            docUpdate = new ActionBlock<IFSO>(item => DocumentHelper.Update(item));
            docReindexTransformBlock.LinkTo(docUpdate, linkOptions);
            docInsertTranformBlock.LinkTo(docInsertBatch, linkOptions, item => DocumentHelper.IsBatchable(item));
            docInsertTranformBlock.LinkTo(docInsert, linkOptions, item => !DocumentHelper.IsBatchable(item));
            docInsertTranformBlock.LinkTo(DataflowBlock.NullTarget<IFSO>(), linkOptions);

            if (args.CrawlMode == CrawlMode.FindMissingContent || args.CrawlMode == CrawlMode.EmailOnlyMissingContent)
            {                
                try
                {
                    //var inputs = new List<FSOdirectory>();
                    //#region ProcessInputPaths
                    //inputs.AddRange(GetDirectoriesFromInputPaths(args));
                    //#endregion
                    //#region DoWork
                    //await ReCrawlMissingAttachmentContent(inputs).ConfigureAwait(false);
                    //docInsertTranformBlock.Complete();
                    //docReindexTransformBlock.Complete();
                    //await Task.WhenAll(docInsert.Completion, docUpdate.Completion, docInsertArray.Completion).ConfigureAwait(false);
                    //completionInfo.exitCode = CompletionInfo.ExitCode.OK;
                    //#endregion
                }
                catch (AggregateException ae)
                {
                    if (ilerror) _il.LogErr("CrawlingAggregateErrors", "", null, ae);//TODO verify log4net enumerates all the inner exceptions when it converts the exception object.
                    foreach (var ex in ae.Flatten().InnerExceptions)
                    {
                        if (ilerror) _il.LogErr("aggregate exception:", "", null, ex);
                    }
                    completionInfo.exitCode = CompletionInfo.ExitCode.Fatal;
                }
                catch (OperationCanceledException ex)
                {
                    completionInfo.exitCode = CompletionInfo.ExitCode.Cancel;
                    if (ilfatal) _il.LogFatal("Cancelling", "", null, ex);
                }
                catch (Exception ex)
                {
                    if (ilfatal) _il.LogFatal("Fatal Exception...quiting now", "", null, ex);
                    completionInfo.exitCode = CompletionInfo.ExitCode.Fatal;
                }
            }
            else
            {
                throw new ArgumentException($"{args.CrawlMode} is not supported in {nameof(WorkerByQuery)}");
            }
            completionInfo.EndTime = DateTime.Now;
            completionInfo.DirCount = _dircount;
            completionInfo.FileCount = _filesmatched;
            completionInfo.FileSkipped = _filesskipped;
            completionInfo.FileNotFound = _filesnotfound;
            completionInfo.Deleted = _deleted;
            return completionInfo;
        }


  

        //later, we can also look at making a custom dataflowblock based on filesize.
        private async Task WaitForMemory(double desiredFreeMemoryKB)
        {
            bool GCCollectCalled = false;
            int count = 0;
            ulong availableSizeKB;
            NativeMethods.MEMORYSTATUSEX memStatus;
            do
            {
                memStatus = new NativeMethods.MEMORYSTATUSEX();
                if (NativeMethods.GlobalMemoryStatusEx(memStatus))
                {
                    availableSizeKB = memStatus.ullAvailPhys / 1024;
                }
                else
                {
                    availableSizeKB = ulong.MaxValue;//if we can't get the available memory just let it continue..
                }
                if (availableSizeKB < desiredFreeMemoryKB)
                {
                    count++;
                    if (count > 5)
                    {
                        count = 0;
                        docInsertBatch.TriggerBatch();
                        this._indexEndPoint = new DAL.Index(_args.ElasticIndexURI, new Elastic.Logger.Log4NetLogger("Index"));
                        this._discoveryEndPoint = new DAL.Discovery(_args.ElasticDiscoveryURI, new Elastic.Logger.Log4NetLogger("Discovery"));
                        if (ilwarn) _il.LogWarn($"Almost out of memory...cleared elastic");
                    }

                    if (ilwarn) _il.LogWarn($"Almost out of memory...will wait. Insert={docInsert.InputCount},Array={docInsertArray.InputCount},InsertTransform={docInsertTranformBlock.InputCount}", "", availableSizeKB);
                    if (ilwarn) _il.LogWarn($"Almost out of memory...will wait. Desired={desiredFreeMemoryKB}KB Available={availableSizeKB}", "", availableSizeKB);
                    if (!GCCollectCalled)
                    {
                        GCCollectCalled = true;
                        GC.Collect();
                    }
                    //we could also trigger batch blocks to clear their queues.                    
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            } while (availableSizeKB < desiredFreeMemoryKB);
        }
    }
}
