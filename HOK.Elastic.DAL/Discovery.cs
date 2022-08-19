using HOK.Elastic.DAL.Models;
using Microsoft.Extensions.Logging;
using Nest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HOK.Elastic.DAL
{
    /// <summary>
    /// This Class is used to search Elastic; We need to search Elastic to enumberate what is already indexed when doing incremental crawls. We can then either skip,insert, update or delete the elastic entry.
    /// </summary>
    public class Discovery : Base, IDiscovery
    {

        public Discovery(Uri uri, Logger.Log4NetLogger logger) : base(uri, logger)
        {
        }
        public Discovery(Uri[] uri, Logger.Log4NetLogger logger) : base(uri, logger)
        {
        }

        private readonly string[] DefaultSourceFieldsFilter = new string[] { "id", "parent", "acls", "last_write_timeUTC", "failureCount" };
        protected readonly string AllIndicies = StaticIndexPrefix.Prefix + "*";
        private Type typedir = typeof(FSOdirectory);
        private Type typefsofile = typeof(FSOfile);
        private Type typefsodoc = typeof(FSOdocument);
        private Type typefsoemail = typeof(FSOemail);


        /// <summary>
        /// Called by workercrawler recursion
        /// </summary>
        /// <param name="path">ensure lowercase</param>
        /// <returns></returns>
        public DirectoryContents FindRootAndChildren(string path)
        {
            var response = client.Search<FSO>(d => d
                        .Index(AllIndicies)
                        .Size(1000)//if we get results at size limit we will scroll the query.
                        .Sort(sort => sort.Ascending("id.keyword"))//added to ensure the root/parent/'path we are searching for' is actually found
                        .Source(s => s.Includes(inc => inc.Fields(DefaultSourceFieldsFilter)))//we could sort here if we really wanted to ensure we get the 'root' document but it's highly likely to be returned in the sub 1000 query.
                        .Query(q => q
                            .Bool(b => b
                                .Filter(bf => bf
                                    .Term("id.keyword", path) || bf.Term("parent.keyword", path)
                                    )
                                )
                            )
                        );
            if (response.IsValid)
            {
                if (response.Hits.Any())
                {
                    DirectoryContents directoryContents;
                    var root = response.Hits.Where(x => x.Id.Equals(path, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
                    var children = response.Hits.Where(x => x.Id != path);//.Select(x => new DirectoryContents.Content(x.Id.ToLowerInvariant(), x.Index, x.Source.Acls, x.Source.Last_write_timeUTC, x.Source.FailureCount));
                    if (root != default)
                    {
                        directoryContents = new DirectoryContents()
                        {
                            Id = root.Id,
                            Acls = root.Source.Acls,
                            Last_write_timeUTC = root.Source.Last_write_timeUTC,
                            IndexName = root.Index,
                            Contents = new HashSet<DirectoryContents.Content>()
                        };
                    }
                    else if (response.Hits.Count > 0)//root was null but we had some children...
                    {
                        directoryContents = new DirectoryContents()
                        {
                            Id = path,
                            IndexName = FSOdirectory.indexname
                        };
                    }
                    else//root and children were both null...
                    {
                        return null;
                    }

                    if (response.Hits.Count >= 1000)
                    {
                        var docs = FindChildrenScroll(path, false);
                        foreach (var hit in docs)
                        {
                            directoryContents.Contents.Add(new DirectoryContents.Content(hit.Id.ToLowerInvariant(), hit.Index, hit.Source.Acls, hit.Source.Last_write_timeUTC, hit.Source.FailureCount));
                        }
                    }
                    else
                    {
                        if (children != null && children.Any())
                        {
                            directoryContents.Contents = new HashSet<DirectoryContents.Content>(children.Select(hit => new DirectoryContents.Content(hit.Id.ToLowerInvariant(), hit.Index, hit.Source.Acls, hit.Source.Last_write_timeUTC, hit.Source.FailureCount)));
                        }
                    }
                    return directoryContents;
                }
                else
                {
                    return null;//no response hits. 
                }
            }
            else
            {
                var err = ElasticResponseError.GetError(response);
                if (ilwarn) _il.LogWarn(nameof(FindRootAndChildren), path, err);
                return null;//not valid
            }
        }


        /// <summary>
        /// called by workercrawler recursion if there are more than 1k hits
        /// </summary>
        /// <param name="path"></param>
        /// <param name="fullSource"></param>
        /// <returns></returns>
        private IEnumerable<IHit<IFSO>> FindChildrenScroll(string path, bool fullSource)
        {
            SourceFilterDescriptor<FSO> sourceFilter;
            if (fullSource)
            {
                sourceFilter = new SourceFilterDescriptor<FSO>();
                sourceFilter.IncludeAll();
            }
            else
            {
                sourceFilter = new SourceFilterDescriptor<FSO>();
                sourceFilter.Includes(f => f.Fields(DefaultSourceFieldsFilter));
            }
            string scrolltimeout = "10m";
            ISearchResponse<FSO> searchResponse = null;
            searchResponse = client.Search<FSO>(d => d
                        .Index(AllIndicies)
                        .Size(1000)
                        .Scroll(scrolltimeout)
                        .Source(a => sourceFilter)
                        .Query(q => q
                           .Bool(b => b
                              .Filter(bf => bf
                               .Term("parent.keyword", path)
                               )
                              )
                           )
                        );
            while (searchResponse != null && searchResponse.Documents.Any())
            {
                foreach (var hit in searchResponse.Hits)
                {
                    yield return hit;
                }
                searchResponse = client.Scroll<FSO>(scrolltimeout, searchResponse.ScrollId);
            }
            if (searchResponse != null)
            {
                if (searchResponse.IsValid == false)
                {
                    if (ilerror)
                    {
                        var err = ElasticResponseError.GetError(searchResponse);
                        _il.LogErr("Discovery.FindRootAndChildren", path, err);
                        throw new InvalidOperationException(err.ServerErrorReason ?? "unknown scroll error");
                    }
                }
                client.ClearScroll(new ClearScrollRequest(searchResponse.ScrollId));
            }
        }

        /// <summary>
        /// Called by Nausni Audit Events - Full path to the directory will match on anything with the same parent.
        /// </summary>
        /// <param name="pageSize"></param>
        /// <returns>Fully Populated Model</returns>
        public IEnumerable<IFSO> FindChildren(string path)
        {
            foreach (var doc in FindDescendentsForMoving<FSOdirectory>(path))
            {
                yield return doc;
            }
            foreach (var doc in FindDescendentsForMoving<FSOfile>(path))
            {
                yield return doc;
            }
            foreach (var doc in FindDescendentsForMoving<FSOemail>(path))
            {
                yield return doc;
            }
            foreach (var doc in FindDescendentsForMoving<FSOdocument>(path))
            {
                yield return doc;
            }
        }

        /// <summary>
        /// Called by Nausni Audit Events - Full path to the directory will match on anything with the same parent. We use this during incremental crawl
        /// </summary>
        /// <param name="pageSize"></param>
        /// <returns>Fully Populated Model</returns>
        public IEnumerable<T> FindDescendentsForMoving<T>(string path) where T : class, IFSO
        {
            T doc;
            string scrolltimeout = "10m";
            ISearchResponse<T> searchResponse = null;
            searchResponse = client.Search<T>(d => d
                        .Index(GetIndexName<T>())
                        .Size(1000)
                        .Scroll(scrolltimeout)
                        .Source(a => a.IncludeAll())
                        .Query(q => q
                           .Bool(b => b
                              .Filter(bf => bf
                               .Term("parent.smbtreelower", path)//was parent.keyword
                               )
                              )
                           )
                        );
            while (searchResponse != null && searchResponse.Documents.Any())
            {
                foreach (var hit in searchResponse.Hits)
                {
                    doc = hit.Source as T;
                    doc.IndexName = hit.Index;
                    yield return doc;
                }
                searchResponse = client.Scroll<T>(scrolltimeout, searchResponse.ScrollId);
            }
            if (searchResponse != null)
            {
                if (searchResponse.IsValid == false)
                {
                    if (ilerror)
                    {
                        var err = ElasticResponseError.GetError(searchResponse);
                        _il.LogErr("Discovery.FindDescendentsForMoving", path, err);
                        throw new InvalidOperationException(err.ServerErrorReason ?? "unknown scroll error");
                    }
                }
                client.ClearScroll(new ClearScrollRequest(searchResponse.ScrollId));
            }
        }



        private string GetIndexName<T>()
        {
            if (typeof(T) == typedir)
            {
                return FSOdirectory.indexname;
            }
            else if (typeof(T) == typefsofile)
            {
                return FSOfile.indexname;
            }
            else if (typeof(T) == typefsoemail)
            {
                return FSOemail.indexname;
            }
            else if (typeof(T) == typefsodoc)
            {
                return FSOdocument.indexname;
            }
            else
            {
                throw new NotSupportedException(typeof(T) + "is not supported");//won't be caught below should fix that
            }
        }

        /// <summary>
        /// Called by WorkerCrawler's Missing Content.
        /// We want to be careful not to do deep paging. So we can't just query and say let's page through all the result in Elastic. Maybe we could look at find all>aggregate on some fields and then choose those as the filter for paging through the results. or something like that.
        ///Elastic removed Scroll support so we know this is not how they want us to search.
        ///We can use 'sort' + 'searchafter' to get beyone 10000 results. However, the use-case for this is to get bad records, fix them and update the index. so each time the number of hits should get lower.
        ///
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="directoryPublishedPath"></param>
        /// <param name="failureCountFilter"></param>
        /// <param name="minimumDate"></param>
        /// <returns></returns>
        public IEnumerable<T> GetIFSOdocumentsLackingContentV2<T>(string directoryPublishedPath, int failureCountFilter, DateTime? minimumDate = null) where T : class, IFSOdocument
        {
            string scrolltimeout = "30m";//Its value (e.g. 1m, see Time units) does not need to be long enough to process all data-it just needs to be long enough to process the previous batch of results.
            string indexName;
            DateTime? maximumDate = null;
            if (!minimumDate.HasValue) minimumDate = new DateTime(1955, 01, 01);
            if (failureCountFilter > 0)
            {
                //if we are processing items with failureCountFilter greater than zero, it means we are looping through items that were just inserted by an incremental or event crawl(in metadataonly mode). Therefore, we should ignore very recent timestamps as they could be items we have just recently inserted and failed at.
                maximumDate = DateTime.Now.Subtract(TimeSpan.FromHours(1));
            }
            if (typeof(T) == typeof(FSOdocument))
            {
                indexName = FSOdocument.indexname;
            }
            else if (typeof(T) == typeof(FSOemail))
            {
                indexName = FSOemail.indexname;
            }
            else
            {
                throw new NotSupportedException(typeof(T) + directoryPublishedPath + "is not supported");//won't be caught below should fix that
            }
            ISearchResponse<T> searchResponse;
            try
            {
                searchResponse = client.Search<T>(s => s
                                .Index(indexName)
                                .Source(src => src.Includes(inc => inc.Fields(DefaultSourceFieldsFilter)))
                                .Scroll(scrolltimeout)
                                .Size(100)
                                .Sort(sort => sort.Ascending(f => f.Timestamp))//hopefully find the newest documents first and then work through the older ones.
                                .Query(q => +q
                                .DateRange(d => d.Field(field => field.Last_write_timeUTC).GreaterThan(minimumDate.Value)) && +q
                                .DateRange(d => d.Field(field => field.Timestamp).LessThan(maximumDate)) && +q
                                .Term(t => t.FailureCount, failureCountFilter) && +q
                                .Range(r => r.Field(field => field.LengthKB).GreaterThan(0)) && +q
                                .Term("parent.smbtreelower", directoryPublishedPath) && !q
                                .Exists(e => e.Field(field => field.Attachment.ContentType))
                             )
                       );
            }
            catch (Exception ex)
            {
                if (ilerror) _il.LogErr($"Failed Get on Index {indexName}:", directoryPublishedPath, null, ex);
                searchResponse = null;
            }

            while (searchResponse != null && searchResponse.Documents.Any())
            {
                int count = 0;
                foreach (var hit in searchResponse.Hits)
                {
                    count++;
                    var doc = hit.Source as T;
                    doc.IndexName = hit.Index;
                    yield return doc;
                }
                if (ilinfo) _il.LogInfo($"Found {count} {indexName} docs missing content.", directoryPublishedPath, null);
                try
                {
                    searchResponse = client.Scroll<T>(scrolltimeout, searchResponse.ScrollId);
                }
                catch (Exception ex)
                {
                    if (ilerror) _il.LogErr($"Failed Get on Index {indexName}:", directoryPublishedPath, null, ex);
                }
            }
            if (searchResponse.IsValid == false)
            {
                var err = ElasticResponseError.GetError(searchResponse);
                if (ilerror)
                {
                    _il.LogErr("MissingContent", directoryPublishedPath, err);
                }
                if (err.IsBecauseBusy())
                {
                    Pause("MissingContent");
                }
            }
            client.ClearScroll(new ClearScrollRequest(searchResponse.ScrollId));
            if (ilinfo) _il.LogInfo($"Doc {indexName} missing content query for items newer then {minimumDate.Value.Year} and failure count {failureCountFilter}", directoryPublishedPath);
            yield break;
        }


        /// <summary>
        /// Similar to GetIFSOdocumentsLackingContentV2, but takes in a raw JSON query string to fetch documents,
        /// instead of a directory path
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="directoryPublishedPath"></param>
        /// <param name="failureCountFilter"></param>
        /// <param name="minimumDate"></param>
        /// <returns></returns>
        public IEnumerable<T> GetIFSOsByQuery<T>(string jsonQueryString, int failureCountFilter, DateTime? minimumDate = null) where T : class, IFSO
        {
            string scrolltimeout = "30m";//Its value (e.g. 1m, see Time units) does not need to be long enough to process all data-it just needs to be long enough to process the previous batch of results.
            string indexName;
            DateTime? maximumDate = null;
            if (!minimumDate.HasValue) minimumDate = new DateTime(1955, 01, 01);
            if (failureCountFilter > 0)
            {
                //if we are processing items with failureCountFilter greater than zero, it means we are looping through items that were just inserted by an incremental or event crawl(in metadataonly mode). Therefore, we should ignore very recent timestamps as they could be items we have just recently inserted and failed at.
                maximumDate = DateTime.Now.Subtract(TimeSpan.FromHours(1));
            }
            if (typeof(T) == typeof(FSOfile))
            {
                indexName = FSOfile.indexname;
            }
            else if (typeof(T) == typeof(FSOdirectory))
            {
                indexName = FSOdirectory.indexname;
            }
            else if (typeof(T) == typeof(FSOemail))
            {
                indexName = FSOemail.indexname;
            }
            else if (typeof(T) == typeof(FSOdocument))
            {
                indexName = FSOdocument.indexname;
            }
            else
            {
                throw new NotSupportedException(typeof(T) + "is not supported");//won't be caught below should fix that
            }
            ISearchResponse<T> searchResponse;
            try
            {
                searchResponse = client.Search<T>(s => s
                                .Index(indexName)
                                .Scroll(scrolltimeout)
                                .Size(100)
                                .Sort(sort => sort.Descending("project.fullName.keyword"))
                                .Query(q => +q
                                    .Raw(jsonQueryString) && +q
                                    .DateRange(d => d.Field(field => field.Last_write_timeUTC).GreaterThan(minimumDate.Value)) && +q
                                    .DateRange(d => d.Field(field => field.Timestamp).LessThan(maximumDate)) && +q
                             )
                       );
            }
            catch (Exception ex)
            {
                if (ilerror) _il.LogErr($"Failed Get on Index {indexName}:", jsonQueryString, null, ex);
                searchResponse = null;
            }

            while (searchResponse != null && searchResponse.Documents.Any())
            {
                int count = 0;
                foreach (var hit in searchResponse.Hits)
                {
                    count++;
                    var doc = hit.Source as T;
                    //doc.SetFileSystemInfoFromId();                    
                    doc.IndexName = hit.Index;
                    yield return doc;
                }
                if (ilinfo) _il.LogInfo($"Found {count} {indexName} docs missing content.", null, jsonQueryString);
                try
                {
                    searchResponse = client.Scroll<T>(scrolltimeout, searchResponse.ScrollId);
                }
                catch (Exception ex)
                {
                    if (ilerror) _il.LogErr($"Failed Get on Index {indexName}:", jsonQueryString, null, ex);
                }
            }
            if (searchResponse.IsValid == false)
            {
                var err = ElasticResponseError.GetError(searchResponse);
                if (ilerror)
                {
                    _il.LogErr("MissingContent", jsonQueryString, err);
                }
                if (err.IsBecauseBusy())
                {
                    Pause("MissingContent");
                }
            }
            client.ClearScroll(new ClearScrollRequest(searchResponse.ScrollId));
            if (ilinfo) _il.LogInfo($"Doc {indexName} missing content query for items newer then {minimumDate.Value.Year} and failure count {failureCountFilter}", jsonQueryString);
            yield break;
        }

        public IEnumerable<T> FindGuardianDocuments<T>(string guardianPath, DateTime from, DateTime to, int pageSize = 100) where T : class, IFSO
        {
            //TODO to change to searchafter with PIT
            List<T> fsos = new List<T>();
            for (int i = 0; i < pageSize * 100; i++)//tood change the upper-limit we shoudln't limit the results...or think about it.
            {
                var resp = client.Search<T>
                        (search => search
                            .Index(AllIndicies)
                            .From(i * pageSize)
                            .Size(pageSize)
                            .Source(src => src.Includes(inc => inc.Fields(DefaultSourceFieldsFilter)))
                            .Query(q => +q
                                    //.DateRange(d => d.Field(field => field.last_write_timeUTC).GreaterThan(from).LessThanOrEquals(to)) && +q
                                    .Term(t => t.Field(field => field.Acls.GuardianPath).Value(guardianPath.ToLowerInvariant()))
                                    )
                                );
                if (resp.Hits.Count > 0)
                {
                    foreach (var hit in resp.Hits)
                    {
                        var doc = hit.Source as T;
                        doc.FailureCount++;
                        doc.IndexName = hit.Index;
                        yield return doc;
                    }
                    if (resp.Hits.Count < pageSize)
                    {
                        //this is the last iteration where we got hits.
                        yield break;
                    }
                }
                else
                {
                    yield break;
                }
            }
        }


        public T GetById<T>(string id, string indexName) where T : class, IFSO
        {
            var resp = this.client.Get<T>(id, g => g
                        .Index(indexName)
                        );
            if (resp.Found)
            {
                var doc = resp.Source as T;
                doc.IndexName = indexName;
                return doc;
            }
            //else//the nest client sets 'isvalid = false' when returning 404 not found...so it's not necessarily an error, https://www.elastic.co/guide/en/elasticsearch/client/net-api/current/nest-breaking-changes.html
            //{
            //    var err = ElasticResponseError.GetError(resp);
            //    if (ilerror)
            //    {
            //        _il.LogErr("GetbyID", id, err);
            //    }
            //}
            return null;
        }

        /// <summary>
        /// Uses the Elastic client to validate a JSON-string based query
        /// befure using it in the missing content crawl
        /// </summary>
        /// <param name="jsonQueryString"></param>
        /// <returns>True if a valid query</returns>
        public bool ValidateJsonStringQuery(string jsonQueryString)
        {
            var resp = client.Indices.ValidateQuery<IFSO>(v => v
                .Index(AllIndicies)
                .Query(q => q.Raw(jsonQueryString)));
            if (resp.IsValid)
            {
                return true;
            }
            if (ilerror) _il.LogError("Query Based Missing Content: Query Invalid", jsonQueryString, null);
            return false;
        }


    }
}
