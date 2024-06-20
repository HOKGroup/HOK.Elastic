using System;
using System.Collections.Generic;

namespace HOK.Elastic.Logger
{
    public class ExceptionSerializable
    {
        public string Message { get; set; }
        public string StackTrace { get; set; }
        public string Type { get; set; }
        public List<ExceptionSerializable> InnerExceptions { get; set; } = new List<ExceptionSerializable>();
        public ExceptionSerializable() { }
        public ExceptionSerializable(Exception exception)
        {
            if (exception != null)
            {
                Message = exception.Message;
                StackTrace = exception.StackTrace;
                Type = exception.GetType().Name;
                if (exception.InnerException != null)
                {
                   InnerExceptions.Add(new (exception.InnerException));
                }
                if (exception is AggregateException)
                {
                    var ae = exception as AggregateException;
                    foreach (var e in ae.InnerExceptions)
                    {
                        InnerExceptions.Add(new(e));
                    }
                    //Aggregate Exception Mssages often look like: "One or more errors occurred. (Unexpected Query ......(......(.....(.....(
                    //let's strip out the innerexception messages as they are included in the innerexceptions list and will be available for reading there.
                    var endOfRootMessage = Message.IndexOf('(');
                    if(endOfRootMessage>0)
                    {
                        Message = Message.Substring(0,endOfRootMessage).Trim();
                    }
                }
            }
        }        
        public static ExceptionSerializable Get(Exception exception)
        {
            return new ExceptionSerializable(exception);
        }
    }
}
