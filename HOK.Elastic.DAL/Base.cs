using Elasticsearch.Net;
using Microsoft.Extensions.Logging;
using Nest;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Collections.Generic;

namespace HOK.Elastic.DAL
{
    public class Base : IBase
    {
        protected ElasticClient client;
        protected IConnectionPool connectionPool;
        protected Logger.Log4NetLogger _il;
        protected bool ildebug;
        protected bool ilerror;
        protected bool ilwarn;
        protected bool ilinfo;
        protected Random _random = new Random(DateTime.Now.Second);
        protected ApiKey apiKey;
        protected string apiKeyGuid;
        private bool disposedValue;

        public Base(Uri elastiSearchServerUrl, Logger.Log4NetLogger logger) : this(new SingleNodeConnectionPool(elastiSearchServerUrl), logger)
        {
        }

        public Base(Uri[] elastiSearchServerUrls, Logger.Log4NetLogger logger) : this(new StaticConnectionPool(elastiSearchServerUrls), logger)
        {
        }
        public Base(IConnectionPool connectionPool, Logger.Log4NetLogger logger)
        {
            this.connectionPool = connectionPool;
            _il = logger;
            ildebug = _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug);
            ilinfo = _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information);
            ilwarn = _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Warning);
            ilerror = _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error);
            apiKeyGuid = Guid.NewGuid().ToString() + " - " + this.GetType().Name;
            apiKey = GetApiKey();
            var settings = new ConnectionSettings(connectionPool, new ApiKeyCredentialsHttpConnection(apiKey, GetApiKey));
            settings.MemoryStreamFactory(Elasticsearch.Net.MemoryStreamFactory.Default); //recycle memorystream linked to mem leakage https://github.com/serilog/serilog-sinks-elasticsearch/issues/368
#if DEBUG
            settings.DisablePing();//we don't want to do this. But for some reason it seems to fail when connecting to HOK-395 if it's enabled.
            settings.EnableDebugMode();
            settings.DisableDirectStreaming();

#endif
            settings.RequestTimeout(TimeSpan.FromMinutes(5));//todo change this to a setting             
            this.client = new ElasticClient(settings);
        }
        /// <summary>
        /// https://www.elastic.co/guide/en/elasticsearch/client/net-api/current/modifying-default-connection.html
        /// https://github.com/elastic/elasticsearch-net/issues/533
        /// https://www.elastic.co/guide/en/elasticsearch/client/net-api/6.x/modifying-default-connection.html
        /// unused, i suspect we won't need this but leaving here for now.
        /// </summary>
        public class KerberosConnection : HttpConnection
        {
            protected override HttpRequestMessage CreateRequestMessage(RequestData requestData)
            {
                var message = base.CreateRequestMessage(requestData);
                var header = string.Empty;// "WWW-Authenticate: Negotiate";
                message.Headers.Authorization = new AuthenticationHeaderValue("Negotiate", header);
                return message;
            }
        }


        /// <summary>
        /// NetworkCredential Handler to allow passing the process' default credentials to the elastic service
        /// </summary>
        public class NetworkCredentialsHttpConnection : HttpConnection
        {
            protected override HttpMessageHandler CreateHttpClientHandler(RequestData requestData)
            {
                var handler = (HttpClientHandler)base.CreateHttpClientHandler(requestData);
                handler.UseDefaultCredentials = true;
                handler.PreAuthenticate = true;
                handler.ClientCertificateOptions = ClientCertificateOption.Automatic;
                return handler;
            }
        }

        /// <summary>
        /// ApiKeyCredentials Handler to generate a scoped API key to authenticate all requests for a connection
        /// </summary>
        public class ApiKeyCredentialsHttpConnection : HttpConnection
        {
            protected ApiKey apiKey;
            protected Func<ApiKey> requestNewApiKey;
            protected string indexPrefix;

            public ApiKeyCredentialsHttpConnection(ApiKey apiKey, Func<ApiKey> requestNewApiKey)
            {
                this.apiKey = apiKey;
                this.requestNewApiKey = requestNewApiKey;
            }

            protected override HttpRequestMessage CreateRequestMessage(RequestData requestData)
            {
                if (apiKey == null || DateTime.UtcNow > apiKey.ExpirationSunset)
                {
                    apiKey = requestNewApiKey();
                }
                var message = base.CreateRequestMessage(requestData);
                var b = Encoding.UTF8.GetBytes($"{apiKey.Id}:{apiKey.Secret}");
                var base64EncodedKey = Convert.ToBase64String(b);
                message.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", base64EncodedKey);
                return message;
            }
        }

        /// <summary>
        /// Log response information, optionally throwing on failed responses.
        /// </summary>
        /// <param name="iresponse">Elastic Response</param>
        /// <param name="throwOnError">Set to True to throw errors when response failed</param>
        public void WriteResponse(IResponse iresponse, bool throwOnError = false)
        {
            if (_il != null)
            {
                if (iresponse.IsValid)
                {
                    if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug)) _il.LogDebugInfo($"call: '{iresponse.ApiCall.Uri.AbsoluteUri}'");
                }
                else
                {
                    if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Warning))
                    {
                        _il.LogWarn($"call: '{iresponse.ApiCall.Uri.AbsoluteUri}' status:'{iresponse.ServerError?.Status.ToString() ?? "N/A"}' reason: '{iresponse.ServerError?.Error?.Reason ?? iresponse.OriginalException?.Message ?? "N/A"}'", "", iresponse.OriginalException);
                    }
                }
            }
            if (!iresponse.IsValid && throwOnError)
            {
                throw iresponse.OriginalException;
            }
        }



        public string GetClientStatus()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                var stats = client.Nodes.Stats();
                foreach (var item in stats?.Nodes?.Values)
                {
                    sb.AppendLine(item.Name);
                    sb.AppendLine("Roles");
                    foreach (var role in item.Roles)
                    {
                        sb.AppendLine(role.ToString());
                    }
                }
                sb.AppendLine("Failed:" + stats.NodeStatistics?.Failed);
                sb.AppendLine("Success:" + stats.NodeStatistics?.Successful);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error)) _il.LogErr("failed to get elastic client status", null, null, ex);
                return "Fail" + ex.Message;
            }
        }
        /// <summary>
        /// Utility method to pause the thread in the event that the cluster is returning too busy status codes.
        /// </summary>
        /// <param name="message"></param>
        internal void Pause(string message = "")
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(_random.Next(120, 1000));
            if (ilwarn) _il.LogWarn($"ClientPausing {message}", null, timeSpan.TotalSeconds);
            Thread.Sleep(timeSpan);
        }

        public ApiKey GetApiKey()
        {
            var settings = new ConnectionSettings(connectionPool, new NetworkCredentialsHttpConnection());
            settings.MemoryStreamFactory(Elasticsearch.Net.MemoryStreamFactory.Default); //recycle memorystream linked to mem leakage https://github.com/serilog/serilog-sinks-elasticsearch/issues/368
            ElasticClient apiKeyClient = new ElasticClient(settings);
            List<string> indexNames = new List<string>() { StaticIndexPrefix.Prefix + "*" };
            //uncomment this when we are ready to delete documents in elastic thru the alias...
            //var possibleIndexAliasNames = new string[] { DAL.Models.FSOdirectory.indexname, DAL.Models.FSOfile.indexname, DAL.Models.FSOdocument.indexname, DAL.Models.FSOemail.indexname };
            //foreach(var possibleIndexAlias in possibleIndexAliasNames)
            //{
            //    var concreteIndex = apiKeyClient.GetIndicesPointingToAlias(possibleIndexAlias);
            //    if (concreteIndex.Count > 0) indexNames.AddRange(concreteIndex);
            //}
            //end uncomment section

            CreateApiKeyResponse keyResponse = apiKeyClient.Security.CreateApiKeyAsync(
                (_v) => new CreateApiKeyRequest
                {
                    Name = apiKeyGuid,
                    Expiration = "1h",
                    Roles = new ApiKeyRoles
                           {
                               {
                                   "read-write-only", new ApiKeyRole
                                               {
                                                   Cluster = new [] { "all" },
                                                   Index = new []
                                                   {
                                                       new ApiKeyPrivileges
                                                       {
                                                           Names = indexNames,
                                                           //Privileges = new[] { "read", "write", "manage" }
                                                           Privileges = new []{ "all" }
                                                       }
                                                   }
                                               }
                               },
                           }
                }).GetAwaiter().GetResult();

            if (keyResponse.IsValid)
            {
                return new ApiKey
                {
                    Id = keyResponse.Id,
                    Secret = keyResponse.ApiKey,
                    Expiration = keyResponse.Expiration.Value,
                    ExpirationSunset = keyResponse.Expiration.Value.AddMinutes(-1)
                };
            }
            else
            {
                var err = ElasticResponseError.GetError(keyResponse);
                throw new HttpRequestException("Failed to generate token" + err.HttpStatusCode + err.ServerErrorReason + err.OriginalMessage + err.InnerMessage);
            }
        }


        public ApiKey GetApiKeyOld()
        {
            var settings = new ConnectionSettings(connectionPool, new NetworkCredentialsHttpConnection());
            settings.MemoryStreamFactory(Elasticsearch.Net.MemoryStreamFactory.Default); //recycle memorystream linked to mem leakage https://github.com/serilog/serilog-sinks-elasticsearch/issues/368
            ElasticClient apiKeyClient = new ElasticClient(settings);
            CreateApiKeyResponse keyResponse = apiKeyClient.Security.CreateApiKeyAsync(
                (_v) => new CreateApiKeyRequest
                {
                    Name = apiKeyGuid,
                    Expiration = "1h",
                    Roles = new ApiKeyRoles
                           {
                               {
                                   "read-write-only", new ApiKeyRole
                                               {
                                                   Cluster = new [] { "all" },
                                                   Index = new []
                                                   {
                                                       new ApiKeyPrivileges
                                                       {
                                                           Names = new [] { StaticIndexPrefix.Prefix + "*" },
                                                           //Privileges = new[] { "read", "write", "manage" }
                                                           Privileges = new []{ "all" }
                                                       }
                                                   }
                                               }
                               },
                           }
                }).GetAwaiter().GetResult();
            if (keyResponse.IsValid)
            {
                return new ApiKey
                {
                    Id = keyResponse.Id,
                    Secret = keyResponse.ApiKey,
                    Expiration = keyResponse.Expiration.Value,
                    ExpirationSunset = keyResponse.Expiration.Value.AddMinutes(-1)
                };
            }
            else
            {
                var err = ElasticResponseError.GetError(keyResponse);
                throw new HttpRequestException("Failed to generate token" + err.HttpStatusCode + err.ServerErrorReason + err.OriginalMessage + err.InnerMessage);
            }
        }
        public bool InvalidateApiKey(string apiGuid)
        {
            var invalidateResponse = client.Security.InvalidateApiKeyAsync(new InvalidateApiKeyRequest { Name = apiGuid }).GetAwaiter().GetResult();
            if (!invalidateResponse.IsValid)
            {
                _il.LogWarn("Unable to invalidate API key");
                return false;
            }
            _il.LogDebug("Invalidated API key");
            return true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    InvalidateApiKey(apiKeyGuid);
                    client.ConnectionSettings.Connection.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
