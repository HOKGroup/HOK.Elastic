using Elasticsearch.Net;
using HOK.Elastic.DAL.Models;
using Microsoft.Extensions.Logging;
using Nest;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace HOK.Elastic.DAL
{
    //https://www.elastic.co/blog/the-future-of-attachments-for-elasticsearch-and-dotnet
    public partial class Index : Discovery, IIndex
    {
        private readonly int _maxbulkthreads = Convert.ToInt32(Environment.ProcessorCount * 0.75);
        private readonly TimeSpan defaultQueryTimeout = TimeSpan.FromSeconds(120);

        public Index(Uri uri, Logger.Log4NetLogger logger) : base(uri, logger)
        {
        }

        public Index(Uri[] uri, Logger.Log4NetLogger logger)
            : base(uri, logger)
        {
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
                    .Term("id.smbtreelower", directoryPublishedPath)));
            if (!response.IsValid)
            {
                if (ilerror) _il.LogErr("Error deleting directory contents", directoryPublishedPath, null, response.OriginalException);
                throw response.OriginalException;
            }
            else
            {
                if (response.Failures.Any() || response.Deleted > 1000)//arbirtray number of when we want to warn
                {
                    if (ilwarn) _il.LogWarn(string.Format("Deleted {0} items but had {1} failures", response.Deleted, response.Failures.Count), directoryPublishedPath, null);
                    if (response.Deleted > 50000)
                    {
                        if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Critical)) _il.LogCritical("Deleted an alarming lot of items!", directoryPublishedPath, response.Deleted, null);
                    }
                }
                if (ildebug) _il.LogDebugInfo("Deleted from", directoryPublishedPath, response.Deleted);
            }
            return response.Deleted;
        }

        public long Delete(string key, string index)//should we specify only a single, targeted index?//TODO change this to a FILTER query for performance.
        {
             return Delete(new string[] { key }, index);
        }

        public long Delete(string[] keys, string index)
        {
            var ir = this.client.DeleteByQuery<FSO>(d => d
                .Index(index)
                .Query(q => +q
                    .Ids(i => i.Values(keys))
                    )
                );
            if (!ir.IsValid)
            {
                var err = ElasticResponseError.GetError(ir);
                if (ilerror) _il.LogErr("Index.Delete", null, err);//this shouldn't fail normally
                if (ilwarn)
                {
                    for (int i = 0; i < keys.Length; i++)
                    {
                        _il.LogWarn("Index.Delete", keys[i], err.ServerErrorReason);
                    }
                }
                return 0;
            }
            else
            {                
                return ir.Deleted;
            }           
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
            if (thistype == typeof(FSOemail))
            {
                var eml = item as FSOemail;
                InsertEmail(eml);
            }
            else if (thistype == typeof(FSOdocument))
            {
                var doc = item as FSOdocument;
                InsertTikaDoc(doc);
            }
            else if (thistype == typeof(FSOdirectory))
            {
                var doc = item as FSOdirectory;
                InsertDirectory(doc);
            }
            else if (thistype == typeof(FSOfile))
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
            IndexResponse ir = new IndexResponse();
            ir = this.client.Index(item, i => i
                .Index(item.IndexName)
                .Timeout(defaultQueryTimeout)
                );
            if (!ir.IsValid)
            {
                var err = ElasticResponseError.GetError(ir);
                if (ilerror) _il.LogErr("Index.InsertFile", item.Id, err);//this shouldn't fail normally
            }
            else
            {
                if (ildebug) _il.LogDebugInfo("Index.InsertFile", item.Id, true);
            }
        }

        public void InsertDirectory(FSOdirectory item)
        {
            IndexResponse ir = new IndexResponse();
            ir = this.client.Index(item, i => i
                .Index(item.IndexName)
                .Timeout(defaultQueryTimeout)
                );
            if (!ir.IsValid)
            {
                var err = ElasticResponseError.GetError(ir);
                if (ilerror) _il.LogErr("Index.Insertdirectory", item.Id, err);//this shouldn't fail normally.
            }
            else
            {
                if (ildebug) _il.LogDebugInfo("Index.Insertdirectory", item.Id, true);
            }
        }

        public void InsertEmail(FSOemail item)
        {
            IndexResponse ir = new IndexResponse();
            ir = this.client.Index(item, i => i
                .Index(FSOemail.indexname)
                .Timeout(defaultQueryTimeout)
                );
            if (!ir.IsValid)
            {
                var err = ElasticResponseError.GetError(ir);
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
            var temp = DateTime.Now;
            if (item.Content == null)
            {
                InsertTikaDocNonAttachment(item);
            }
            else
            {
                IndexResponse ir = new IndexResponse();
                ir = this.client.Index(item, i => i
                        .Index(FSOdocument.indexname)
                        .Timeout(TimeSpan.FromMinutes(5))//todo decide on how to handle timeouts
                        );
                if (!ir.IsValid)
                {
                    var err = ElasticResponseError.GetError(ir);
                    if (err.IsBecauseBusy()&& item.FailureCount < 1)
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
            var temp = DateTime.Now;
            IndexResponse ir = new IndexResponse();
            ir = this.client.Index(item, i => i
                                .Pipeline(InitializationPipeline.PIPEvalidate)//override
                                .Index(FSOdocument.indexname)
                                .Timeout(defaultQueryTimeout)//todo decide on how to handle timeouts
                                );
            if (!ir.IsValid)
            {
                var indexError = ElasticResponseError.GetError(ir);
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
                                  //.ContinueAfterDroppedDocuments(true)
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
                                              _il.LogDebugInfo($"Bulk success {response.Items.Count()}" );
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
            var ir = client.Update<object>(doc.Id, u => u//specifying T instead of object, causes all properties to get written (instead of using the annotation hints)
            .Index(doc.IndexName)
            .Doc(doc)
            );
           
            if (!ir.IsValid)
            {
                if (ilerror)
                {
                    var err = ElasticResponseError.GetError(ir);//TODO this is a temporary routine to move misplaced documents from October-Nov 2020 where fsodocuments got inserted into fsofile index.Will remove 
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

    }
}








