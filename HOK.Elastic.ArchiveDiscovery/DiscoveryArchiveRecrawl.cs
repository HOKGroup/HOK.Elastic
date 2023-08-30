using HOK.Elastic.DAL;
using HOK.Elastic.DAL.Models;
using HOK.Elastic.Logger;
using Microsoft.Extensions.Logging;
using Nest;

namespace HOK.Elastic.ArchiveDiscovery
{
    internal class DiscoveryArchiveRecrawl : HOK.Elastic.DAL.Discovery
    {
        public DiscoveryArchiveRecrawl(IEnumerable<Uri> elasticHost, HOK.Elastic.Logger.Log4NetLogger logger) : base(elasticHost, logger)
        {
        }
        public async Task<IEnumerable<string>> FindOffices()
        {
            List<string> offices = new List<string>();
            var aggs = TermsAggregationBuilder(nameof(HOK.Elastic.DAL.Models.FSOdirectory.Office));
            var search = client.SearchAsync<DAL.Models.FSOdirectory>(s => s.Index(DAL.Models.FSOdirectory.indexname)
            .Size(0)
            .Query(q => q.Bool(x => x.Filter(f => f.Term(t => t.IsProjectRoot, true))))
            .Aggregations(x => x.Terms("officesagg", o => o.Field(f => f.Office).Size(1000)))//todo I can't recall why is this size 1000
            );
            var response = await search.ConfigureAwait(false);

            if (response.IsValid)
            {
                var myoffices = response.Aggregations.Terms("officesagg");
                foreach (var item in myoffices.Buckets)
                {
                    offices.Add(item.Key);
                }
            }
            else
            {
                if (ilerror) _il.LogErr("Failed", null, search.Exception?.Flatten().Message ?? response.ServerError?.Error.ToString());
            }
            if (ildebug) _il.LogDebug("Offices:" + string.Join(',', offices));
            return offices;
        }

        public IEnumerable<FSOdirectory> FindProjectRootsInArchive(string pathprefix, string office,string patharchivesuffix)
        {
            string officepath = Path.Combine(pathprefix,office,patharchivesuffix);
            string scrolltimeout = "10m";
            ISearchResponse<FSOdirectory> searchResponse = null;
            // string officepath = @$"\\group\hok\{office}\projects";

            searchResponse = client.Search<DAL.Models.FSOdirectory>(s => s
                .Index(DAL.Models.FSOdirectory.indexname)
                .Source(s => s.Includes(x => x.Fields(new string[] { "last_write_timeUTC", "id", "name", "parent", "project" })))
                .Size(500)
                .Scroll(scrolltimeout)
                .Query(q => q.Bool(x => x.Filter(f => +f
                                    .Term(t => t.IsProjectRoot, true) && +f
                                    .Term(t => t.Office, office) && +f
                                    .Term("parent.smbtreelower", officepath)
           )))
           );

            while (searchResponse != null && searchResponse.Documents.Any())
            {
#if DEBUG
                var scrollTime = DateTime.Now;
#endif

                foreach (var hit in searchResponse.Hits)
                {
                    var doc = hit.Source as FSOdirectory;
                    doc.IndexName = hit.Index;
                    yield return doc;
                }
#if DEBUG
                if (ildebug)
                {
                    _il.LogDebugInfo("OurScroll took: " + DateTime.Now.Subtract(scrollTime).TotalMinutes.ToString());
                }
#endif
                searchResponse = client.Scroll<FSOdirectory>(scrolltimeout, searchResponse.ScrollId);
            }
            if (searchResponse != null)
            {
                client.ClearScroll(new ClearScrollRequest(searchResponse.ScrollId));
                if (searchResponse.IsValid == false)
                {
                    if (ilerror)
                    {
                        var err = ElasticResponseError.GetError(searchResponse);
                        _il.LogErr(nameof(FindProjectRootsInArchive), office, err);
                        throw new InvalidOperationException(err.ServerErrorReason ?? "unknown scroll error");///hmm do we need to throw an error or can we try again or skip?
                    }
                }
            }
        }


        public async Task<FSOdirectory?> FindArchiveProjectsInProduction(string pathprefix,string office,string pathprodsuffix, string projectNumber,string projectName)
        {
            Dictionary<string, FSOdirectory> keyValuePairs = new Dictionary<string, FSOdirectory>();
            string officepath = Path.Combine(pathprefix, office, pathprodsuffix);
            var search = client.SearchAsync<DAL.Models.FSOdirectory>(s => s
                .Index(DAL.Models.FSOdirectory.indexname)
                .Source(s => s.Includes(x => x.Fields(new string[] { "last_write_timeUTC", "id", "name", "parent", "project" })))
                .Size(10)//if more than one result we have improper filing. 
                .Query(q =>                
                q.Bool(x => x.Filter(f => +f
                                    .Term(t => t.IsProjectRoot, true) && +f
                                    .Term(t => t.Office, office) && +f
                                    .Term("project.number.keyword", projectNumber) && +f
                                    .Term("parent.smbtreelower", officepath)

           )))
           );
            var results = await search.ConfigureAwait(false);

            if (results.IsValid && results.Hits.Any())
            {
                //var doc = results.Documents.FirstOrDefault();
                if (results.Hits.Count > 1)
                {
                    var doc = results.Documents.Where(x=>x.Project.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();                    
                    if (ilwarn) _il.LogWarn("Duplicates", officepath, projectNumber);
                    return doc;
                }
                else
                {
                    return results.Documents.FirstOrDefault();
                }
            }
            return null;
        }


        private Dictionary<string, IAggregationContainer> TermsAggregationBuilder(params string[] fieldNames)
        {
            var aggregations = new Dictionary<string, IAggregationContainer>();
            foreach (string field in fieldNames)
            {
                var termsAggregation = new TermsAggregation(field)
                {
                    Field = field
                };

                aggregations.Add(field, new AggregationContainer
                {
                    Terms = termsAggregation
                });
            }
            return aggregations;
        }
    }
}
