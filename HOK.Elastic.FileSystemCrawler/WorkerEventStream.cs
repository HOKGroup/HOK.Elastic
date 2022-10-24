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

namespace HOK.Elastic.FileSystemCrawler
{
    public class WorkerEventStream : WorkerBase
    {
        public WorkerEventStream(IIndex elasticIngest, IDiscovery elasticDiscovery, SecurityHelper sh, DocumentHelper dh, Logger.Log4NetLogger logger) : base(elasticIngest, elasticDiscovery, dh, sh, logger)
        {
        }

        public override CompletionInfo Run(ISettingsJobArgs args, CancellationToken ct = default)
        {
            var awaiter = RunAsync(args, ct).GetAwaiter();
            return awaiter.GetResult();
        }
        /// <summary>
        /// This is similar to the Full Crawl, Incrmental Crawl but takes the list of items fetched from NasuniApi 
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public override async Task<CompletionInfo> RunAsync(ISettingsJobArgs args, CancellationToken ct = default)
        {
            var completionInfo = new CompletionInfo(args);
            Interlocked.Exchange(ref _filesmatched, 0);
            Interlocked.Exchange(ref _filesnotfound, 0);
            Interlocked.Exchange(ref _filesskipped, 0);
            Interlocked.Exchange(ref _dircount, 0);
            Interlocked.Exchange(ref _deleted, 0);
            _args = args;
            if (ilinfo)
            {
                if (ildebug)
                {
                    _il.LogDebugInfo("Starting", "", _args);
                }
                _il.LogInfo("Discovery" + _discoveryEndPoint.GetClientStatus());
                _il.LogInfo("IndexNodes" + _indexEndPoint.GetClientStatus());
            };
            DataflowLinkOptions linkOptions = new DataflowLinkOptions() { PropagateCompletion = true };
            try
            {
                #region setup dataflowblocks
                BufferBlock<InputPathEventStream> bb = new BufferBlock<InputPathEventStream>(new DataflowBlockOptions { BoundedCapacity = 300 });
                ActionBlock<InputPathEventStream> blockActionMove = new ActionBlock<InputPathEventStream>(item => ActionMoveOrCopy(item), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 4, BoundedCapacity = 4 });
                ActionBlock<InputPathEventStream> blockActionUpdateOrNew = new ActionBlock<InputPathEventStream>(item => ActionUpdateOrNew(item), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 4, BoundedCapacity = 4 });
                ActionBlock<InputPathEventStream> blockActionDelete = new ActionBlock<InputPathEventStream>(item => ActionDelete(item), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 4, BoundedCapacity = 4 });
                bb.LinkTo(blockActionDelete, linkOptions, item => item.PresenceAction == ActionPresence.Delete);
                bb.LinkTo(blockActionMove, linkOptions, item => item.PresenceAction == ActionPresence.Move);
                bb.LinkTo(blockActionUpdateOrNew, linkOptions, item => item.PresenceAction == ActionPresence.None);
                docInsertTranformBlock = new TransformBlock<IFSO, IFSO>(item => DocumentHelper.InsertTransform(item));
                docReindexTransformBlock = new TransformBlock<IFSO, IFSO>(item => DocumentHelper.ReindexTransform(item));
                docUpdateExistingTransformBlock = new TransformBlock<IFSO, IFSO>(item => DocumentHelper.ReindexTransform(item));
                docInsert = new ActionBlock<IFSO>(item => DocumentHelper.Insert(item), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1, BoundedCapacity = 2 });
                docInsertReindex = new ActionBlock<IFSO>(item => DocumentHelper.Insert(item), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1, BoundedCapacity = 2 });
                docInsertArray = new ActionBlock<IFSO[]>(item => DocumentHelper.Insert(item), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1, BoundedCapacity = 2 });
                if (args.ReadFileContents == true)
                {
                    docInsertBatch = new BatchBlock<IFSO>(args.CrawlThreads);
                }
                else
                {
                    docInsertBatch = new BatchBlock<IFSO>(args.BulkUploadSize??100);
                }
                docInsertBatch.LinkTo(docInsertArray, linkOptions);
                docUpdate = new ActionBlock<IFSO>(item => DocumentHelper.Update(item));//todo make update methods
                docReindexTransformBlock.LinkTo(docInsertReindex, linkOptions);
                docUpdateExistingTransformBlock.LinkTo(docUpdate, linkOptions);
                docInsertTranformBlock.LinkTo(docInsertBatch, linkOptions, item => DocumentHelper.IsBatchable(item));
                docInsertTranformBlock.LinkTo(docInsert, linkOptions, item => !DocumentHelper.IsBatchable(item));
                docInsertTranformBlock.LinkTo(DataflowBlock.NullTarget<IFSO>(), linkOptions);
                #endregion
                #region DoWork
                int count = 0;
                foreach (InputPathEventStream path in args.InputPaths)
                {
                    if (ildebug) _il.LogDebugInfo("Bufferblock Add", path.Path, path);
                    await bb.SendAsync(path).ConfigureAwait(false);
                    count++;
                }
                if (ilinfo) _il.LogInfo("Bufferblock Count", null, count);
                bb.Complete();
                if (ilinfo) _il.LogInfo("Bufferblock Complete");
                await Task.WhenAll(blockActionDelete.Completion, blockActionMove.Completion, blockActionUpdateOrNew.Completion).ConfigureAwait(false);
                if (ilinfo) _il.LogInfo("Actionblocks Complete");
                docInsertTranformBlock.Complete();
                docReindexTransformBlock.Complete();
                docUpdateExistingTransformBlock.Complete();
                await Task.WhenAll(docInsertArray.Completion, docInsert.Completion, docInsertReindex.Completion, docUpdate.Completion).ConfigureAwait(false);
                if (ilinfo) _il.LogInfo("Completed Task");
                completionInfo.exitCode = CompletionInfo.ExitCode.OK;
                #endregion
            }
            catch (AggregateException aex)
            {
                completionInfo.exitCode = CompletionInfo.ExitCode.None;
                if (ilerror)
                {
                    _il.LogErr("CrawlingAggregateErrors", "", null, aex);//TODO verify log4net enumerates all the inner exceptions when it converts the exception object.
                }
            }
            catch (Exception ex)
            {
                completionInfo.exitCode = CompletionInfo.ExitCode.Fatal;
                if (ilfatal)
                {
                    _il.LogFatal("Fatal...quiting task now", "", null, ex);
                }
            }
            completionInfo.EndTime = DateTime.Now;
            completionInfo.DirCount = _dircount;
            completionInfo.FileCount = _filesmatched;
            completionInfo.FileSkipped = _filesskipped;
            completionInfo.FileNotFound = _filesnotfound;
            completionInfo.Deleted = _deleted;
            return completionInfo;
        }

        private async Task ActionMoveOrCopy(InputPathEventStream auditEvent)
        {
            try
            {
                if (auditEvent.IsDir ? HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreDirectory(auditEvent.Path) : HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreFile(auditEvent.Path))
                {
                    if (ildebug) _il.LogDebugInfo("ActionMove PathTo ShouldIgnore", auditEvent.Path, null);
                    if (!auditEvent.IsDir ? HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreDirectory(auditEvent.PathFrom) : HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreFile(auditEvent.PathFrom))
                    {
                        ActionDelete(auditEvent);
                        Interlocked.Increment(ref _deleted);
                    }
                    Interlocked.Increment(ref _filesskipped);
                    return;
                }
                IFSO ToDoc;
                ToDoc = DocumentHelper.MakeBasicDoc(auditEvent.Path, auditEvent.IsDir);
                if (ToDoc == null)
                {
                    //FileSystem Not Found
                    if (ilwarn) _il.LogWarn("ActionMoveOrCopy NotFound PathTo", auditEvent.Path, auditEvent);
                    Interlocked.Increment(ref _filesnotfound);
                    return;
                }
                ToDoc = DocumentHelper.ReindexTransform(ToDoc);

                if (ildebug) _il.LogDebugInfo("ActionMoveOrCopy OK", auditEvent.Path, auditEvent);
                if (auditEvent.IsDir)
                {
                    var fromPublishedPath = PathHelper.GetPublishedPath(auditEvent.PathFrom);
                    #region movedir
                    //first, move any affected Children....actually we should do this regardless if existing doc was found...
                    var affectedDocuments = _discoveryEndPoint.FindDescendentsForMoving(fromPublishedPath);
                    foreach (var fso in affectedDocuments)
                    {
                       try
                        {
                            //delete pathfrom doc....
                            string oldPath = fso.Id;
                            if (auditEvent.PresenceAction == ActionPresence.Move)
                            {
                                if (fso.IndexName.Equals(FSOdirectory.indexname, StringComparison.OrdinalIgnoreCase) ? !Directory.Exists(oldPath) : !File.Exists(oldPath))
                                {
                                    if (Directory.Exists(PathHelper.ContentRoot))//double-check that the filer is online and available and then delete.
                                    {
                                        Interlocked.Add(ref _deleted, _indexEndPoint.Delete(oldPath, fso.IndexName));
                                    }
                                }
                            }
                            //add new path doc....
                            string newPublishedPath = fso.Id.Replace(fromPublishedPath, ToDoc.PublishedPath);
                            fso.Id = newPublishedPath;
                            fso.SetFileSystemInfoFromId();
                            if (ildebug) _il.LogDebugInfo("ActionMoveOrCopy Child", oldPath, newPublishedPath);
                            fso.Reason = "ActionMoveOrCopy Child";
                            await docReindexTransformBlock.SendAsync(fso).ConfigureAwait(false);
                            Interlocked.Increment(ref _filesmatched);
                        }
                        catch (Exception ex)
                        {
                            if (ex is FileNotFoundException)
                            {
                                if (ilwarn) _il.LogWarn("ActionMoveOrCopy Child NotFound", fso.Id, auditEvent);
                                Interlocked.Increment(ref _filesnotfound);
                            }
                            else
                            {
                                if (ilerror) _il.LogErr("ActionMoveOrCopy Child", auditEvent.Path, auditEvent, ex);
                            }
                        }
                    }
                    var existingdoc = GetExistingDocument(auditEvent);
                    if (existingdoc != null)
                    {
                        //next, delete the existing, old document
                        if (auditEvent.PresenceAction == ActionPresence.Move)
                        {
                            if (!Directory.Exists(auditEvent.PathFrom) && Directory.Exists(PathHelper.ContentRoot))//this was path...but I think we'd only care about deleting the 'old/pathfrom' document...
                            {
                                Interlocked.Add(ref _deleted, _indexEndPoint.Delete(existingdoc.Id, existingdoc.IndexName));
                            }
                        }
                        //next, insert the new document after updating the properties of the existing doc with the new location's properties.
                        existingdoc.Id = ToDoc.Id;
                        existingdoc.SetFileSystemInfoFromId();
                        if (auditEvent.ContentAction >= ActionContent.ACLSet)
                        {
                            existingdoc.Acls = ToDoc.Acls;
                        }
                        existingdoc.Reason = "ActionMove Dir";
                        await docInsertTranformBlock.SendAsync(existingdoc).ConfigureAwait(false);
                        Interlocked.Increment(ref _dircount);
                    }
                    else
                    {
                        //existing doc was null
                        ToDoc.Reason = "ActionMove Dir Null";
                        await docInsertTranformBlock.SendAsync(ToDoc).ConfigureAwait(false);
                        Interlocked.Increment(ref _filesmatched);
                    }
                    #endregion
                }
                else
                {
                    #region movefile
                    //audit event is no dir...MOVE FILE
                    var existingdoc = GetExistingDocument(auditEvent);
                    if (existingdoc != null)
                    {
                        _il.LogDebugInfo("ActionMove File Reindex", existingdoc.Id, ToDoc.Id);
                        if (auditEvent.PresenceAction == ActionPresence.Move)
                        {
                            if (!File.Exists(auditEvent.PathFrom))//this was path which would be the field for destination....which is incorrect
                            {
                                //source doesn't exist, therefore delete the old path from elastic                          
                                Interlocked.Add(ref _deleted, _indexEndPoint.Delete(existingdoc.Id, existingdoc.IndexName));
                            }
                        }
                        existingdoc.Id = ToDoc.Id;
                        existingdoc.SetFileSystemInfoFromId();
                        if (File.Exists(existingdoc.PathForCrawling))// existingdoc.FileSystemInfo.Exists)
                        {
                            if (auditEvent.ContentAction == ActionContent.None)
                            {
                                existingdoc.Reason = "ActionMove and actioncontentnone";
                                await docReindexTransformBlock.SendAsync(existingdoc).ConfigureAwait(false);//I think this should be docinserttransform....as we aren't reindexing anything that currently exists at that ID
                                Interlocked.Increment(ref _filesmatched);
                            }
                            else
                            {
                                ToDoc.Reason = "ActionMove and newcontent";
                                await docInsertTranformBlock.SendAsync(ToDoc).ConfigureAwait(false);//can't think of why we were insterting the existingdoc instead of the todoc......
                                Interlocked.Increment(ref _filesmatched);
                            }
                        }
                        else
                        {
                            if (ilwarn) _il.LogWarn("ActionMove NotFound", existingdoc.Id, auditEvent);
                            Interlocked.Increment(ref _filesnotfound);
                        }
                    }
                    else
                    {
                        ToDoc.Reason = "ActionMove Filedoc null";
                        await docInsertTranformBlock.SendAsync(ToDoc).ConfigureAwait(false);
                        Interlocked.Increment(ref _filesmatched);
                    }
                    #endregion
                }
            }
            catch (Exception ex)
            {
                if (ilerror) _il.LogErr("ActionMove", auditEvent.Path, auditEvent, ex);
            }
        }

        private async Task ActionUpdateOrNew(InputPathEventStream auditEvent)
        {
            try
            {
                if (auditEvent.IsDir ? HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreDirectory(auditEvent.Path) : HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreFile(auditEvent.Path))
                {
                    _il.LogDebugInfo("ActionUpdateOrNew ShouldIgnore", auditEvent.Path, null);
                    Interlocked.Increment(ref _filesskipped);
                    return;
                }
                IFSO newIfso;
                newIfso = DocumentHelper.MakeBasicDoc(auditEvent.Path, auditEvent.IsDir);
                if (newIfso == null)
                {
                    _il.LogDebugInfo("ActionUpdateOrNew NotFound", auditEvent.Path, null);
                    Interlocked.Increment(ref _filesnotfound);
                    return;
                }
                var existingdoc = GetExistingDocument(auditEvent);
                if (existingdoc == null || auditEvent.ContentAction == ActionContent.Write)
                {
                    //new document that's not currently in the index or is existing document that was a file with content update (write)
                    newIfso.Reason = auditEvent.ContentAction == ActionContent.Write ? "ActionUpdateOrNew Write" : "ActionUpdateOrNew Null";
                    await docInsertTranformBlock.SendAsync(newIfso).ConfigureAwait(false);
                    Interlocked.Increment(ref _filesmatched);
                }
                else//existing document...or NOT actioncontent.write ...therefore possibly acls set?
                {
                    Interlocked.Increment(ref _filesmatched);
                    newIfso.Reason = "ActionUpdateOrNew existingdoc or non-write";
                    await docUpdateExistingTransformBlock.SendAsync(newIfso).ConfigureAwait(false);
                    if (auditEvent.IsDir)
                    {
                        Interlocked.Increment(ref _dircount);
                        int counter = 0;
                        //retrieve files and directories (if any) that have this directory as their guardianpath. We assume the acls have changed and need to update the acls on the children
                        var affectedDocuments = _discoveryEndPoint.FindGuardianDocuments<FSO>(existingdoc.PublishedPath, new DateTime(1955, 01, 01), DateTime.Now, 5000);//todo we can't just look for fsofile.also, fso affected child is unsupported reindex.
                        if (affectedDocuments != null)
                        {
                            foreach (var item in affectedDocuments)
                            {
                                counter++;
                                newIfso = DocumentHelper.MakeBasicDoc(item.Id, item.IndexName.Equals(FSOdirectory.indexname,StringComparison.OrdinalIgnoreCase));
                                if (newIfso != null)
                                {
                                    newIfso.Reason = "ActionUpdateOrNew affected child doc";//TODO no evidence of these documents as .dir indicies...
                                    await docUpdateExistingTransformBlock.SendAsync(newIfso).ConfigureAwait(false);
                                }
                            }
                        }
                        if (counter > 0)
                        {
                            Interlocked.Add(ref _filesmatched, counter);
                            if (ildebug) _il.LogDebugInfo(string.Format("Updated {0} items as a result of guardian path change", counter), auditEvent.Path, counter);
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref _filesmatched);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ilerror) _il.LogErr("ActionUpdateOrNew", auditEvent.Path, auditEvent.ToString(), ex);
            }
        }

        private void ActionDelete(InputPathEventStream auditEvent)
        {
            _il.LogDebugInfo("ActionDelete", auditEvent.Path, auditEvent.ToString());
            try
            {
                var existingdoc = GetExistingDocument(auditEvent);
                if (existingdoc != null)//if the document doesn't exist in the index, there is no need to delete it
                {
                    if (auditEvent.IsDir)
                    {
                        if (!Directory.Exists(auditEvent.Path) && IsCrawlServerOnline(auditEvent.Path))//only if the directory actually doesn't exist should we proceed to delete
                        {
                            if (ildebug) _il.LogDebugInfo("Deleting directory contents", existingdoc.Id, null);
                            //changed this method call to include all indicies so it deletes files and folders that don't exist.
                            var deletedCount = _indexEndPoint.DeleteDirectoryDescendants(existingdoc.Id, new string[] { FSOdirectory.indexname, FSOfile.indexname, FSOdocument.indexname, FSOemail.indexname });
                            Interlocked.Add(ref _deleted, deletedCount);
                        }
                    }
                    else
                    {
                        if (!File.Exists(auditEvent.Path) && IsCrawlServerOnline(auditEvent.Path))//only if the file doesn't actually exist should we delete
                        {
                            if (ildebug) _il.LogDebugInfo("Deleting file.", existingdoc.Id, null);
                            Interlocked.Add(ref _deleted, _indexEndPoint.Delete(existingdoc.Id, existingdoc.IndexName));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ilerror) _il.LogErr("ActionDelete", auditEvent.Path, auditEvent, ex);
            }
        }

        /// <summary>
        /// Returns a document if it exists in elastic, if the event is a move it will search by event's event.PathFrom otherwise it will search by event.Path.
        /// </summary>
        /// <param name="auditEvent"></param>
        /// <returns></returns>
        private IFSO GetExistingDocument(InputPathEventStream auditEvent)
        {
            IFSO existingdoc = null;
            try
            {
                var existingDocPath = PathHelper.GetPublishedPath(auditEvent.PathFrom?.ToLowerInvariant() ?? auditEvent.Path.ToLowerInvariant());
                if (auditEvent.IsDir)
                {
                    existingdoc = _discoveryEndPoint.GetById<FSOdirectory>(existingDocPath, FSOdirectory.indexname);
                }
                else
                {
                    var fi = new FileInfo(existingDocPath);
                    if (FSOemail.CanBeMadeFrom(fi))
                    {
                        existingdoc = _discoveryEndPoint.GetById<FSOemail>(existingDocPath, FSOemail.indexname);
                    }
                    else if (FSOdocument.CanBeMadeFrom(fi))
                    {
                        existingdoc = _discoveryEndPoint.GetById<FSOdocument>(existingDocPath, FSOdocument.indexname);
                    }
                    else
                    {
                        existingdoc = _discoveryEndPoint.GetById<FSOfile>(existingDocPath, FSOfile.indexname);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ilerror) _il.LogErr("Error getting existing document", auditEvent.Path, auditEvent, ex);
            }
            return existingdoc;
        }
    }
}
