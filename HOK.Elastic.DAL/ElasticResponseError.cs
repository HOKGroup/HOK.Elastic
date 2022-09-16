using Nest;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace HOK.Elastic.DAL
{
         public class ElasticResponseError
        {    
            public string OriginalMessage { get; set; }
            /// <summary>
            /// Truncated to 300chars
            /// </summary>
            public string ServerErrorReason { get; set; }
            public int? HttpStatusCode { get; set; }
            public string InnerMessage { get; set; }
            public string Type { get; set; }
            public static ElasticResponseError GetError(IResponse ir)
            {
                var ire = new ElasticResponseError()
                {
                    HttpStatusCode = ir.ServerError?.Status,
                    OriginalMessage = ir.OriginalException?.Message,
                    InnerMessage = ir.OriginalException?.InnerException?.Message,
                    Type = ir.ServerError?.Error.Type,
                    ServerErrorReason = new string(ir.ServerError?.Error?.Reason?.Take(300).ToArray())

                };
                if (!ire.HttpStatusCode.HasValue && ire.OriginalMessage != null)
                {
                    //while they addressed the issue it looks like atleast this 
                    //https://github.com/elastic/elasticsearch/issues/2902
                    var match = ExtractStatusCode.Match(ire.OriginalMessage);
                    if (match.Success)
                    {
                        ire.HttpStatusCode = Convert.ToInt32(match.Groups[2].Value);
                        ire.ServerErrorReason += match.Groups[1].Value;
                        ire.Type = "Extracted Server Error";
                    }
                }
                return ire;
            }
        public bool IsBecauseBusy()
        {
            if (HttpStatusCode == 429 || HttpStatusCode == 500 || HttpStatusCode == 503)  //429(too many requests) or 500(Internal Server Error) we associate with elastic cluster being in a degraded state and so we will pause for a lengthy period of time and hope the cluster recovers.TODO chane to async.
            {
                return true;
            }
            else return false;
        }
        private static readonly Regex ExtractStatusCode = new Regex(@"^(.*?)\.\sCall\:\sStatus\scode\s(\d{1,3})",RegexOptions.IgnoreCase);
    }
}








