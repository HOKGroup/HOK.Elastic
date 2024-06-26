using Elasticsearch.Net;
using HOK.Elastic.DAL.Models;
using Microsoft.Extensions.Logging;
using Nest;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.IO;
using System.Text;
using static System.Net.WebRequestMethods;
using Newtonsoft.Json;

namespace HOK.Elastic.DAL
{
    //https://www.elastic.co/blog/the-future-of-attachments-for-elasticsearch-and-dotnet
    public partial class Index : Discovery, IIndex
    {
        private readonly int _maxbulkthreads = Convert.ToInt32(Environment.ProcessorCount * 0.75);
        private readonly TimeSpan defaultQueryTimeout = TimeSpan.FromSeconds(120);
        private const int WARNIFDELETEMORETHAN = 9000;
        private const int CRITICALDELETEMORETHAN = 30000;



        public Index(PipeLineNameHelper pipeLineNameHelper, IndexNameHelper indexNameHelper, Uri uri, ILogger logger) : base(pipeLineNameHelper, indexNameHelper, uri, logger)
        {
        }

        public Index(PipeLineNameHelper pipeLineNameHelper, IndexNameHelper indexNameHelper, IEnumerable<Uri> uri, ILogger logger)
            : base(pipeLineNameHelper, indexNameHelper, uri, logger)
        {
        }


        #region Inserts
        /// <summary>
        /// Insert Single Item into the index; for small documents like fsodirectory or metadataonly, consider using BulkInsert method.
        /// </summary>
        /// <typeparam name="T">type of document to be inserted</typeparam>
        /// <param name="item">document to be inserted</param>
        public void Insert<T>(T item) where T : class, IFSO
        {
            if (item == null) return;//TODO:we really need to stop generating null documents further up the chain...

            var thistype = item.GetType();
            if (thistype == typefsoemail)
            {
                var eml = item as FSOemail;
                InsertEmail(eml);
            }
            else if (thistype == typefsodoc)
            {
                var doc = item as FSOdocument;
                InsertTikaDoc(doc);
            }
            else if (thistype == typedir)
            {
                var doc = item as FSOdirectory;
                InsertDirectory(doc);
            }
            else if (thistype == typefsofile)
            {
                var doc = item as FSOfile;
                InsertFile(doc);
            }
            else
            {
                throw new ArgumentException(thistype.ToString() + " is an unsupported Type");
            }
        }

        public void InsertFile(FSOfile item)
        {
            var response = this.client.Index(item, i => i
                .Index(item.IndexName)
                .Timeout(defaultQueryTimeout)
                );
            if (!response.IsValid)
            {
                var err = ElasticResponseError.GetError(response);
                if (ilerror) _il.LogErr("Index.InsertFile", item.Id, err);//this shouldn't fail normally
            }
            else
            {
                if (ildebug) _il.LogDbgInfo("Index.InsertFile", item.Id, true);
            }
        }

        public void InsertDirectory(FSOdirectory item)
        {
            
            var response = this.client.Index(item, i => i
                .Index(item.IndexName)
                .Timeout(defaultQueryTimeout)
                );
            if (!response.IsValid)
            {
                var err = ElasticResponseError.GetError(response);
                if (ilerror) _il.LogErr("Index.Insertdirectory", item.Id, err);//this shouldn't fail normally.
            }
            else
            {
                if (ildebug) _il.LogDbgInfo("Index.Insertdirectory", item.Id, true);
            }
        }

        public void InsertEmail(FSOemail item)
        {
            var response = this.client.Index(item, i => i
                .Index(IndexHelper.IndexNameFsoMsg)
                .Timeout(defaultQueryTimeout)
                );
            if (!response.IsValid)
            {
                var err = ElasticResponseError.GetError(response);
                if (ilerror) _il.LogErr("Index.InsertEmail", item.Id, err);
            }
            else
            {
                if (ildebug) _il.LogDbgInfo("Index.InsertEmail", item.Id);
            }
        }


        /// <summary>
        /// Should there be a tika parsing exception, we try and insert it with empty content and skip the pipeline.
        /// </summary>
        /// <param name="item"></param>
        public void InsertTikaDoc(FSOdocument item)
        {
            if (item.Content == null)
            {
                InsertTikaDocNonAttachment(item);
            }
            else
            {
               var response = this.client.Index(item, i => i
                        .Index(IndexHelper.IndexNameFsoDoc)
                        .Timeout(TimeSpan.FromMinutes(5))//todo decide on how to handle timeouts
                        );
                if (!response.IsValid)
                {
                    var err = ElasticResponseError.GetError(response);
                    if (err.IsBecauseBusy() && item.FailureCount < 1)
                    {
                        if (ilwarn) _il.LogWarn("Index.InsertTika", item.Id, err);
                        Pause("Index.InsertTika");
                        //try again once and then move on...
                        item.FailureCount++;//we need to increment once that so that we don't end up here next time.
                        InsertTikaDoc(item);
                    }
                    else
                    {
                        item.Content = null;
                        item.FailureCount++;
                        item.FailureReason = err.ServerErrorReason;
                        if (ilwarn) _il.LogWarn("Index.InsertTika", item.Id, err);
                        InsertTikaDocNonAttachment(item);
                    }
                }
                else
                {
                    if (ildebug) _il.LogDbgInfo("Index.InsertTika", item.Id);
                }
            }
        }

        private void InsertTikaDocNonAttachment(FSOdocument item)
        {
          var  response = this.client.Index(item, i => i
                                .Pipeline(PipeLineNameHelper.PIPEvalidate)//override
                                .Index(IndexHelper.IndexNameFsoDoc)
                                .Timeout(defaultQueryTimeout)//todo decide on how to handle timeouts
                                );
            if (!response.IsValid)
            {
                var indexError = ElasticResponseError.GetError(response);
                if (ilerror) _il.LogErr("Index.InsertTika-Nocontent", item.Id, indexError, null);
            }
            else
            {
                if (ildebug) _il.LogDbgInfo("Index.InsertTika-Nocontent", item.Id);
            }
        }

        /// <summary>
        /// bulk insert should only support 'light' documents, so for example the FSOdoc makes sense with metadataonly=true
        /// </summary>
        /// <param name="dws"></param>
        public void BulkInsert(IFSO[] dws, bool crawlContent = false)
        {
            // throw new Exception("don't catch this");
            var groups = dws.GroupBy(x => x.IndexName);//group by type and then do a bulk insert of each type  
            var failures = new ConcurrentBag<IFSO>();
            foreach (var group in groups)
            {
                var bulk = client.BulkAll(group, b => b
                                  .Index(group.Key)
                                  .BackOffTime("60s")
                                  .BackOffRetries(2)
                                  .Timeout(defaultQueryTimeout)
                                  //.RefreshOnCompleted(true)
                                  .MaxDegreeOfParallelism(_maxbulkthreads)
                                  .Size(50)
                                  .ContinueAfterDroppedDocuments(true)
                                  .BulkResponseCallback(response =>
                                  {
                                      if (!response.IsValid && ilwarn)
                                      {
                                          var err = ElasticResponseError.GetError(response);
                                          _il.LogWarn("Bulk request failed", "", err);
                                      }
                                      else
                                      {
                                          if (response.ItemsWithErrors.Any() && ilwarn)
                                          {
                                              _il.LogWarn($"Bulk Errors {response.ItemsWithErrors.Count()}");
                                          }
                                          if (response.Items.Any() && ilwarn)
                                          {
                                              _il.LogDbgInfo($"Bulk success {response.Items.Count()}");
                                          }
                                      }
                                  })
                                  .DroppedDocumentCallback((response, o) =>
                                  {
                                      //this doesn't seem to get called for failed documents but it's here just in case
                                      failures.Add(o);
                                      if (ilwarn)
                                      {
                                          var err = new ElasticResponseError(response);
                                          _il.LogWarn("Bulk fail", response.Id, err);
                                      }
                                  })
                                  );
                try
                {
                    bulk.Wait(TimeSpan.FromMinutes(5), next =>
                      {
                          if (ildebug)
                          {
                              _il.LogDbgInfo($"Bulked {next.Items.Count:N0} of {group.Count():N0} items, in {next.Retries.ToString()} retries, up to item '{next.Items.Last().Id}'", next.Items.First().Id, null);//we could pass dws here but it will fillup the logs
                          }
                      });
                }
                catch (Exception e)
                {
                    if (failures.Any())
                    {
                        if (ilerror) _il.LogErr("Error on Bulk index-Bulkall failure", "", "Will try inserting failed items individually...", e);
                        foreach (var item in failures)
                        {
                            Insert(item);
                        }
                    }
                    else
                    {
                        throw e;
                    }
                }
            }
        }

        public void Update<T>(T doc) where T : class, IFSO
        {
            var response = client.Update<object>(doc.Id, u => u//specifying T instead of object, causes all properties to get written (instead of using the annotation hints)
            .Index(doc.IndexName)
            .Doc(doc)
            );

            if (!response.IsValid)
            {
                if (ilerror)
                {
                    var err = ElasticResponseError.GetError(response);
                    //TODO this is a temporary routine to move misplaced documents from October-Nov 2020
                    //Where fsodocuments got inserted into fsofile index.Will remove 
                    if (err.HttpStatusCode == 404)
                    {
                        IFSO exist;
                       List<string> indexNames = new List<string>() { IndexHelper.IndexNameFsoFile, IndexHelper.IndexNameFsoMsg, IndexHelper.IndexNameFsoDoc, IndexHelper.IndexNameDir };
                        foreach(var indexName in indexNames)
                        {
                            exist = GetById<FSO>(doc.Id, indexName);
                            if(exist!=null)
                            {
                                    Delete(exist.Id, exist.IndexName);
                                    doc.Reason = doc.AppendReason(" relocate from " + exist.IndexName);
                                    Insert(doc);//because of the 404 above, this would never be an update.
                                break;
                            }
                        }
                    }
                    else
                    {
                        _il.LogErr("Unable to update3", doc.Id, err);
                    }
                }
            }
        }
        #endregion

        #region Deletes

        public long DeleteGroup(IFSO[] docs)
        {
            long count = 0;
            var docGroupedByIndex = docs.GroupBy(x => x.IndexName);
            foreach (var docsByIndex in docGroupedByIndex)
            {
                count = +Delete(docsByIndex.Select(x => x.Id).ToArray(), docsByIndex.Key);
                if (ildebug)
                {
                    var logPathGroupings = docsByIndex.GroupBy(files => Path.GetDirectoryName(files.Id), x => Path.GetFileName(x.Id));
                    foreach (var group in logPathGroupings)
                    {
                        _il.LogDbgInfo($"{nameof(DeleteGroup)} items", group.Key,new Tuple<string,List<string>>(docsByIndex.Key,group.ToList()));//string.Join(",", group.ToList()));
                    }
                }
            }
            return count;
        }
        public long Delete(string key, string index)//should we specify only a single, targeted index?//TODO change this to a FILTER query for performance.
        {
            return Delete(new string[] { key }, index);
        }

        public long Delete(string[] keys, string index)
        {
            var response = this.client.DeleteByQuery<FSO>(d => d
                .Index(index)
                .MaximumDocuments(keys.Length)
                .Conflicts(Conflicts.Proceed)
                .RequestsPerSecond(100)
                .Query(q => +q
                    .Ids(i => i.Values(keys))
                    )
                );
            if (!response.IsValid)
            {
                var err = ElasticResponseError.GetError(response);
                if (ilerror) _il.LogErr("Index.Delete", null, err);//this shouldn't fail normally
                if (ilwarn)
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < keys.Length; i++)
                    {
                        sb.AppendLine(keys[i] + ";");
                    }
                    _il.LogWarn("Index.Delete + " + sb.ToString(), err.ServerErrorReason);
                }
                return 0;
            }
            else
            {
                return response.Deleted;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="directoryPublishedPath">path in lowercase of the directory containing the documents to be deleted</param>
        /// <param name="indicies"></param>
        public long DeleteDirectoryDescendants(string directoryPublishedPath, string[] indicies)
        {
            var response = this.client.DeleteByQuery<FSO>(d => d
                .Index(string.Join(",", indicies))
                .Conflicts(Conflicts.Proceed)
                .Query(q => +q
                    .Term("parent.smbtreelower", directoryPublishedPath)));
            if (!response.IsValid)
            {
                if (ilerror) _il.LogErr("Error deleting directory contents", directoryPublishedPath, null, response.OriginalException);
                throw response.OriginalException;
            }
            else
            {
                if (response.Deleted > WARNIFDELETEMORETHAN)
                {
                    if (ilwarn) _il.LogWarn("Deleted", directoryPublishedPath, response.Deleted);
                }
                else if (response.Deleted > CRITICALDELETEMORETHAN)
                {
                    if (ilerror) _il.LogFatal("Deleted an alarming lot of items from {0}! ({1})", directoryPublishedPath, response.Deleted);
                }
                else if (response.Failures.Any())
                {
                    if (ilwarn) _il.LogWarn($"Deleted {response.Deleted} but had failures", directoryPublishedPath, response.Failures.Count );
                }
                else
                {
                    if (ildebug) _il.LogDbgInfo("Deleted", directoryPublishedPath, response.Deleted);
                }
            }
            return response.Deleted;
        }

      
        public long DeleteAbandonedDocuments(string directoryPath, List<string> extantChildren, BatchBlock<IFSO> deleteBlock) 
        {
            int pageSize = 1000;
            int totalDeletedCount = 0;
            var lastCheck = 0;
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 50 };
            bool exit = false;
			var group = FindDescendants(directoryPath, extantChildren, SourceFilterDescriptors<FSO>.JustIds, pageSize, false);//No PIT for first query

            while (!exit)
            {
                try
                {
                   // foreach (var group in docGroup)
                    //{
                        if (group != null && group.Any())
                        {
                            if (totalDeletedCount - lastCheck > pageSize)
                            {
                                lastCheck = totalDeletedCount;
                                if (!Directory.Exists(directoryPath))
                                {
                                    throw new DirectoryNotFoundException($"Verification path doesn't exist '{directoryPath}' but should...we will abort deleting abandoned documents.");
                                }
                            }
                            if (totalDeletedCount > WARNIFDELETEMORETHAN)
                            {
                                if (ilwarn) _il.LogWarn("Exceeded abandoned documents deleted...exiting loop", directoryPath,WARNIFDELETEMORETHAN);
                                exit = true;
                                break;
                            }
                            else
                            {
                                Parallel.ForEach(group, parallelOptions, () => 0, (doc, loopState, localCount) =>
                                {
                                    var di = new DirectoryInfo(doc.Id);
                                    var x = di.Attributes.HasFlag(FileAttributes.Directory) && di.Exists;
                                    bool exists = false;

                                    if (di.Attributes.HasFlag(FileAttributes.Directory))
                                    {
                                        if (di.Exists)
                                        {
                                            exists = true;
                                        }
                                    }
                                    else if (System.IO.File.Exists(doc.Id))
                                    {
                                        exists = true;
                                    }
                                    if (exists)
                                    {
                                        if (ilwarn) _il.LogWarn("Unexepected existing document", doc.Id);//lock files for example are transient and appear occasionally
                                    }
                                    else
                                    {
                                        localCount++;
                                        if (!deleteBlock.Post(doc))
                                        {
                                            _il.LogWarn("Couldn't POST...shouldn't be possible");
                                        }
                                    }
                                    return localCount;

                                }, localCount => Interlocked.Add(ref totalDeletedCount, localCount));
                            }
                        }else
                        {
                            exit = true;
                        }
                    }
                //}
                catch (Exception ex)
                {
                    _il.LogErr(ex.Message, directoryPath, null, ex);
                    exit = true;
                }
                if (totalDeletedCount == pageSize)//number should match for nonPIT first run unless there were documentst that existed that shouldn't have - in which case we want to exit anyways.
                {
                    this.client.Indices.Refresh(IndexHelper.PrefixWildcard, x => x.Index(IndexHelper.AllIndexNames));//to avoid getting the same documents again
                    group = FindDescendants(directoryPath, extantChildren,SourceFilterDescriptors<FSO>.JustIds, pageSize, true);//search with PIT on next iteration.
                }
                else
                {
                    exit = true;
                }
            }
            return totalDeletedCount;
        }
        #endregion
    }
}








