using Elasticsearch.Net;
using HOK.Elastic.DAL.Models;
using Nest;
using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;



namespace HOK.Elastic.DAL
{

    public class ElasticResponseError
    {
        private static readonly Regex ExtractStatusCode = new Regex(@"^(.*?)\.\sCall\:\sStatus\scode\s(\d{1,3})", RegexOptions.IgnoreCase);
        public string OriginalMessage { get; set; }
        /// <summary>
        /// Truncated to 300chars
        /// </summary>
        public string ServerErrorReason { get; set; }
        public int? HttpStatusCode { get; set; }
        public string InnerMessage { get; set; }
        public string Type { get; set; }
        [JsonIgnore]
        public Exception? Exception { get; set; }
        private ElasticResponseError() { }


        public ElasticResponseError(Nest.BulkResponseItemBase ir)
        {
            OriginalMessage = ir.Result;

            if (ir.Error != null)
            {
                InnerMessage = ir.Error.Reason;
                if (ir.Error.RootCause.Any())
                {
                    InnerMessage = InnerMessage + string.Join(";", ir.Error.RootCause);
                }
                Type = ir.Error.Type;
            }
            HttpStatusCode = ir.Status;//I suspect this isn't correct but let's try it for now.

            //don't populate the exception otherwise we will easily trigger a toomanyexceptions limit cancellation. 
        }
        public ElasticResponseError(IResponse ir)
        {
            if (ir.ServerError != null)
            {
                HttpStatusCode = ir.ServerError.Status;
                Type = ir.ServerError.Error?.Type ?? "default";
                ServerErrorReason = new string(ir.ServerError.Error?.Reason?.Take(300).ToArray());
            }
            if (ir.OriginalException != null)
            {
                Exception = ir.OriginalException;
                OriginalMessage = ir.OriginalException.Message;
                InnerMessage = ir.OriginalException?.InnerException?.Message;
            }
            else
            {
                Exception = new ElasticsearchClientException("nooriginalexception");
            }
            if (ir.ServerError == null && OriginalMessage != null)
            {
                //while they addressed the issue it looks like atleast this 
                //https://github.com/elastic/elasticsearch/issues/2902
                var match = ExtractStatusCode.Match(OriginalMessage);
                if (match.Success)
                {
                    HttpStatusCode = Convert.ToInt32(match.Groups[2].Value);
                    ServerErrorReason += match.Groups[1].Value;
                    Type = "Extracted Server Error";
                }
            }
        }
        //ROUP\elastic.crawler2024-05-08 14:05:24,141 [96] ERROR Default.Index [(null)] -
        //{"message":"Index.Delete",
        //"json":
        //{
        //"OriginalMessage":"Request failed to execute. Call: Status code 409 from: POST /hok.fs.can.projs.v3.fsodoc/_delete_by_query",
        //"ServerErrorReason":"Request failed to execute",
        //"HttpStatusCode":409,
        //"InnerMessage":null,
        //"Type":"Extracted Server Error"
        //}}

        public static ElasticResponseError GetError(IResponse ir)
        {
            return new ElasticResponseError(ir);
        }


        public bool IsBecauseBusy()
        {
            if (HttpStatusCode == 429 || HttpStatusCode == 500 || HttpStatusCode == 503)  //429(too many requests) or 500(Internal Server Error) we associate with elastic cluster being in a degraded state and so we will pause for a lengthy period of time and hope the cluster recovers.TODO chane to async.
            {
                return true;
            }
            else return false;
        }
    }
}








