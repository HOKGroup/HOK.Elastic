using HOK.Elastic.FileSystemCrawler.Models;
//using Microsoft.AspNetCore.Http;
//using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Nest;
using System.Net;
using System;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Principal;
using HOK.Elastic.FileSystemCrawler.WebAPI.DAL.Models;

namespace HOK.Elastic.ArchiveDiscovery
{
    internal class APIClient
    {
        private Logger.Log4NetLogger _il;
        private bool ilDebug;
        private bool ilWarn;
        private static HttpClient httpClient;
        private string host;
        private const string JOBSAPI = @"/jobsapi";
        private readonly string freeSlotpath;
        public APIClient(string hostAddress, Logger.Log4NetLogger log4NetLogger)
        {
            _il = log4NetLogger;
            ilDebug = _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug);
            ilWarn = _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Warning);
            host = hostAddress.Trim('/');
            freeSlotpath = @$"{hostAddress}{JOBSAPI}/freeslots";

            //https://weblog.west-wind.com/posts/2021/Nov/27/NTLM-Windows-Authentication-Authentication-with-HttpClient
            // Create a new Credential - note NTLM is not documented but works
            var credentialsCache = new CredentialCache();
            credentialsCache.Add(new Uri(host), "NTLM", CredentialCache.DefaultNetworkCredentials);
            credentialsCache.Add(new Uri(host), "Negotiate", CredentialCache.DefaultNetworkCredentials);            
            var handler = new HttpClientHandler() { Credentials = credentialsCache, PreAuthenticate = true };
            httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30)};
        }

        internal async Task<bool> HasFreeSlotsAsync()
        {
            var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, freeSlotpath));
            if (response.IsSuccessStatusCode)
            {
                var freeSlots = await response.Content.ReadFromJsonAsync<int>();
                return freeSlots > 0;
            }
            else
            {
                if (ilWarn) _il.LogWarn("Get Free Slots Failed", null, response);
            }
            return false;
        }

        internal async Task<int> PostAsync(SettingsJobArgsDTO settingsJobArgsDTO)
        {
            var path = @$"{host}{JOBSAPI}";
            var response = await httpClient.PostAsJsonAsync<SettingsJobArgsDTO>(path, settingsJobArgsDTO);
            response.EnsureSuccessStatusCode();
            var jobId = await response.Content.ReadFromJsonAsync<int>();
            return jobId;
        }

        internal async Task<HostedJobInfo> GetJobInfo(int taskId)
        {
            //https://localhost:44346/jobs/0
            var path = @$"{host}{JOBSAPI}/{taskId}";
            var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, path));
            if(response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<HostedJobInfo>();
                if (result != null)
                {
                    if (ilDebug) _il.LogDebugInfo("Retrieved task info", null, result);
                    return result;
                }
            }
                if (ilWarn) _il.LogWarn($"Couldn't get{nameof(HostedJobInfo)} for {taskId}", null, response.StatusCode);
            return null;
        }



        internal async Task DeleteAsync(int taskId)
        {
            var path = @$"{host}{JOBSAPI}/{taskId}";
            try
            {
                var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, path));
                response.EnsureSuccessStatusCode();
            }
            catch (Exception e)
            {
                if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error))
                {
                    _il.LogErr("Couldn't delete", null, taskId, e);
                }
                throw;
            }
        }
    }
}
