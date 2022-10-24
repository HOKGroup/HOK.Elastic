using HOK.Elastic.DAL.Models;
using HOK.Elastic.FileSystemCrawler.Models;
using HOK.Elastic.Logger;
using Microsoft.Extensions.Logging;
using RestSharp;
using RestSharp.Authenticators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static HOK.Elastic.FileSystemCrawler.WorkerBase;

namespace HOK.Elastic.FileSystemCrawler
{
    public class EventStreamClient
    {
        private HOK.Elastic.Logger.Log4NetLogger _il;

        protected class ODataCollectionWrapper<T> where T : class
        {
            public IEnumerable<T> Value { get; set; }
        }
        public EventStreamClient(Uri eventStreamAPIEndPoint, Log4NetLogger logger)
        {
            _il = logger;
            this.EventStreamEndPoint = eventStreamAPIEndPoint;
        }
        public Uri EventStreamEndPoint { get; private set; }

        /// <summary>
        /// This is the loop that will sit and run and check for work from the audit stream/log system. Maybe later we'll look at Elastic, or the Nasuni API directly. But for now this will be fine.
        /// Basically loop around and check a REST api for some events. We will request a block of events so that other crawlers(if deployed) can share the load.
        /// </summary>
        /// <param name="ct">CancellationToken so that we may cancel the process if desired</param>
        /// <param name="thetaskInfo">Initiation information to do the job</param>
        /// <returns></returns>
        public async Task<CompletionInfo> ProcessEventsAsync(ISettingsJobArgs thetaskInfo, WorkerEventStream workerEventStream, CancellationToken ct)
        {
            var totalCompletionInfo = new CompletionInfo(thetaskInfo);
            totalCompletionInfo.InputPaths?.Clear();//we don't need to report back on the inputpaths in the final totals.
            while (!ct.IsCancellationRequested)
            {
                IEnumerable<InputPathEventStream> httpRESTResponseFromService = await GetEvents(@"\\?\\Internal\", PathHelper.CrawlRoot).ConfigureAwait(false);//todo this will have to be some kind of variable I think...unless every site is \now\internal

                if (httpRESTResponseFromService == null || !httpRESTResponseFromService.Any())
                {
                    if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug)) _il.LogDebugInfo("No API items returned...", null, null);
#if DEBUG
                    await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
#else
                    await Task.Delay(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
#endif
                }
                else
                {
                    if (thetaskInfo.InputPaths == null)
                    {
                        thetaskInfo.InputPaths = new InputPathCollectionEventStream();// List<InputPathBase>();
                    }
                    else
                    {
                        thetaskInfo.InputPaths.Clear();
                    }
                    foreach (var item in httpRESTResponseFromService)
                    {
                        thetaskInfo.InputPaths.Add(item);
                    }
                    if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug)) _il.LogDebugInfo($"Found {thetaskInfo.InputPaths.Count} work items", "", null);
                    CompletionInfo iterationCompletionInfo = await workerEventStream.RunAsync(thetaskInfo).ConfigureAwait(false);
                    totalCompletionInfo.Deleted += iterationCompletionInfo.Deleted;
                    totalCompletionInfo.DirCount += iterationCompletionInfo.DirCount;
                    totalCompletionInfo.FileCount += iterationCompletionInfo.FileCount;
                    totalCompletionInfo.FileSkipped += iterationCompletionInfo.FileSkipped;
                    totalCompletionInfo.FileNotFound += iterationCompletionInfo.FileNotFound;
                    if (_il.IsEnabled(LogLevel.Information))
                    {
                        _il.LogInfo("Iteration SubTotal", "", iterationCompletionInfo);
                        _il.LogInfo("Iteration Total", "", totalCompletionInfo);
                    }
                }
            }
            return totalCompletionInfo;
        }

        /// <summary>
        /// //go to the api and request a block of 400 entries for example.
        /// </summary>
        /// <param name="find">paths coming from event system may need to have path substitutions made to make referencing possible</param>
        /// <param name="replace">replacement token</param>
        /// <returns>List of Actionable Events; It could be null if no events.</returns>
        private async Task<IEnumerable<InputPathEventStream>> GetEvents(string find, string replace)
        {
            //var samplepath = @"\now\Internal\site\DEPTS\department\Software Development\Elastic";
            //samplepath = samplepath.Replace(find, replace);
            IEnumerable<InputPathEventStream> httpResponsePaths = null;
            var client = new RestSharp.RestClient(this.EventStreamEndPoint);
            client.Authenticator = new NtlmAuthenticator(System.Net.CredentialCache.DefaultNetworkCredentials);
            RestRequest request = new RestRequest("auditevents?$top=75", Method.GET);//we can adjust how many items to take, the less items the more likely the paths will be 'more' accurate/timely...
            request.OnBeforeDeserialization = resp => { resp.ContentType = "application/json"; };
            try
            {
                var response = await client.ExecuteAsync<ODataCollectionWrapper<InputPathEventStream>>(request).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    httpResponsePaths = response.Data.Value;
                    if (httpResponsePaths != null)
                    {
                        int replacementlength = find.Length;//here's where we substitute the path from eventStream for example \now\internal\projects ...to what we want \\domain\fileroot\projects etc.
                        if(replacementlength>0)
                        {
                            foreach (var item in httpResponsePaths)
                            {
                                item.Path = replace + item.Path.Substring(replacementlength);
                                if (item.PathFrom != null)
                                {
                                    item.PathFrom = replace + item.PathFrom.Substring(replacementlength);
                                }
                            }
                        }
                    }
                    return httpResponsePaths;
                }
                else
                {
                    if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error)) _il.LogErr(response.StatusDescription, "", null, response.ErrorException);
                }
                ///Summary:
                ///build a list of paths and actions either externally or via a pre-processing step. The input for the next step should just be: 
                ///Delete (my action remove from index)0
                ///Set Security (my action read metadata)1
                ///Write (my action read content and metadata)2
                ///Rename/Move (my action reindex item with new id) //only happens when newpath and oldpath are interesting.
                /////Rename unless both paths are interesting becomes two independant items: A Delete and a Write. We only care about Renames that newpath is interesting. 
                ///So Rename from xls to tmp....delete is ignored, write is ignored as newpath ext is excluded....or maybe we just log a delete event and let the write event below 'cancel' it out.
                ///So Rename from tmp to xls, delete is ignored as path is excluded. write is interesting though. And so becomes a write event as shown above (read content + metadata)
                ///so as you look at the logs, when you encounter a rename, if both paths are interesting, request a move, otherwise, if the currentpath is excluded but newpath isn't request a write newpath, if the currentpath is good, but newpath is bad request a delete of currentpath (to be canceled out later possibly)
            }
            catch (Exception ex)
            {
                if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error)) _il.LogErr(ex.Message, "", request.ToString(), ex);
            }
            return null;
        }
    }
}
