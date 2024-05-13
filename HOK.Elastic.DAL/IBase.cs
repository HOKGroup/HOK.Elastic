using Elasticsearch.Net;
using System;

namespace HOK.Elastic.DAL
{
    public interface IBase : IDisposable
    {
        string GetClientStatus();
        ApiKey GetApiKey();
        IndexNameHelper IndexHelper { get;  set; }
        PipeLineNameHelper PipeLineNameHelper { get; set; }
    }
}