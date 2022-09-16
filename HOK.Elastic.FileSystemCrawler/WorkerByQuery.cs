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
    public class WorkerByQuery : WorkerBase
    {
        private double _desiredMaxMemoryKB = 2000000;//default to 2gb

        /// <summary>
        /// we might want to remove this constructor to ensure we always populate path substitutions from here.
        /// </summary>
        /// <param name="logger"></param>
        public WorkerByQuery(IIndex elasticIngest, IDiscovery elasticDiscovery, SecurityHelper sh, DocumentHelper dh, HOK.Elastic.Logger.Log4NetLogger logger) : base(elasticIngest, elasticDiscovery, dh, sh, logger)
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
                    var inputs = new List<FSOdirectory>();
                    #region ProcessInputPaths
                    inputs.AddRange(GetDirectoriesFromInputPaths(args));
                    #endregion
                    #region DoWork
                    await ReCrawlMissingAttachmentContent(inputs).ConfigureAwait(false);
                    docInsertTranformBlock.Complete();
                    docReindexTransformBlock.Complete();
                    await Task.WhenAll(docInsert.Completion, docUpdate.Completion, docInsertArray.Completion).ConfigureAwait(false);
                    completionInfo.exitCode = CompletionInfo.ExitCode.OK;
                    #endregion
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
            else if (args.CrawlMode == CrawlMode.QueryBasedReIndex) // jsonqueryreindex
            {
                try
                {
                    #region processInputJson// Test to see if input is valid Elastic query
                    var validQuery = _discoveryEndPoint.ValidateJsonStringQuery(args.JsonQueryString);
                    if (!validQuery)
                    {
                        throw new JsonException("Elastic Client could not validate input JSON query");
                    }
                    #endregion
                    #region DoWork
                    await ReCrawlByQuery(args.JsonQueryString).ConfigureAwait(false);
                    docInsertTranformBlock.Complete();
                    docReindexTransformBlock.Complete();
                    await Task.WhenAll(docInsert.Completion, docUpdate.Completion, docInsertArray.Completion).ConfigureAwait(false);
                    completionInfo.exitCode = CompletionInfo.ExitCode.OK;
                    #endregion
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
                catch (JsonException fe)
                {
                    if (ilfatal) _il.LogFatal("missingcontentquery.json is not valid JSON", "", null, fe);
                    completionInfo.exitCode = CompletionInfo.ExitCode.Fatal;
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
        /// <summary>
        /// Find documents with missing content and attempts to read content and reinsert into elastic
        /// </summary>
        /// <param name="inputItems">Paths to search elastic for missing content</param>
        /// <returns></returns>
        private async Task ReCrawlMissingAttachmentContent(List<FSOdirectory> inputItems)
        {
            //var startTime = DateTime.Now;
            int count;
            int howmanytimeswehadzeroresults = 0;
            int failureCountFilter;
            DateTime? dateTimeCutoffForFirstPass = DateTime.Now.AddYears(-1);
            try
            {
                while (howmanytimeswehadzeroresults < 3)
                {
                    _ct.ThrowIfCancellationRequested();
                    count = 0;
                    foreach (var inputPath in inputItems)
                    {
                        try
                        {
                            failureCountFilter = (Math.Min(2, howmanytimeswehadzeroresults));
                            var publishedPath = inputPath.PublishedPath;
                            //Get Missing Content Emails
                            count += await GetMissingContentDocsAndPostToTransformBlock<FSOemail>(publishedPath, failureCountFilter, dateTimeCutoffForFirstPass).ConfigureAwait(false);
                            //Get Missing Content Documents if applicable
                            if (_args.CrawlMode != CrawlMode.EmailOnlyMissingContent)
                            {
                                count += await GetMissingContentDocsAndPostToTransformBlock<FSOdocument>(publishedPath, failureCountFilter, dateTimeCutoffForFirstPass).ConfigureAwait(false);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            if (ilerror) _il.LogErr("Had some kind of getcontent error while getting content within", inputPath.PathForCrawling, null, ex);
                        }
                    }
                    Interlocked.Add(ref _filesmatched, count);
                    if (ilinfo) _il.LogInfo("MissingContentStatusUpdate:", "all paths", new CrawlMetrics()
                    {
                        FileCount = Interlocked.Read(ref _filesmatched),
                        FileSkipped = Interlocked.Read(ref _filesskipped),
                        FileNotFound = Interlocked.Read(ref _filesnotfound),
                        Deleted = Interlocked.Read(ref _deleted)
                    });
                    //wait 
                    while (docInsert.InputCount + docInsertTranformBlock.InputCount > 0)
                    {
                        if (ilinfo) _il.LogInfo($"Pipeline still processing...", "", null);
                        await Task.Delay(TimeSpan.FromSeconds(60)).ConfigureAwait(false);//always wait a minute so in the case of small data-sets elastic has a chance to realize the document has been updated before looping through it again...
                    }
                    if (count < 30)//if we are into the small numbers we can move onto the next item
                    {
                        if (ilinfo) _il.LogInfo($"Few files found with failurecount={howmanytimeswehadzeroresults} and will now pause briefly.", "allpaths", count);
                        if (dateTimeCutoffForFirstPass == null)
                        {
                            howmanytimeswehadzeroresults++;
                        }
                        dateTimeCutoffForFirstPass = null;//set to remove date filter on next query.
                        await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);//Only wait a short time (or maybe not at all) because our next iteration will look for documents with a greater retry limit.
                    }
                }
            }
            finally
            {
                docInsertTranformBlock.Complete();
                docReindexTransformBlock.Complete();
            }
        }

        /// <summary>
        /// Find documents with missing content based on a JSON query and attempts to read content and reinsert into elastic
        /// </summary>
        /// <param name="jsonQueryString">An Elatic query in JSON format</param>
        /// <returns></returns>
        private async Task ReCrawlByQuery(string jsonQueryString)
        {
            //var startTime = DateTime.Now;
            int countDocsInserted;
            int howmanytimeswehadzeroresults = 0;
            int failureCountFilter;
            DateTime? dateTimeCutoffForFirstPass = DateTime.Now.AddYears(-1);
            try
            {
                while (howmanytimeswehadzeroresults < 3)
                {
                    countDocsInserted = 0;
                    try
                    {
                        failureCountFilter = (Math.Min(2, howmanytimeswehadzeroresults));
                        countDocsInserted += await GetFromQueryAndPostToTransformBlock<FSOemail>(jsonQueryString, failureCountFilter, dateTimeCutoffForFirstPass).ConfigureAwait(false);
                        countDocsInserted += await GetFromQueryAndPostToTransformBlock<FSOdocument>(jsonQueryString, failureCountFilter, dateTimeCutoffForFirstPass).ConfigureAwait(false);
                        countDocsInserted += await GetFromQueryAndPostToTransformBlock<FSOdirectory>(jsonQueryString, failureCountFilter, dateTimeCutoffForFirstPass).ConfigureAwait(false);
                        countDocsInserted += await GetFromQueryAndPostToTransformBlock<FSOfile>(jsonQueryString, failureCountFilter, dateTimeCutoffForFirstPass).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (ilerror) _il.LogErr("Had some kind of getcontent error while getting content within", jsonQueryString, null, ex);
                    }

                    Interlocked.Add(ref _filesmatched, countDocsInserted);
                    if (ilinfo) _il.LogInfo("MissingContentStatusUpdate:", "all paths", new CrawlMetrics()
                    {
                        FileCount = Interlocked.Read(ref _filesmatched),
                        FileSkipped = Interlocked.Read(ref _filesskipped),
                        FileNotFound = Interlocked.Read(ref _filesnotfound),
                        Deleted = Interlocked.Read(ref _deleted)
                    });
                    //wait 
                    while (docInsert.InputCount + docInsertTranformBlock.InputCount > 0)
                    {
                        if (ilinfo) _il.LogInfo($"Pipeline still processing...", "", null);
                        await Task.Delay(TimeSpan.FromSeconds(60)).ConfigureAwait(false);//always wait a minute so in the case of small data-sets elastic has a chance to realize the document has been updated before looping through it again...
                    }
                    if (countDocsInserted < 30)//if we are into the small numbers we can move onto the next item
                    {
                        if (ilinfo) _il.LogInfo($"Few files found with failurecount={howmanytimeswehadzeroresults} and will now pause briefly.", "allpaths", countDocsInserted);
                        if (dateTimeCutoffForFirstPass == null)
                        {
                            howmanytimeswehadzeroresults++;
                        }
                        dateTimeCutoffForFirstPass = null;//set to remove date filter on next query.
                        await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);//Only wait a short time (or maybe not at all) because our next iteration will look for documents with a greater retry limit.
                    }
                }
            }
            finally
            {
                docInsertTranformBlock.Complete();
                docReindexTransformBlock.Complete();
            }
        }

        private async Task<int> GetMissingContentDocsAndPostToTransformBlock<T>(string publishedPath, int retries, DateTime? dateTime = null) where T : class, IFSOdocument
        {
            int count = 0;
            var items = _discoveryEndPoint.GetIFSOdocumentsLackingContentV2<T>(publishedPath, retries, dateTime);
            FileInfo fileInfo;
            foreach (var item in items)
            {
                _ct.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(item.Id))
                    {
                        count++;
                        fileInfo = new FileInfo(item.PathForCrawlingContent);// item.FileSystemInfo as FileInfo;
                        item.SetFileSystemInfoFromId(fileInfo);
                        item.Acls = SecurityHelper.GetDocACLs(fileInfo);
                        item.Owner = SecurityHelper.GetOwner(fileInfo);
                        if (item.Reason == "Missing Content")
                        {
                            item.FailureCount++;
                        }
                        item.Reason = "Missing Content";                    
                        await WaitForMemory(_desiredMaxMemoryKB).ConfigureAwait(false);
                        await docInsertTranformBlock.SendAsync(item).ConfigureAwait(false);
                    }
                    else
                    {
                        DeleteElasticDocumentWhenNotOnDiskSource(item);
                    }
                }
                catch (Exception ex)
                {
                    if (ex is DirectoryNotFoundException || ex is PathTooLongException || ex is UnauthorizedAccessException)
                    {
                        if (ilwarn) _il.LogWarn(ex.Message, item.Id, null);
                    }
                    else
                    {
                        if (ilerror) _il.LogErr(nameof(GetMissingContentDocsAndPostToTransformBlock), item.Id, null, ex);//errors here when filer goes offline
                    }
                }
            }
            return count;
        }

        private async Task<int> GetFromQueryAndPostToTransformBlock<T>(string jsonQueryString, int retries, DateTime? dateTime = null) where T : class, IFSO
        {
            int count = 0;
            var items = _discoveryEndPoint.GetIFSOsByQuery<T>(jsonQueryString, retries, dateTime);
            FileSystemInfo fileSystemInfo;
            foreach (var item in items)
            {
                _ct.ThrowIfCancellationRequested();
                try
                {
                    if (item is FSOdirectory ? Directory.Exists(item.Id) : File.Exists(item.Id))
                    {
                        count++;
                        //build fileSystemInfo object
                        if (item is FSOdirectory)
                        {
                            fileSystemInfo = new DirectoryInfo(item.PathForCrawlingContent);
                        }
                        else
                        {
                            fileSystemInfo = new FileInfo(item.PathForCrawlingContent);// item.FileSystemInfo as FileInfo;
                            if (item is FSOdocument)
                            {
                                (item as FSOdocument).Owner = SecurityHelper.GetOwner(fileSystemInfo as FileInfo);
                            }
                            else if (item is FSOemail)
                            {
                                (item as FSOemail).Owner = SecurityHelper.GetOwner(fileSystemInfo as FileInfo);
                            }
                            else if (item is FSOfile)
                            {
                                (item as FSOfile).Owner = SecurityHelper.GetOwner(fileSystemInfo as FileInfo);
                            }
                        }
                        //use fileSystemInfo object to populate the elastic document
                        item.SetFileSystemInfoFromId(fileSystemInfo);
                        item.Acls = SecurityHelper.GetDocACLs(fileSystemInfo);
                        item.Reason = "Reindex by Query";
                        await WaitForMemory(_desiredMaxMemoryKB).ConfigureAwait(false);
                        if (_args.ReadFileContents??false)
                        {
                            await docInsertTranformBlock.SendAsync(item).ConfigureAwait(false);
                        }
                        else
                        {
                            await docReindexTransformBlock.SendAsync(item).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        DeleteElasticDocumentWhenNotOnDiskSource(item);
                    }
                }
                catch (Exception ex)
                {
                    if (ex is DirectoryNotFoundException || ex is PathTooLongException || ex is UnauthorizedAccessException)
                    {
                        if (ilwarn) _il.LogWarn(ex.Message, item.Id, null);
                    }
                    else
                    {
                        if (ilerror) _il.LogErr(nameof(GetFromQueryAndPostToTransformBlock), item.Id, null, ex);//errors here when filer goes offline
                    }
                }
            }
            return count;
        }

        private void DeleteElasticDocumentWhenNotOnDiskSource(IFSO item)
        {
            //Elastic document doesn't exist on disk currently:
            Interlocked.Increment(ref _filesnotfound);
            if (item.Timestamp < DateTime.UtcNow.Subtract(TimeSpan.FromDays(14)) && IsCrawlServerOnline(item.Id))
            {
                Interlocked.Increment(ref _deleted);
                _deleted += _indexEndPoint.Delete(item.Id, item.IndexName);//TODO - we probably want to ensure that we can't delete large number of items due to communication failure(item.Id, item.IndexName);
                if (item is FSOdirectory)
                {
                    if (ildebug) _il.LogDebugInfo("Directory Not Found", item.Id);
                    _deleted += _indexEndPoint.DeleteDirectoryDescendants(item.Id, new string[] { FSOdirectory.indexname, FSOfile.indexname, FSOdocument.indexname, FSOemail.indexname });
                }
                else
                {
                    if (ildebug) _il.LogDebugInfo("File Not Found", item.Id);
                }
            }
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
                        this._indexEndPoint = new DAL.Index(_args.ElasticIndexURI.ToArray(), new Elastic.Logger.Log4NetLogger("Index"));
                        this._discoveryEndPoint = new DAL.Discovery(_args.ElasticDiscoveryURI.ToArray(), new Elastic.Logger.Log4NetLogger("Discovery"));
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
