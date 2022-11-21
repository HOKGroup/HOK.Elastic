using HOK.Elastic.FileSystemCrawler.Models;
using Nest;
using Newtonsoft.Json;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{

    //https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-6.0&tabs=visual-studio
    public class HostedJobInfo
    {
        private static SemaphoreSlim _lock = new SemaphoreSlim(1,1);
        private static int _uid = 0;
        private int _id;
        private ISettingsJobArgs _job;
        private CancellationTokenSource _thisTokenSource;
        private CancellationTokenSource _linkedTokenSource;
 
        public HostedJobInfo(ISettingsJobArgs settingsJob, CancellationToken cancellationToken)
        {
            Status = State.unstarted;
            try
            {
               if( _lock.Wait(500, cancellationToken))
                {
                    _id = _uid++;
                }
            }finally
            {
                _lock.Release();
            } 
        
            _job = settingsJob;
            _thisTokenSource = new CancellationTokenSource();
            _linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _thisTokenSource.Token);
#if DEBUG
            _linkedTokenSource.CancelAfter(60000);//just for fun to give a sample of jobs running.
#endif
            WhenCreated = DateTime.Now;
        }
        /// <summary>
        /// Detault Constructor called when deserializing.
        /// </summary>
        public HostedJobInfo()
        {
            _thisTokenSource = new CancellationTokenSource();
            _linkedTokenSource = new CancellationTokenSource();
        }
  
        public bool IsCompleted => Status >= State.complete;
        public DateTime? WhenCreated { get; set; }
        public DateTime? WhenCompleted { get; set; }
        public ISettingsJobArgs SettingsJobArgs => _job;
        public int Id => _id;
        public CompletionInfo CompletionInfo { get; set; }

        public State Status { get; set; }
        public enum State
        {
            unstarted,
            started,
            cancelled,
            complete,
            completedWithException
        }

        private Exception? _exception;
        public bool HasException => _exception != null;
        public Exception GetException() { return _exception; }       
        public Exception Exception { set { _exception = value; } }
        public override string ToString()
        {
            var json = JsonConvert.SerializeObject(this);
            return json;
        }
        public CancellationToken GetCancellationToken() => _linkedTokenSource.Token;
        internal void Cancel()
        {
            _thisTokenSource.Cancel();
        }
    }
}
