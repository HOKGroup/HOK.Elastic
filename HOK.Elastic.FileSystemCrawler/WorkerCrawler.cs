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
    public class WorkerCrawler : WorkerBase
    {
  
        /// <summary>
        /// we might want to remove this constructor to ensure we always populate path substitutions from here.
        /// </summary>
        /// <param name="logger"></param>
        public WorkerCrawler(IIndex elasticIngest, IDiscovery elasticDiscovery, SecurityHelper sh, DocumentHelper dh, HOK.Elastic.Logger.Log4NetLogger logger) : base(elasticIngest, elasticDiscovery, dh, sh, logger)
        {
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
        /// Initial Crawl/Full Crawl
        /// Full Crawl:
        /// Read every file and index it. Add pause button and save state (array of directories)
        /// Incremental Crawl:
        /// Read Each Directory, If it is had date modified more recent than start of last scan (or maybe we should look up directory modified date)
        /// If modified date is new, enumerate all files and directories. Go to Elastic to get files and directories in this folder (names and dates) compare.
        /// Delete from Elastic any files and all directories (and their contents) where the name no is no longer present.
        /// Index any file that has a modified date newer than the last indexed
        /// Index any file not present.
        /// In all cases regardless of date modified, Index directory (and its content)
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
            if (args.CrawlMode == CrawlMode.Full|| args.CrawlMode==CrawlMode.Incremental)
            {               
                try
                {
                    var offices = args.InputPaths.Select(x => x.Office).Distinct();
                    foreach (var office in offices)
                    {
                        var inputs = GetDirectoriesFromInputPaths(args,office);
                        if (args.CrawlMode == CrawlMode.Full)//recurse multiple input paths in foreach.
                        {
                            await Recurse(inputs, false).ConfigureAwait(false);
                        }
                        else if (args.CrawlMode == CrawlMode.Incremental)
                        {
                            await Recurse(inputs, true).ConfigureAwait(false);
                        }
                    }
                    docInsertTranformBlock.Complete();
                    docReindexTransformBlock.Complete();
                    await Task.WhenAll(docInsert.Completion, docUpdate.Completion, docInsertArray.Completion).ConfigureAwait(false);
                    completionInfo.exitCode = CompletionInfo.ExitCode.OK;
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
            else//not crawl
            {
                throw new ArgumentException($"{args.CrawlMode} is not supported in {nameof(WorkerCrawler)}");
            }
            completionInfo.EndTime = DateTime.Now;
            completionInfo.DirCount = _dircount;
            completionInfo.FileCount = _filesmatched;
            completionInfo.FileSkipped = _filesskipped;
            completionInfo.FileNotFound = _filesnotfound;
            completionInfo.Deleted = _deleted;
            if (completionInfo.exitCode == CompletionInfo.ExitCode.OK)
            {
                InputPathLoader.ClearUnfinishedPaths(_args.InputPathLocation);
            }
            return completionInfo;
        }

        private async Task Recurse(List<FSOdirectory> dirWs, bool isIncremental)
        {
            DateTime unfinishedPathTimer = DateTime.Now;
            DateTime statusUpdateTimer = DateTime.Now;
            long UnexpectedExceptions;
            ConcurrentBag<string> currentItemsAsPublishedPaths;
            HashSet<DirectoryContents.Content> elasticContents;
            bool isExplicit = false;
            if (isIncremental) { currentItemsAsPublishedPaths = new ConcurrentBag<string>(); } else { currentItemsAsPublishedPaths = null; }
            ConcurrentStack<FSOdirectory> stack = new ConcurrentStack<FSOdirectory>();
            foreach (FSOdirectory dirW in dirWs)
            {
                stack.Push(dirW);
            }
            try
            {
                while (stack.Count > 0)
                {
                    _ct.ThrowIfCancellationRequested();
                    if (!stack.TryPop(out FSOdirectory directory))
                    {
                        continue;
                    }
                    if (directory == null) continue;
                    _dircount++;
                    UnexpectedExceptions = 0;
                    isExplicit = false;
                    try
                    {
                        if (HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreDirectory(directory.PathForCrawling))
                        {
                            if (ildebug) _il.LogDebugInfo("Ignoring", directory.PathForCrawling);
                            continue;
                        }
                        var di = new DirectoryInfo(directory.PathForCrawling);
                        if (directory.Acls == null)//after first loop from input paths, ACLS is already populated.
                        {
                            directory.Acls = SecurityHelper.GetDocACLs(di);
                        }
                        var accessControl = di.GetAccessControl(System.Security.AccessControl.AccessControlSections.Access);
                        if (accessControl.AreAccessRulesProtected || accessControl.GetAccessRules(true, false, SecurityHelper.sidType).Count > 0)
                        {
                            isExplicit = true;
                        }               
                        var dirs = System.IO.Directory.EnumerateDirectories(directory.PathForCrawling).ToList();// directory.DirectoryInfo.GetDirectories();
                        var files = Directory.EnumerateFiles(directory.PathForCrawling).ToList();// directory.DirectoryInfo.GetFiles();
                        #region ProcessCurrentDirectoryRegion
                        if (isIncremental)
                        {
                            currentItemsAsPublishedPaths = new ConcurrentBag<string>();
                            DirectoryContents dirContentResponse = _discoveryEndPoint.FindRootAndChildren(directory.Id);
                            elasticContents = dirContentResponse?.Contents;
                            if (dirContentResponse == null)
                            {                                
                                directory.Reason = "incremental newfolder";
                                if (ilwarn) _il.LogWarn(directory.Reason, directory.Id);
                                await docInsertTranformBlock.SendAsync(directory).ConfigureAwait(false);
                            }
                            else
                            {
                                if (dirContentResponse.Acls == null)//could be because root document was missing from index and so was constructed as a placeholder without ACLS.
                                {
                                    directory.Reason = "acls missing-root doc not present";
                                    await docInsertTranformBlock.SendAsync(directory).ConfigureAwait(false);
                                }
                                else
                                {
                                    if (!dirContentResponse.Acls.Equals(directory.Acls))
                                    {
                                        directory.Reason = "dir acls unequal";
                                        await docInsertTranformBlock.SendAsync(directory).ConfigureAwait(false);
                                    }
                                    else
                                    {
                                        ///skip alcs 
#if DEBUG
                                        if (ildebug) _il.LogDebugInfo("dir acls equal skip", directory.PublishedPath);
#endif
                                    }
                                }
                            }
                        }
                        else
                        {
                            directory.Reason = "fullcrawl newfolder";
                            await docInsertTranformBlock.SendAsync(directory).ConfigureAwait(false);
                            elasticContents = null;
                        }

                        #endregion
                        #region WriteOutPathsRegion
                        if (statusUpdateTimer < DateTime.Now)
                        {
                            if (ildebug) _il.LogDebugInfo("Crawled", directory.PathForCrawlingContent, new CrawlMetrics()
                            {
                                DirCount = _dircount,
                                FileCount = Interlocked.Read(ref _filesmatched),
                                FileSkipped = Interlocked.Read(ref _filesskipped),
                                Deleted = Interlocked.Read(ref _deleted)
                            });
                            statusUpdateTimer = DateTime.Now.Add(TimeSpan.FromSeconds(5));//every x amount of time, write a status update.
                            if (unfinishedPathTimer < DateTime.Now)
                            {
                                unfinishedPathTimer = DateTime.Now.Add(TimeSpan.FromMinutes(5));//every x amount of time, write out the paths so we can resume them.
                                InputPathLoader.WriteOutUnfinishedPaths(_args.InputPathLocation, stack, directory);
                            }
                        }
                        #endregion
                        #region CrawlSubDirectoriesRegion

                        if (dirs.Count < _args.CrawlThreads)
                        {
                            foreach (string dir in dirs)
                            {
                                var directoryResult = EnumerateDirectory(directory, dir, isIncremental, isExplicit, currentItemsAsPublishedPaths);
                                if (directoryResult.Item2 != null) stack.Push(directoryResult.Item2);
                                if (directoryResult.Item1 == true) Interlocked.Increment(ref UnexpectedExceptions);
                            }
                        }
                        else
                        {
#if DEBUG//for debugging easier to step thru foreach.
                            foreach (string dir in dirs)
#else
                            Parallel.ForEach(dirs, (dir) =>
#endif
                            {
                                var directoryResult = EnumerateDirectory(directory, dir, isIncremental, isExplicit, currentItemsAsPublishedPaths);
                                if (directoryResult.Item2 != null) stack.Push(directoryResult.Item2);
                                if (directoryResult.Item1 == true) Interlocked.Increment(ref UnexpectedExceptions);
                            }
#if DEBUG

#else
                              );
#endif
                        }

                        #endregion
                        #region CrawlFilesRegion

                        if (files.Count < _args.CrawlThreads)
                        {
                            foreach (var fi in files)
                            {
                                var exceptions = await EnumerateFile(elasticContents, directory, fi, isIncremental, isExplicit, currentItemsAsPublishedPaths).ConfigureAwait(false);
                                if (exceptions) Interlocked.Increment(ref UnexpectedExceptions);
                            }
                        }
                        else
                        {
                            Parallel.ForEach(files, async (fi) =>
                            //foreach (FileInfo fi in directory.DirectoryInfo.EnumerateFiles())
                            {
                                var exceptions = await EnumerateFile(elasticContents, directory, fi, isIncremental, isExplicit, currentItemsAsPublishedPaths).ConfigureAwait(false);
                                if (exceptions) Interlocked.Increment(ref UnexpectedExceptions);
                            }
                            );
                        }

                        #endregion
                        #region AbandonedDocumentsRegion
                        if (isIncremental && elasticContents != null)
                        {
                            if (Interlocked.Read(ref UnexpectedExceptions) > 0)
                            {
                                if (ilwarn)
                                {
                                    _il.LogWarn("AbandonedContent; Skipping due to Unexpected Exceptions", directory.PublishedPath);
                                }
                            }
                            else
                            {
                                if (elasticContents.Count > currentItemsAsPublishedPaths.Count)
                                {
                                    var deletedItems = DeleteAbandonedItems(elasticContents, directory, currentItemsAsPublishedPaths);
                                    Interlocked.Add(ref _deleted, deletedItems);
                                }
                            }
                        }
                        #endregion
                    }
                    catch (AggregateException ae)
                    {
                        foreach (var ex in ae.Flatten().InnerExceptions)
                        {
                            if (ilerror) _il.LogErr("Aggregate Exception:", directory.PathForCrawling, null, ex);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is DirectoryNotFoundException || ex is PathTooLongException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                        {
                            //these are expected exceptions and so we can just warn
                            if (ilwarn) _il.LogWarn(ex.Message, directory.PathForCrawling, null);
                        }
                        else
                        {
                            //unexpected exception maybe we have encountered a transient error that might resolve itself...
                            if (directory.RetryAttemtps < FileSystemRetryAttempts)//number of retry attempts TODO we should make this a setting later.
                            {
                                directory.RetryAttemtps++;
                                if (ilwarn) _il.LogWarn($"Retrying because {ex.Message}", directory.PathForCrawling, null);
                                await Task.Delay(500).ConfigureAwait(false);
                                stack.Push(directory);
                            }
                            else
                            {
                                if (ilerror) _il.LogErr($"Retry limit exceeded", directory.PathForCrawling, FileSystemRetryAttempts, ex);//errors here when filer goes offline
                            }
                        }
                    }
                }//end while
            }
            catch (OperationCanceledException)
            {
                if (ilwarn) _il.LogWarn("Canceled task.", "", stack.Select(x => x.PathForCrawling));//errors here when filer goes offline
                throw;
            }
            catch (Exception ex)
            {
                if (ilerror) _il.LogErr("Recursion error", "", null, ex);
            }
            finally
            {
                if (ildebug) _il.LogDebugInfo($"Recursion Complete. {_dircount} directories, {Interlocked.Read(ref _filesmatched)} files with {Interlocked.Read(ref _filesskipped)} skipped");
            }
            //TODO handle access denied errors - specifically error 53 intermittent network errors. Track number of retries? In          
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="parentDirectory"></param>
        /// <param name="di">DirectoryInfo object to create FSOdirectory from</param>
        /// <param name="isIncremental"></param>
        /// <param name="currentItemsAsPublishedPaths"></param>
        /// <returns>bool(True if unexpected exceptions were encountered) and Child FSO</returns>
        private Tuple<bool, FSOdirectory> EnumerateDirectory(FSOdirectory parentDirectory, string subdir, bool isIncremental, bool isExplicitParent, ConcurrentBag<string> currentItemsAsPublishedPaths)
        {
            FSOdirectory child = null;
            try
            {
                var di = new DirectoryInfo(subdir);
                System.Security.AccessControl.DirectorySecurity accessControlList;
                child = new FSOdirectory(di, parentDirectory.Office, parentDirectory.Project);
                if (isIncremental) currentItemsAsPublishedPaths.Add(child.PublishedPath);
                accessControlList = di.GetAccessControl();
                if (accessControlList.AreAccessRulesProtected || accessControlList.GetAccessRules(true, false, SecurityHelper.sidType).Count > 0)
                {
                    if (isExplicitParent)
                    {
                        //this subfolder has explict permissions and the parent is explicit
                        child.Acls = SecurityHelper.GetDocACLs(di);//therefore we want to look it up...specifically look past the parent.                       
                    }
                    else
                    {
                        //this subfolder has explict permissions but the parent isn't explicit
                        child.Acls = SecurityHelper.GetDocACLs(di, new Tuple<List<string>, string>(parentDirectory.Acls.Guardian, parentDirectory.Acls.GuardianPath));//therefore we can use the parent directory's guardian.
                    }
                }
                else
                {
                    //this subfolder doesn't have explicit permissions so we can reuse parent acls.
                    child.Acls = parentDirectory.Acls;
                }
            }
            catch (Exception ex)
            {
                if (ex is DirectoryNotFoundException || ex is PathTooLongException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is InvalidOperationException)
                {
                    if (ilwarn) _il.LogWarn(ex.Message, parentDirectory.PathForCrawling, null);
                }
                else
                {
                    _il.LogErr("Unexpected Error", subdir, null, ex);
                    return new Tuple<bool, FSOdirectory>(true, child);
                }
            }
            return new Tuple<bool, FSOdirectory>(false, child);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="elasticContents"></param>
        /// <param name="fi"></param>
        /// <param name="isIncremental"></param>
        /// <param name="currentItemsAsPublishedPaths"></param>
        /// <returns>True if unexpected exceptions were encountered</returns>
        private async Task<bool> EnumerateFile(HashSet<DirectoryContents.Content> elasticContents, FSOdirectory parentDirectory, string subfile, bool isIncremental, bool isExplicitParent, ConcurrentBag<string> currentItemsAsPublishedPaths)
        {
            DirectoryContents.Content existingElasticDocument;
            try
            {
                //throw new Exception("fake exception to catch see if we can prevent a delete from happening.");
                if (HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreFile(subfile))
                {
                    Interlocked.Increment(ref _filesskipped);
                    return false;
                }
                var fi = new FileInfo(subfile);
                var fsoFile = new FSOfile(fi);
                var accessControlList = fi.GetAccessControl();
                if (accessControlList.AreAccessRulesProtected || accessControlList.GetAccessRules(true, false, SecurityHelper.sidType).Count > 0)
                {
                    //if a file has explicit permissions set, let's double-check that the parent folder should be the guardian.
                    //we don't want to check if we don't have to.fsoFile.Acls = SecurityHelper.GetDocACLs(fi, null);
                    if (isExplicitParent)
                    {
                        //this subfolder has explict permissions and the parent is explicit
                        fsoFile.Acls = SecurityHelper.GetDocACLs(fi);//therefore we want to look it up...specifically look past the parent.
                    }
                    else
                    {
                        //this subfolder has explict permissions but the parent isn't explicit
                        fsoFile.Acls = SecurityHelper.GetDocACLs(fi, new Tuple<List<string>, string>(parentDirectory.Acls.Guardian, parentDirectory.Acls.GuardianPath));//therefore we can use the parent directory's guardian.
                    }
                }
                else
                {
                    //if the file is inheriting permissions just
                    fsoFile.Acls = SecurityHelper.GetDocACLs(fi, new Tuple<List<string>, string>(parentDirectory.Acls.Guardian, parentDirectory.Acls.GuardianPath));
                }
                if (!isIncremental)
                {
                    fsoFile.Reason = "fullcrawl";
                    await docInsertTranformBlock.SendAsync(fsoFile).ConfigureAwait(false);
                    Interlocked.Increment(ref _filesmatched);
                }
                else if (elasticContents == null)
                {
                    currentItemsAsPublishedPaths.Add(fsoFile.PublishedPath);
                    fsoFile.Reason = "isincremental; elasticContents null";//so just insert it
                    await docInsertTranformBlock.SendAsync(fsoFile).ConfigureAwait(false);
                    Interlocked.Increment(ref _filesmatched);
                }
                else //incremental and elasticContents not null
                {
                    currentItemsAsPublishedPaths.Add(fsoFile.PublishedPath);
                    existingElasticDocument = elasticContents.Where(x => x.Item1 == fsoFile.Id).FirstOrDefault();
                    if (existingElasticDocument == null)
                    {
                        fsoFile.Reason = "isincremental; no match in elasticContents";
                        await docInsertTranformBlock.SendAsync(fsoFile).ConfigureAwait(false);
                        Interlocked.Increment(ref _filesmatched);
                    }
                    else //elasticDocument was found
                    {
                        fsoFile.FailureCount = existingElasticDocument.Item5;
                        if (Math.Abs(fsoFile.Last_write_timeUTC.Subtract(existingElasticDocument.Item4).TotalMinutes) > 5)//the time skew could be either way and we have seen greater then 2 minutes time skew (2 minutes 11 seconds)
                        {
                            fsoFile.Reason = "isincremental; newer";
                            await docInsertTranformBlock.SendAsync(fsoFile).ConfigureAwait(false);
                            Interlocked.Increment(ref _filesmatched);
                        }
                        else //timestamps match
                        {
                            if (existingElasticDocument.Item3 == null || !existingElasticDocument.Item3.Equals(fsoFile.Acls))
                            {
                                fsoFile.Reason = "isincremental; acls unequal";
                                await docReindexTransformBlock.SendAsync(fsoFile).ConfigureAwait(false);//we'll use the update api.
                                Interlocked.Increment(ref _filesmatched);
                            }
                            else
                            {
                                //existing was either null, not null and acls were different or older file.>>not null and not null and acls same and not null and time same
                                Interlocked.Increment(ref _filesskipped);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex is FileNotFoundException || ex is PathTooLongException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is InvalidOperationException)
                {
                    if (ilwarn) _il.LogWarn(ex.Message, subfile, null);
                }
                else
                {
                    _il.LogErr("UnexpectedError", subfile, null, ex);
                    return true;
                }
            }
            return false;
        }

        private long DeleteAbandonedItems(HashSet<DirectoryContents.Content> elasticContents, FSOdirectory directory, ConcurrentBag<string> currentItemsAsPublishedPaths)
        {
           List<DirectoryContents.Content> abandonedItems = elasticContents.Where(x => !currentItemsAsPublishedPaths.Where(a => x.Item1.Equals(a) || x.Item1.StartsWith(a)).Any()).ToList();

            long itemsDeleted = 0;
            foreach (DirectoryContents.Content abandonedItem in abandonedItems)
            {
                try
                {
                    if (abandonedItem.Item2 == FSOdirectory.indexname)
                    {
                        if (ilwarn) _il.LogWarn("DeleteDescendants", abandonedItem.Item1);
                        itemsDeleted += _indexEndPoint.DeleteDirectoryDescendants(abandonedItem.Item1.ToLowerInvariant(), new string[] { FSOdirectory.indexname, FSOfile.indexname, FSOemail.indexname, FSOdocument.indexname });
                    }
                    else
                    {
                        if (ilwarn) _il.LogWarn("DeleteSingle", abandonedItem.Item1);
                        itemsDeleted += _indexEndPoint.Delete(abandonedItem.Item1.ToLowerInvariant(), abandonedItem.Item2);
                    }
                }
                catch (Exception ex)
                {
                    if (ilerror)
                    {
                        _il.LogErr("Error cleaning records from index", directory.PublishedPath, null, ex);
                    }
                    return 0;
                }
            }
            return itemsDeleted;
        }
    }
}
