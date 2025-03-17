using Elastic.Elasticsearch.Xunit;
using Elastic.Elasticsearch.Ephemeral;
using Elastic.Elasticsearch.Ephemeral.Plugins;
using Elastic.Stack.ArtifactsApi.Products;

namespace HOK.Elastic.Tests
{
    /// <summary> Declare our cluster that we want to inject into our test classes </summary>
    public class LocalCluster : XunitClusterBase
    {
        /// <summary>
        /// We pass our configuration instance to the base class.
        /// </summary>
        public LocalCluster() : base(new XunitClusterConfiguration(
            "8.17.2",
            ClusterFeatures.None,
            new ElasticsearchPlugins(new List<ElasticsearchPlugin> {}),
            1
        ))
        { }
    }
}
