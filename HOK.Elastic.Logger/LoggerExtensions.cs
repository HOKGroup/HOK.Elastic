using HOK.Elastic.Logger;
using Newtonsoft.Json;
using System;
using System.IO;

namespace Microsoft.Extensions.Logging
{
    public static partial class LoggerExtensions
    {
        private static JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings()
        {
            ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore
            ,DefaultValueHandling=DefaultValueHandling.Include,//crawl metric logs entries didn't include 'FileCount' property when default/0 when DefaultValueHandling.Ignore.
            NullValueHandling=NullValueHandling.Ignore,
            Formatting = Formatting.Indented

        };
        public static string GetJson(string text, string path = "", object data = null, Exception ex = null)
        {
            StringWriter sw = new StringWriter();
            JsonTextWriter writer = new JsonTextWriter(sw);

            writer.WriteStartObject();
            writer.WritePropertyName("message");
            writer.WriteValue(text);
            if (path != null)
            {
                writer.WritePropertyName("path");
                writer.WriteValue(path);
            }
            if (data != null)
            {
                var type = data.GetType();
                if (data is string || type.IsPrimitive)
                {
                    writer.WritePropertyName(type.Name.ToLowerInvariant());
                    writer.WriteValue(data);
                }
                else
                {
                    writer.WritePropertyName("json");
                    writer.WriteRawValue(JsonConvert.SerializeObject(data, jsonSerializerSettings));
                }
            }
            if (ex != null)
            {
                writer.WritePropertyName("exception");
                var exs = ExceptionSerializable.Get(ex);
                writer.WriteRawValue(JsonConvert.SerializeObject(exs, jsonSerializerSettings));
            }
            writer.WriteEndObject();
            return sw.ToString();
        }
       

        public static void LogDebugInfo(this ILogger log, string text, string path = "", object data = null)
        {
            log.LogDebug(GetJson(text, path, data));
        }

        public static void LogInfo(this ILogger log, string text, string path = "", object data = null)
        {
            log.LogInformation(GetJson(text, path, data));
        }

        public static void LogWarn(this ILogger log, string text, string path = "", object data = null)
        {
            log.LogWarning(GetJson(text, path, data));
        }
        public static void LogErr(this ILogger log, string text, string path = "", object data = null, Exception ex = null)
        {           
            log.LogError(GetJson(text, path, data, ex));
            ExceptionRateLimiter.HasRateLimitExceeded(ex);
        }
        public static void LogFatal(this ILogger log, string text, string path = "", object data = null, Exception ex = null)
        {
            log.LogCritical(GetJson(text, path, data, ex));
            ExceptionRateLimiter.HasRateLimitExceeded(ex);
        }
    }
}
