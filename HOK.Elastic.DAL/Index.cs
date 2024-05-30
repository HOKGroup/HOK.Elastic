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



        public Index(Uri uri, Logger.Log4NetLogger logger) : base(uri, logger)
        {
        }

        public Index(IEnumerable<Uri> uri, Logger.Log4NetLogger logger)
            : base(uri, logger)
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
                if (ildebug) _il.LogDebugInfo("Index.InsertFile", item.Id, true);
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
                if (ildebug) _il.LogDebugInfo("Index.Insertdirectory", item.Id, true);
            }
        }

        public void InsertEmail(FSOemail item)
        {
            var response = this.client.Index(item, i => i
                .Index(FSOemail.indexname)
                .Timeout(defaultQueryTimeout)
                );
            if (!response.IsValid)
            {
                var err = ElasticResponseError.GetError(response);
                if (ilerror) _il.LogErr("Index.InsertEmail", item.Id, err);
            }
            else
            {
                if (ildebug) _il.LogDebugInfo("Index.InsertEmail", item.Id);
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
                        .Index(FSOdocument.indexname)
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
                    if (ildebug) _il.LogDebugInfo("Index.InsertTika", item.Id);
                }
            }
        }

        private void InsertTikaDocNonAttachment(FSOdocument item)
        {
          var  response = this.client.Index(item, i => i
                                .Pipeline(InitializationPipeline.PIPEvalidate)//override
                                .Index(FSOdocument.indexname)
                                .Timeout(defaultQueryTimeout)//todo decide on how to handle timeouts
                                );
            if (!response.IsValid)
            {
                var indexError = ElasticResponseError.GetError(response);
                if (ilerror) _il.LogErr("Index.InsertTika-Nocontent", item.Id, indexError, null);
            }
            else
            {
                if (ildebug) _il.LogDebugInfo("Index.InsertTika-Nocontent", item.Id);
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
                                              _il.LogDebugInfo($"Bulk success {response.Items.Count()}");
                                          }
                                      }
                                  })
                                  .DroppedDocumentCallback((response, o) =>
                                  {
                                      //this doesn't seem to get called for failed documents but it's here just in case
                                      failures.Add(o);
                                      if (ilwarn)
                                      {
                                          _il.LogWarn("Bulk fail", response.Id, response.Error?.Reason);
                                      }
                                  })
                                  );
                try
                {
                    bulk.Wait(TimeSpan.FromMinutes(5), next =>
                      {
                          if (ildebug)
                          {
                              _il.LogDebugInfo($"Bulked {next.Items.Count:N0} of {group.Count():N0} items, in {next.Retries.ToString()} retries, up to item '{next.Items.Last().Id}'", next.Items.First().Id, null);//we could pass dws here but it will fillup the logs
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
                        if (doc.IndexName == FSOdocument.indexname || doc.IndexName == FSOemail.indexname)
                        {
                            exist = GetById<FSOfile>(doc.Id, FSOfile.indexname);
                        }
                        else
                        {
                            _il.LogErr("Unable to update1", doc.Id, err);
                            return;//TODO
                        }

                        if (exist != null)
                        {
                            Delete(exist.Id, FSOfile.indexname);
                            doc.Reason += " relocate from fsofile.";
                            Insert(doc);//because of the 404 above, this would never be an update.
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

        #region Moves



        ////        /// <summary>
        ////        /// Called by Nausni Audit Events - Full path to the directory will match on anything with the same parent. We use this during incremental crawl
        ////        /// </summary>
        ////        /// <param name="pageSize"></param>
        ////        /// <returns>Fully Populated Model</returns>
        ////        public IEnumerable<T> FindDescendentsForMovingOLD<T>(string path, int pageSize) where T : class, IFSO
        ////        {
        ////            int desiredTake = pageSize;
        ////            T doc;
        ////            string scrolltimeout = "10h";
        ////            string indexName = GetIndexName<T>().ToString();
        ////            ISearchResponse<T> searchResponse = null;
        ////            searchResponse = client.Search<T>(d => d
        ////                        .Index(indexName)
        ////                        .Size(pageSize)//in 10m 
        ////                        .Scroll(scrolltimeout)
        ////                        .Source(a => a.Includes(i => i.Fields(JustId)))
        ////                        .Query(q => q
        ////                           .Bool(b => b
        ////                              .Filter(bf => bf
        ////                               .Term("parent.smbtreelower", path)//was parent.keyword
        ////                               )
        ////                              )
        ////                           )
        ////                        );
        ////            while (searchResponse != null && searchResponse.Documents.Any())
        ////            {
        ////#if DEBUG
        ////                var scrollTime = DateTime.Now;
        ////#endif
        ////                var scrollSearchIds = searchResponse.Hits.Select(x => x.Id).ToList();
        ////                List<T> docs = new List<T>();
        ////                while (scrollSearchIds.Any())
        ////                {
        ////                    try
        ////                    {
        ////                        var results = client.MultiGet(m => m.Index(indexName).GetMany<T>(scrollSearchIds.Take(pageSize), (op, id) => op.Index(indexName)));
        ////                        foreach (var hit in results.Hits)
        ////                        {
        ////                            doc = hit.Source as T;
        ////                            docs.Add(doc);
        ////                        }
        ////                        scrollSearchIds.RemoveRange(0, Math.Min(scrollSearchIds.Count, pageSize));
        ////                    }
        ////                    catch (Exception ex)
        ////                    {
        ////                        if (pageSize == 1)//we are working with a single document.
        ////                        {
        ////                            var id = scrollSearchIds.First();
        ////                            scrollSearchIds.RemoveRange(0, 1);//we need to remove the actual document!                
        ////                            if (ex is UnexpectedElasticsearchClientException)
        ////                            {
        ////                                if (ex.Message.Contains("expected"))
        ////                                {
        ////                                    Delete(id, indexName);
        ////                                    if (ilwarn) _il.LogWarn("Deleting document because" + ex.Message, id);
        ////                                }
        ////                            }
        ////                            pageSize = desiredTake;
        ////                        }
        ////                        pageSize = Math.Max(1, pageSize / 3);
        ////                    }
        ////                }
        ////                foreach (var d in docs)
        ////                {
        ////                    yield return d;
        ////                }
        ////#if DEBUG
        ////                if (ildebug)
        ////                {
        ////                    _il.LogDebugInfo("OurScroll took: " + DateTime.Now.Subtract(scrollTime).TotalMinutes.ToString());
        ////                }
        ////#endif
        ////                searchResponse = client.Scroll<T>(scrolltimeout, searchResponse.ScrollId);
        ////            }
        ////            if (searchResponse != null)
        ////            {
        ////                if (searchResponse.IsValid == false)
        ////                {
        ////                    if (ilerror)
        ////                    {
        ////                        var err = ElasticResponseError.GetError(searchResponse);
        ////                        _il.LogErr("Discovery.FindDescendentsForMoving", path, err);
        ////                        throw new InvalidOperationException(err.ServerErrorReason ?? "unknown scroll error");///hmm do we need to throw an error or can we try again or skip?
        ////                    }
        ////                }
        ////                client.ClearScroll(new ClearScrollRequest(searchResponse.ScrollId));
        ////            }
        ////        }


        /// <summary>
        /// Called by Nausni Audit Events - Full path to the directory will match on anything with the same parent.
        /// </summary>
        /// <param name="pageSize"></param>
        /// <returns>Fully Populated Model</returns>
        public IEnumerable<IFSO> FindDescendentsForMoving(string path)
        {
            var docs = FindDescendentsForMoving<IFSO>(path, 1000);
           foreach(var page in docs)
            {
                foreach(var doc in page)
                {
                   yield return doc;
                }
            }
        }

        public IEnumerable<List<T>> FindDescendentsForMoving<T>(string path, int pageSize) where T : class, IFSO
        {
           var documents = FindDescendants<T>(path,null,SourceFilterDescriptors<T>.IncludeAlls, pageSize);
            return documents;
        }

        #endregion

        #region Deletes

        public long DeleteGroup(FSO[] docs)
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
                        _il.LogDebug($"{nameof(DeleteGroup)} {docsByIndex.Key} items from {group.Key}{string.Join(",", group.ToList())}");
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
                    if (ilwarn) _il.LogWarning("Deleted {1} from {0}", directoryPublishedPath, response.Deleted);
                }
                else if (response.Deleted > CRITICALDELETEMORETHAN)
                {
                    if (ilerror) _il.LogCritical("Deleted an alarming lot of items from {0}! ({1})", directoryPublishedPath, response.Deleted);
                }
                else if (response.Failures.Any())
                {
                    if (ilwarn) _il.LogWarning("Deleted {1} from {0} but had {2} failures", directoryPublishedPath, response.Deleted, response.Failures.Count );
                }
                else
                {
                    if (ildebug) _il.LogDebugInfo("Deleted {1} from {0}", directoryPublishedPath, response.Deleted);
                }
            }
            return response.Deleted;
        }

      
        public long DeleteAbandonedDocuments<T>(string directoryPath, List<string> extantChildren, BatchBlock<T> deleteBlock) where T : class, IFSO
        {
            int pageSize = 1000;
            int totalDeletedCount = 0;
            var lastCheck = 0;
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 50 };
            bool exit = false;
            var docGroup = FindDescendants<T>(directoryPath, extantChildren, SourceFilterDescriptors<T>.JustIds, pageSize, false);//No PIT

            while (!exit)
            {
                try
                {
                    foreach (var group in docGroup)
                    {
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
                                if (ilwarn) _il.LogWarning("More than {0} abandoned documents deleted under '{1}' ", WARNIFDELETEMORETHAN, directoryPath);
                                exit = true;
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
                                        throw new Exception($"Unexpected Query failure.....'{doc.Id}' shouldn't exist but does.");
                                    }
                                    else
                                    {
                                        localCount++;
                                        if (!deleteBlock.Post(doc))
                                        {
                                            _il.LogWarn("Couldn't POST...shouldn't be possible");
                                            // _il.LogTrace("Deleting abandonded '{0}'", doc.Id);
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
                }
                catch (Exception ex)
                {
                    _il.LogError(ex, ex.Message);
                    exit = true;
                }
                if (totalDeletedCount == pageSize)//number should match for nonPIT first run unless there were documentst that existed that shouldn't have - in which case we want to exit anyways.
                {
                    this.client.Indices.Refresh(base.AllIndicies, x => x.Index(AllIndicies));//to avoid getting the same documents again
                    docGroup = FindDescendants<T>(directoryPath, extantChildren,SourceFilterDescriptors<T>.JustIds, pageSize, true);//search with PIT going forward.
                }
            }
            return totalDeletedCount;
        }


        public IEnumerable<List<T>> FindDescendants<T>(string directoryPath, List<string> exceptTheseExtantChildren, SourceFilterDescriptor<T> sourceFilter, int pageSize = 100, bool withPIT = false) where T : class, IFSO
        {
            int counter = 0;
            long docCount = 0;
            string pitID = null;
            IHit<T> lastHit = null;
            PointInTimeDescriptor pointInTime = null;

            var mustNots = new List<Func<QueryContainerDescriptor<T>, QueryContainer>>();
            if(exceptTheseExtantChildren != null)
            {
                //if there are good children don't return the children or the parent.
                foreach (var childPath in exceptTheseExtantChildren)
                {
                    mustNots.Add(q => q.MatchPhrase(w => w.Field(f => f.Id).Query(childPath)));
                }
                mustNots.Add(a => a.Term(new Field("id.keyword"), directoryPath));
            }

            string indexFilter = GetIndexFilterName<T>();
      
            try
            {
                if (withPIT)
                {
                    var pp = GetPIT(indexFilter, new Time(TimeSpan.FromMinutes(5)));
                    if (pp != null)
                    {
                        pitID = pp.Item1;
                        pointInTime = pp.Item2;
                    }
                }
                do
                {
                    var response = client.Search<T>(s => s
                        .Index(indexFilter)
                        .Size(pageSize)
                        .Source(a => a.Includes(i => i
                            .Fields(f => f.Id)
                            ))
                            .Query(q => q
                     .Bool(b => b
                     .Filter(f => f.MatchPhrase(mp => mp
                         .Field(mf => mf.Id)
                         .Query(directoryPath)
                         )
                     )
                     .MustNot(mustNots.ToArray()))
                     )
                            .PointInTime(pitID, x => pointInTime)//null if couldn't do a point in time search.
                        .Sort(srt => srt.Ascending(f => f.Timestamp))
                    .SearchAfter(lastHit?.Sorts ?? null)
                    );


                    if (response != null && response.IsValid)
                    {
                        //For paths with derived folder names (not necessarily children folders) the id field matchphrase query used above will return superfluous documents.
                        //For example, when the documents should be within the path '.\\a\\', elastic matchphrase will also return  '.\\a nother folder\\..' as well as '.\\a big folder\\' as abandoned items and comparing to known/good/extant children.
                        //To resolve this, rather than use wildcard query filtering for a '\\' delimiter...which is expensive, we just filter the results client-side based on string value of id.
                        var docs = response.Hits.Where(x => x.Id.Length > directoryPath.Length && x.Id[directoryPath.Length] == '\\').Select(x =>
                        {
                            var doc = x.Source as T;
                            doc.IndexName = x.Index;
                            return doc;
                        }
                        );
                        var doclist = docs.ToList();
                        docCount = +doclist.Count;
                        yield return doclist;
                        lastHit = response.Hits.LastOrDefault();
                        pitID = response.PointInTimeId;
                    }
                    _il.LogDebug("{0} loop #{1} returning documents in '{2}'", nameof(FindDescendants), counter++, directoryPath);
                } while (withPIT && lastHit != null);

            }
            finally
            {
                if (pitID != null)
                {
                    var closeResponse = client.ClosePointInTime(p => p.Id(pitID));
                }               
            }
            if (ilinfo)
            {
                if (exceptTheseExtantChildren != null && exceptTheseExtantChildren.Any())
                {
                    _il.LogInformation("{0} returned aprox {1} abandoned documents in '{2}'", nameof(FindDescendants), docCount, directoryPath);
                }
                else
                {
                    _il.LogInformation("{0} returned aprox {1} documents in '{2}'", nameof(FindDescendants), docCount, directoryPath);
                }
            }
        }
        #endregion
    }
}








