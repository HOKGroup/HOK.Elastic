using HOK.Elastic.FileSystemCrawler.Models;
using HOK.NasuniAuditEventAPI.DAL;
//using Microsoft.AspNet.OData;
//using Microsoft.AspNet.OData.Query;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace HOK.NasuniAuditEventAPI.Controllers
{
    [Authorize(Roles = "HOK Elastic Crawler Admins")]
    public class AuditEventsController : ODataController
    {
        DAL.NasuniEventReader _auditLogHostedService;

        private readonly ILogger<AuditEventsController> _logger;

        public AuditEventsController(DAL.NasuniEventReader nasuniEventStreamReader, ILogger<AuditEventsController> logger)
        {
            _auditLogHostedService = nasuniEventStreamReader;
            _logger = logger;
        }

        /// <summary>
        /// Allow querying, but not sorting or filtering? (TODO maybe filter is OK) return the results to the client and then remove these items from the dictionary.
        /// There are problems with this naive approach - namely if multiple clients access this API, another client may request new items, which will write new keys, and therefore we shouldn't be allowed to remove them from the list. 
        ///http://localhost:5000/api/auditevents?$top=70
        /// </summary>
        /// <param name="queryOptions">?$top=5$Select=username</param>
        /// <returns></returns>

        [HttpGet]
        [EnableQuery(AllowedQueryOptions = Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Top | AllowedQueryOptions.Select)]

        public async Task<IEnumerable<InputPathEventStream>> GetAsync(ODataQueryOptions<InputPathEventStream> queryOptions)
        {
            return await GetEvents(queryOptions, true);
        }

        [HttpGet("API/Peek")]

        [EnableQuery(AllowedQueryOptions = Microsoft.AspNetCore.OData.Query.AllowedQueryOptions.Top | AllowedQueryOptions.Select)]

        public async Task<IEnumerable<InputPathEventStream>> PeekAsync(ODataQueryOptions<InputPathEventStream> queryOptions)//change ienumerable but not sure if will still work.
        {
            return await GetEvents(queryOptions, false);
        }


        private async Task<IEnumerable<InputPathEventStream>> GetEvents(ODataQueryOptions<InputPathEventStream> queryOptions, bool removewhenCompleted = false)
        {
            var events = await _auditLogHostedService.GetTopEvents(queryOptions.Top?.Value ?? 1);
            var listOfItemsToRemove = events.Select(x => x.Path).ToList();
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebugInfo($"{HttpContext.Connection.RemoteIpAddress} Fetching Top: {queryOptions?.Top?.RawValue}", null, string.Join(";", listOfItemsToRemove));
            }
            else if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInfo($"{HttpContext.Connection.RemoteIpAddress} Fetching Top: {queryOptions?.Top?.RawValue}", null);
            }
            if (removewhenCompleted)
            {
                this.HttpContext.Response.OnCompleted(async () =>
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebugInfo("Removing from Queue", null, string.Join(";", listOfItemsToRemove.Select(x => System.IO.Path.GetFileName(x))));
                    }
                    if (listOfItemsToRemove.Count > 0)
                    {
                        await _auditLogHostedService.RemoveAsync(listOfItemsToRemove);//comment this out so that we never consume the events while testing...
                    }
                });
            }
            return events;
        }

        [HttpPut]
        public ActionResult PutPriorityEvent(InputPathEventStream inputPathEventStream)
        {
            _auditLogHostedService.PutPriorityEvent(inputPathEventStream);
            return null;
        }
    }
}