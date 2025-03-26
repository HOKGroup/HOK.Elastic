using Elasticsearch.Net;
using HOK.Elastic.DAL.Models;
using Microsoft.Extensions.Logging;
using Nest;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks;
using System.IO;
using System.Globalization;

namespace HOK.Elastic.DAL
{
    /// <summary>
    /// This Class is used to search Elastic; We need to search Elastic to enumberate what is already indexed when doing incremental crawls. We can then either skip,insert, update or delete the elastic entry.
    /// </summary>
    public class Discovery : Base, IDiscovery
    {
        internal Type typedir = typeof(FSOdirectory);
        internal Type typefsofile = typeof(FSOfile);
        internal Type typefsodoc = typeof(FSOdocument);
        internal Type typefsoemail = typeof(FSOemail);
        public Discovery(PipeLineNameHelper pipeLineNameHelper, IndexNameHelper indexNameHelper, Uri uri, ILogger logger) : base(pipeLineNameHelper, indexNameHelper, uri, logger)
        {
        }
        public Discovery(PipeLineNameHelper pipeLineNameHelper, IndexNameHelper indexNameHelper, IEnumerable<Uri> uri, ILogger logger) : base(pipeLineNameHelper, indexNameHelper, uri, logger)
        {
        }

        internal static class SourceFilterDescriptors<T> where T : class, IFSO
        {
            static string[] DefaultSourceFieldsFilter = new string[] { "id", "parent", "acls", "last_write_timeUTC", "failureCount","reason" };
            static string[] JustId = new string[] { "id","reason" };
            static public SourceFilterDescriptor<T> IncludeAlls = new SourceFilterDescriptor<T>().IncludeAll();
            static public SourceFilterDescriptor<T> IncludeDefaults = new SourceFilterDescriptor<T>().Includes(f => f.Fields(DefaultSourceFieldsFilter));
            static public SourceFilterDescriptor<T> JustIds = new SourceFilterDescriptor<T>().Includes(f => f.Fields(JustId));
        }
  



        ///// <summary>
        ///// Called by workercrawler recursion
        ///// </summary>
        ///// <param name="path">ensure lowercase</param>
        /////  /// <param name="includeFullSource">set to true to return the full source document (when duplicating a document for example</param>
        ///// <returns>Root document and direct/first-level children</returns>
        public DirectoryContents FindRootAndChildren(string directoryPath, bool includeFullSource)
        {
            SourceFilterDescriptor<FSO> sourceFilter;
            if (includeFullSource)
            {
                sourceFilter = SourceFilterDescriptors<FSO>.IncludeAlls;
            }
            else
            {
                sourceFilter = SourceFilterDescriptors<FSO>.IncludeDefaults;
            }

            int pageSize = 1000;
            int docCount = 0;
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 50 };
            bool exit = false;
            bool firstTimePITUsed = true;
            var docGroup = FindChildrenPIT<FSO>(directoryPath, sourceFilter, pageSize, false);//No PIT
            DirectoryContents directoryContents = null;
            while (!exit)
            {
                try
                {
                    foreach (var group in docGroup)
                    {
                        if (group != null && group.Any())
                        {

                            foreach (var item in group)
                            {
                                docCount++;
                                if (directoryContents == null )
                                {
                                    if (item.Id.Equals(directoryPath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        directoryContents = new DirectoryContents()
                                        {
                                            Id = item.Id,
                                            Acls = item.Acls,
                                            Last_write_timeUTC = item.Last_write_timeUTC,
                                            IndexName = item.IndexName,
                                            Contents = new HashSet<DirectoryContents.Content>()
                                        };
                                    }
                                    else
                                    {
                                        //this isn't expected.
                                        if (ilwarn) _il.LogWarn("Root document wasn't returned {0} ", directoryPath);
                                        directoryContents = new DirectoryContents()
                                        {
                                            Id = directoryPath,
                                            IndexName = IndexHelper.IndexNameDir,
                                            Contents = new HashSet<DirectoryContents.Content>()
                                        };
                                    }
                                }
                                else
                                {
                                    directoryContents.Contents.Add(new DirectoryContents.Content(item.Id, item.IndexName, item.Acls, item.Last_write_timeUTC, item.FailureCount));
                                }
                            }
                        }                       
                    }
                    if (docCount == pageSize)
                    {
                        docGroup = FindChildrenPIT<FSO>(directoryPath, sourceFilter, pageSize, true);//with PIT
                        if (firstTimePITUsed)
                        {
                            directoryContents = null;
                            firstTimePITUsed = false;
                        }
                    }
                    else
                    {
                        exit = true;
                    }
                }
                catch (Exception ex)
                {
                    if (ilerror) _il.LogErr($"Unexpected Error in {nameof(FindRootAndChildren)} because {ex.Message}", "",null,ex );
                    exit = true;
                }
            }
            return directoryContents;
        }

        public IEnumerable<List<T>> FindChildrenPIT<T>(string directoryPath, SourceFilterDescriptor<T> sourceFilter, int pageSize = 100, bool withPIT = false) where T : class, IFSO
        {
            int counter = 0;
            long docCount = 0;
            string pitID = null;
            PointInTimeDescriptor pointInTime = null;
            IHit<T> lastHit = null;
            var indexFilter = GetIndexFilterName<T>();

            try
            {
                if (withPIT)
                {
                    var pit = GetPIT(indexFilter, new Time(TimeSpan.FromMinutes(5)));
                    if (pit != null)
                    {
                        pitID = pit.Item1;
                        pointInTime = pit.Item2;
                    }
                }
                do
                {
                    var search = client.Search<T>(s => s
                        .Index(IndexHelper.AllIndexNames)
                        .Size(pageSize)//if we get results at size limit we will scroll the query.
                        .Source(src => sourceFilter)//we could sort here if we really wanted to ensure we get the 'root' document but it's highly likely to be returned in the sub 1000 query.
                        .Query(q => q
                            .Bool(b => b
                                .Filter(bf => bf
                                    .Term("id.keyword", directoryPath) || bf.Term("parent.keyword", directoryPath)
                                    )
                                )
                            )
                        .PointInTime(pitID, x => pointInTime)//null if couldn't do a point in time search.
                        .Sort(sort => sort.Ascending("id.keyword"))//added to ensure the root/parent/'path we are searching for' is actually found
                        .SearchAfter(lastHit?.Sorts ?? null)
                        );


                    if (search != null && search.IsValid)
                    {
                        //For paths with derived folder names (not necessarily children folders) the id field matchphrase query used above will return superfluous documents.
                        //For example, when the documents should be within the path '.\\a\\', elastic matchphrase will also return  '.\\a nother folder\\..' as well as '.\\a big folder\\' as abandoned items and comparing to known/good/extant children.
                        //To resolve this, rather than use wildcard query filtering for a '\\' delimiter...which is expensive, we just filter the results client-side based on string value of id.
                        //var docs = search.Hits.Where(x => x.Id.Length > directoryPath.Length && x.Id[directoryPath.Length] == '\\').Select(x =>
                        var docs = search.Hits.Select(x =>
                        {
                            var doc = x.Source as T;
                            doc.IndexName = x.Index;
                            return doc;
                        }
                       );
                        var doclist = docs.ToList();
                        docCount = +doclist.Count;
                        yield return doclist;
                        lastHit = search.Hits.LastOrDefault();
                        pitID = search.PointInTimeId;
                    }
                    if (counter > 0&&ildebug)
                    {
                        _il.LogDbgInfo($"{nameof(FindChildrenPIT)} loop #{counter++} returning documents", directoryPath);
                    }
                } while (withPIT && lastHit != null);

            }
            finally
            {
                if (pitID != null)
                {
                    var closeResponse = client.ClosePointInTime(p => p.Id(pitID));
                }
            }
        }

        public IEnumerable<T> FindGuardianDocuments<T>(string guardianPath, DateTime from, DateTime to, int pageSize = 100) where T : class, IFSO
        {
            int counter = 0;
            long docCount = 0;
            string pitID = null;
            PointInTimeDescriptor pointInTime = null;
            IHit<T> lastHit = null;
            var indexFilter = GetIndexFilterName<T>();

            try
            {       
                    var pit = GetPIT(indexFilter, new Time(TimeSpan.FromMinutes(5)));
                    if (pit != null)
                    {
                        pitID = pit.Item1;
                        pointInTime = pit.Item2;
                    }
                do
                {
                    var response = client.Search<T>
                       (s => s
                           .Index(IndexHelper.AllIndexNames)
                           .Size(pageSize)
                           .Source(src => SourceFilterDescriptors<T>.IncludeDefaults)
                           .Query(q => +q
                                   //.DateRange(doc => doc.Field(field => field.last_write_timeUTC).GreaterThan(from).LessThanOrEquals(to)) && +q
                                   .Term(t => t.Field(field => field.Acls.GuardianPath).Value(guardianPath.ToLowerInvariant()))
                                   )
                            .PointInTime(pitID, x => pointInTime)//null if couldn't do a point in time search.
                            .Sort(sort => sort.Ascending("id.keyword"))//added to ensure the root/parent/'path we are searching for' is actually found
                            .SearchAfter(lastHit?.Sorts ?? null)
                               );

                    if (response != null && response.IsValid)
                    {
                        foreach (var hit in response.Hits)
                        {
                            var doc = hit.Source as T;
                            doc.FailureCount++;
                            doc.IndexName = hit.Index;
                            yield return doc;
                        }
                        lastHit = response.Hits.LastOrDefault();
                        pitID = response.PointInTimeId;
                    }
                    if (counter > 0 && ildebug)
                    {
                        _il.LogDbgInfo($"{nameof(FindChildrenPIT)} loop #{counter++} returning documents in '{2}'", guardianPath);
                    }
                } while (lastHit != null);

            }
            finally
            {
                if (pitID != null)
                {
                    var closeResponse = client.ClosePointInTime(p => p.Id(pitID));
                }
                if (docCount > 0 && ilinfo)
                {
                    _il.LogInfo($"{nameof(FindChildrenPIT)} returned aprox {docCount} documents", guardianPath);
                }
            }
        }

        /// <summary>
        /// Called by Nausni Audit Events - Full path to the directory will match on anything with the same parent.
        /// </summary>
        /// <param name="pageSize"></param>
        /// <returns>Fully Populated Model</returns>

        public IEnumerable<IFSO> FindDescendentsForMoving(string path)
        {
            foreach (var page in FindDescendentsForMoving(path, 1000))
            {
                foreach (var doc in page)
                {
                    yield return doc;
                }
            }
           
        }

        public IEnumerable<List<FSO>> FindDescendentsForMoving(string path, int pageSize)
        {
            var documents = FindDescendants(path, new List<string>(), SourceFilterDescriptors<FSO>.IncludeAlls, pageSize);
            return documents;
        }

        public IEnumerable<List<FSO>> FindDescendants(string directoryPath, List<string> exceptTheseExtantChildren, SourceFilterDescriptor<FSO> sourceFilter, int pageSize = 100, bool withPIT = false)
        {
            int counter = 0;
            long docCount = 0;
            string pitID = null;
            IHit<IFSO> lastHit = null;
            PointInTimeDescriptor pointInTime = null;
            int maxClauseCount = 900;//1024 is the default limit..use 900 as a safe buffer.
            var extraClauseExtants = exceptTheseExtantChildren.Skip(maxClauseCount).ToList();
            if(extraClauseExtants.Any())
            {
                if(ilwarn)_il.LogWarn(nameof(FindDescendants)+ " had too many clause/extantchildren and will not be included in query; but will be filtered in code.",directoryPath,extraClauseExtants.Count);
            }

            var mustNots = new List<Func<QueryContainerDescriptor<FSO>, QueryContainer>>();
            if (exceptTheseExtantChildren != null)
            {
                //if there are good children don't return the children or the parent.
                foreach (var childPath in exceptTheseExtantChildren.Take(maxClauseCount))
                {
                    mustNots.Add(q => q.MatchPhrase(w => w.Field(f => f.Id).Query(childPath)));
                }
                mustNots.Add(a => a.Term(new Field("id.keyword"), directoryPath));
            }

            string indexFilter = GetIndexFilterName<IFSO>();

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
                    var response = client.Search<FSO>(s => s
                        .Index(indexFilter)
                        .Size(pageSize)
                        .Source(s=> sourceFilter)
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
                        var docs = response.Hits.Where(x =>
                        x.Id.StartsWith(directoryPath) &&//although directorypath is included as part of the query, sometimes unicode characters of similar(but different) paths are returned (Chinese character for 'Architecture' and 'Structural Engineering' are both returned when searching for 'Architecture' as an example.)
                        !extraClauseExtants.Where(e => x.Id.StartsWith(e)).Any() &&
                        x.Id.Length > directoryPath.Length && x.Id[directoryPath.Length] == '\\').Select(x =>
                        {
                            FSO doc;                            
                            if(x.Index.EndsWith(IndexNameHelper.DIR))
                            {
                                doc = new FSOdirectory();
                            }
                            else if(x.Index.EndsWith(IndexNameHelper.FSOMSG))
                            {
                                doc = new FSOemail();
                            }
                            else if(x.Index.EndsWith(IndexNameHelper.FSODOC))
                            {
                                doc = new FSOdocument();
                            }
                            else
                            {
                                doc = new FSOfile();
                                //we could set doc.indexname to indexhelper.fsodocindexname..to account for aliases specified in the appsettings.json..but maybe not needed.
                            }                
                            FSOdocument.CopyProperties(x.Source, doc);
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
                    else
                    {
                        var err = new ElasticResponseError(response);
                        if (ilerror)
                        {
                            _il.LogErr(nameof(FindDescendants), directoryPath, err, err.Exception);
                        }
                        if (err.IsBecauseBusy())
                        {
                            Pause("MissingContent");
                        }
                    }
                    if (counter > 0 && ildebug)
                    {
                        _il.LogDbgInfo($"{nameof(FindDescendants)} loop #{counter++} returning documents in '{2}'", directoryPath);
                    }
                } while (withPIT && lastHit != null);

            }
            finally
            {
                if (pitID != null)
                {
                    var closeResponse = client.ClosePointInTime(p => p.Id(pitID));
                }
            }
            if (ilinfo&&docCount>0)
            {
                if ( exceptTheseExtantChildren != null && exceptTheseExtantChildren.Any())
                {

                    _il.LogInfo($"{nameof(FindDescendants)} returned aprox {docCount} abandoned documents", directoryPath);
                }
                else if(ildebug)
                {
                    _il.LogDbgInfo($"{nameof(FindDescendants)} returned aprox {docCount} documents", directoryPath);
                }
            }
        }



        /*
         * 
         * {"@timestamp":"2024-05-28T15:16:12.421Z","level":"ERROR","logger":"Default.WorkerEvents","thread":"18",
         * "data":{"message":"ActionMoveOrCopy Child","path":"\\\\?\\unc\\tor-05fs\\canadmin\\internal\\cal\\projects\\2023\\23.81003.00 deloitte canada - calgary orbis\\e-design\\e8-designstudies\\2023-05-17 greenhouse strategy\\2024-05-22 concept imagery",
         * "json":{
         * "PathFrom":"\\\\?\\unc\\tor-05fs\\canadmin\\internal\\cal\\projects\\2023\\23.81003.00 deloitte canada - calgary orbis\\e-design\\e8-designstudies\\2023-05-17 greenhouse strategy\\2023-05-22 concept imagery",
         * "ContentAction":0,
         * "PresenceAction":2,
         * "TimeStampUtc":"2024-05-28T01:15:08Z",
         * "IsDir":true,
         * "Path":"\\\\?\\unc\\tor-05fs\\canadmin\\internal\\cal\\projects\\2023\\23.81003.00 deloitte canada - calgary orbis\\e-design\\e8-designstudies\\2023-05-17 greenhouse strategy\\2024-05-22 concept imagery",
         * "Office":null,
         * "PathStatus":0},
         * "exception":{"message":"index is required to build a url to this API (Parameter 'index')",
         * "stacktrace":"   at Nest.RouteValues.Route(String name, IUrlParameter routeValue, Boolean required)
         * \r\n   at Nest.DeleteByQueryDescriptor`1.<>c.<Index>b__6_0(IDeleteByQueryRequest`1 a, Indices v)
         * \r\n   at HOK.Elastic.DAL.Index.<>c__DisplayClass6_0.<Delete>b__0(DeleteByQueryDescriptor`1 d) in D:\\a\\1\\s\\HOK.Elastic.DAL\\Index.cs:line 66
         * \r\n   at Nest.Extensions.InvokeOrDefault[T,TReturn](Func`2 func, T default)\r\n   at Nest.ElasticClient.DeleteByQuery[TDocument](Func`2 selector)
         * \r\n   at HOK.Elastic.DAL.Index.Delete(String[] keys, String index) in D:\\a\\1\\s\\HOK.Elastic.DAL\\Index.cs:line 66
         * \r\n   at HOK.Elastic.FileSystemCrawler.WorkerEventStream.ActionMoveOrCopy(InputPathEventStream auditEvent) in D:\\a\\1\\s\\HOK.Elastic.FileSystemCrawler\\WorkerEventStream.cs:line 179"}}}
         * */

        public T GetById<T>(string id, string indexName) where T : class, IFSO
        {
            var response = this.client.Get<T>(id, g => g
                        .Index(indexName)
                        );
            if (response.Found)
            {
                var doc = response.Source as T;
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

        #region CrawlByQuery
        /// <summary>
        /// Uses the Elastic client to validate a JSON-string based query
        /// befure using it in the missing content crawl
        /// </summary>
        /// <param name="jsonQueryString"></param>
        /// <returns>True if a valid query</returns>
        public bool ValidateJsonStringQuery(string jsonQueryString)
        {
            var response = client.Indices.ValidateQuery<IFSO>(v => v
                .Index(IndexHelper.AllIndexNames)
                .Query(q => q.Raw(jsonQueryString)));
            if (response.IsValid)
            {
                return true;
            }
            if (ilerror) _il.LogErr("Query Based Missing Content: Query Invalid","N/A", jsonQueryString, null);
            return false;
        }

     

        public IEnumerable<T> GetIFSOsByQuery<T>(string jsonQueryString, int failureCountFilter, DateTime? minimumDate = null) where T : class, IFSO
        {
            int counter = 0;
            long docCount = 0;
            PointInTimeDescriptor pointInTime = null;
            string pitID = null;
            IHit<T> lastHit = null;
            int pageSize = 1000;

            var indexFilter = GetIndexFilterName<T>();
            var pit = GetPIT(indexFilter, new Time(TimeSpan.FromMinutes(5)));
            if (pit != null)
            {
                pitID = pit.Item1;
                pointInTime = pit.Item2;
            }

            //string indexName = GetIndexName<T>();//todo compare with getindexfiltername (fso ifso, wildcard, vs concrete index name?)
            DateTime? maximumDate = null;
            if (!minimumDate.HasValue) minimumDate = new DateTime(1955, 01, 01);
            if (failureCountFilter > 0)
            {
                //if we are processing items with failureCountFilter greater than zero, it means we are looping through items that were just inserted by an incremental or event crawl(in metadataonly mode). Therefore, we should ignore very recent timestamps as they could be items we have just recently inserted and failed at.
                maximumDate = DateTime.Now.Subtract(TimeSpan.FromHours(1));
            }
            ISearchResponse<T> response;
            try
            {
                do
                {
                    response = client.Search<T>(s => s
                                .Index(indexFilter)
                                .Source(src => SourceFilterDescriptors<T>.IncludeDefaults)//added to address Elasticsearch.Net.Utf8Json.JsonParsingException: expected:',', actual:'null' when trying to deserialize null attachment property. TODO, we could use same as getdescendantsformoving (or something like it).
                                .Size(pageSize)
                                .Query(q => +q
                                    .Raw(jsonQueryString) && +q
                                    .DateRange(d => d.Field(field => field.Last_write_timeUTC).GreaterThan(minimumDate.Value)) && +q
                                    .DateRange(d => d.Field(field => field.Timestamp).LessThan(maximumDate)) && +q
                                    )
                                .PointInTime(pitID, x => pointInTime)//null if couldn't do a point in time search.
                                .Sort(sort => sort.Descending(f => f.Timestamp))
                                .SearchAfter(lastHit?.Sorts ?? null)
                                );
                    if (response != null && response.IsValid)
                    {
                        var doclist = response.Hits.Select(x =>
                                               {
                                                   var doc = x.Source as T;
                                                   doc.IndexName = x.Index;
                                                   return doc;
                                               }).ToList();
                        docCount = +doclist.Count;
                        foreach (var doc in doclist)
                        {
                            yield return doc;
                        }
                        lastHit = response.Hits.LastOrDefault();
                        pitID = response.PointInTimeId;
                    }
                    else
                    {
                        var err = new ElasticResponseError(response);
                        if (ilerror)
                        {
                            _il.LogErr("MissingContent", jsonQueryString, err,err.Exception);
                        }
                        if (err.IsBecauseBusy())
                        {
                            Pause("MissingContent");
                        }
                    }
                    _il.LogDbgInfo($"{nameof(GetIFSOsByQuery)} loop #{counter++} returning documents in '{indexFilter}'","",jsonQueryString);
                    if (ilinfo) _il.LogInfo($"Doc {indexFilter} missing content query for items newer then {minimumDate.Value.Year} and failure count {failureCountFilter}", jsonQueryString);

                } while (lastHit != null);

            }
            finally
            {
                if (pit != null)
                {
                    var closeResponse = client.ClosePointInTime(p => p.Id(pitID));
                    //if (ilinfo) _il.LogInformation("Closed" + closeResponse);
                    if(ilinfo) _il.LogInfo("Closed" + closeResponse);
                }
                if (ilinfo) _il.LogInfo($"Doc {indexFilter} missing content query for items newer then {minimumDate.Value.Year} and failure count {failureCountFilter}", jsonQueryString);
            }
        }


        #endregion

        #region MissingContent

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
            long docCount = 0;
            PointInTimeDescriptor pointInTime = null;
            string pitID = null;
            IHit<T> lastHit = null;
            int pageSize = 50;

            string indexFilter;
            DateTime? maximumDate = null;
            if (!minimumDate.HasValue) minimumDate = new DateTime(1955, 01, 01);
            if (failureCountFilter > 0)
            {
                //if we are processing items with failureCountFilter greater than zero, it means we are looping through items that were just inserted by an incremental or event crawl(in metadataonly mode). Therefore, we should ignore very recent timestamps as they could be items we have just recently inserted and failed at.
                maximumDate = DateTime.Now.Subtract(TimeSpan.FromHours(1));
            }
            if (typeof(T) == typeof(FSOdocument))
            {
                indexFilter = IndexHelper.IndexNameFsoDoc;
            }
            else if (typeof(T) == typeof(FSOemail))
            {
                indexFilter = IndexHelper.IndexNameFsoMsg;
            }
            else
            {
                throw new NotSupportedException(typeof(T) + directoryPublishedPath + "is not supported");//won't be caught below should fix that
            }

            var pit = GetPIT(indexFilter, new Time(TimeSpan.FromMinutes(5)));
            if (pit != null)
            {
                pitID = pit.Item1;
                pointInTime = pit.Item2;
            }

            ISearchResponse<T> response;
            try
            {
                do
                {
                    response = client.Search<T>(s => s
                                .Index(indexFilter)
                                .Source(src => SourceFilterDescriptors<T>.IncludeDefaults)
                                .Size(pageSize)
                                .Query(q => +q
                                    .DateRange(d => d.Field(field => field.Last_write_timeUTC).GreaterThan(minimumDate.Value)) && +q
                                    .DateRange(d => d.Field(field => field.Timestamp).LessThan(maximumDate)) && +q
                                    .Term(t => t.FailureCount, failureCountFilter) && +q
                                    .Range(r => r.Field(field => field.LengthKB).GreaterThan(0)) && +q
                                    .Term("parent.smbtreelower", directoryPublishedPath) && !q
                                    .Exists(e => e.Field(field => field.Attachment.ContentType))
                                )
                                .PointInTime(pitID, x => pointInTime)//null if couldn't do a point in time search.
                                .Sort(sort => sort.Descending(f => f.Timestamp))
                                .SearchAfter(lastHit?.Sorts ?? null)
                       );
                    if(response.IsValid)
                    {
                        foreach (var hit in response.Hits)
                        {
                            docCount++;
                            var doc = hit?.Source as T;
                            if (doc != null)
                            {
                                doc.IndexName = hit.Index;
                                yield return doc;
                            }
                            else
                            {
                                if(ilwarn)_il.LogWarn("Couldn't cast source to T",hit.Id,hit.Source);
                            }
                        }
                        lastHit = response.Hits.LastOrDefault();
                        if (ilinfo) _il.LogInfo($"Found {docCount} {indexFilter} docs missing content.", directoryPublishedPath, null);
                    }
                    else
                    {
                        var err = ElasticResponseError.GetError(response);
                        if (ilerror)
                        {
                            _il.LogErr("MissingContent", directoryPublishedPath, err,err.Exception);
                        }
                        if (err.IsBecauseBusy())
                        {
                            Pause("MissingContent");
                        }
                    }
                }
                while(lastHit != null);
            }
            finally
            {
                if (pit != null)
                {
                    var closeResponse = client.ClosePointInTime(p => p.Id(pitID));
                    if (ilinfo) _il.LogInfo("Closed" + closeResponse);
                }
                if (ilinfo) _il.LogInfo($"Doc {indexFilter} missing content query for items newer then {minimumDate.Value.Year} and failure count {failureCountFilter}", directoryPublishedPath);
            }
        }
        #endregion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="indexFilter"></param>
        /// <param name="keepAlive"></param>
        /// <returns>Null if PIT couldn't be obtained.</returns>
        internal Tuple<string, PointInTimeDescriptor> GetPIT(string indexFilter, Time keepAlive)
        {
            string pitID = "";
            PointInTimeDescriptor pointInTime = null;
            var response = client.OpenPointInTime(new OpenPointInTimeRequest(indexFilter) { KeepAlive = keepAlive.ToString() });
            if (response.IsValid)
            {
                pitID = response.Id;
                pointInTime = new PointInTimeDescriptor(pitID);
                pointInTime.KeepAlive(keepAlive);
                return new Tuple<string, PointInTimeDescriptor>(pitID, pointInTime);
            }
            else
            {
                _il.LogWarn("Unable to get PIT");
                return null;
            }
        }

        public string GetIndexFilterName<T>()
        {
            if (typeof(T) == typedir)
            {
                return IndexHelper.IndexNameDir;
            }
            else if (typeof(T) == typefsofile)
            {
                return IndexHelper.IndexNameFsoFile;
            }
            else if (typeof(T) == typefsoemail)
            {
                return IndexHelper.IndexNameFsoMsg;
            }
            else if (typeof(T) == typefsodoc)
            {
                return IndexHelper.IndexNameFsoDoc;
            }else
            {
               return IndexHelper.PrefixWildcard;//TODO this should be allindicies if <T> is IFSO,FSO but not if FSOemail, or FSOdoc etc.
            }
        }
    }
}
