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
            .Aggregations(x => x.Terms("officesagg", o => o.Field(f => f.Office).Size(1000)))
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
                if (ilerror) _il.LogErr("Failed", null, search.Exception?.Flatten().Message??response.ServerError?.Error.ToString());
            }
            if (ildebug) _il.LogDebug("Offices:" + string.Join(',', offices));
            return offices;
        }

        public async Task<List<FSOdirectory>> FindProjectRootsInArchive(string office)
        {
            string officepath = @$"\\group\hok\{office}\archive\projects";
           // string officepath = @$"\\group\hok\{office}\projects";
            var search = client.SearchAsync<DAL.Models.FSOdirectory>(s => s
                .Index(DAL.Models.FSOdirectory.indexname)
                .Source(s => s.Includes(x => x.Fields(new string[] { "last_write_timeUTC", "id", "name", "parent", "project" })))
                .Size(4)
                .Query(q => q.Bool(x => x.Filter(f => +f
                                    .Term(t => t.IsProjectRoot, true) && +f
                                    .Term(t => t.Office, office) && +f
                                    .Term("parent.smbtreelower", officepath)
           )))
           );
            var results = await search.ConfigureAwait(false);

            if (results.IsValid & results.Hits.Any())
            {
                List<FSOdirectory> list = new List<FSOdirectory>(results.Hits.Count);
                list.AddRange(results.Documents);
                return list;
            }
            return new List<FSOdirectory>();
        }

        public async Task<FSOdirectory?> FindArchiveProjectsInProduction(string office, string projectNumber)
        {
            Dictionary<string, FSOdirectory> keyValuePairs = new Dictionary<string, FSOdirectory>();
            string officepath = @$"\\group\hok\{office}\projects";
            var search = client.SearchAsync<DAL.Models.FSOdirectory>(s => s
                .Index(DAL.Models.FSOdirectory.indexname)
                .Source(s => s.Includes(x => x.Fields(new string[] { "last_write_timeUTC", "id", "name", "parent", "project" })))
                .Size(2)//if more than one result we have improper filing. 
                .Query(q => q.Bool(x => x.Filter(f => +f
                                    .Term(t => t.IsProjectRoot, true) && +f
                                    .Term(t => t.Office, office) && +f
                                    .Term(t => t.Project.Number, projectNumber) && +f
                                    .Term("parent.smbtreelower", officepath)
           )))
           );
            var results = await search.ConfigureAwait(false);

            if (results.IsValid && results.Hits.Any())
            {
                var doc = results.Documents.FirstOrDefault();
                if (doc != null && results.Hits.Count > 1)
                {
                    if (ilwarn) _il.LogWarn("Duplicates", officepath, projectNumber);
                }
                else
                {
                    return doc;
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
