using HOK.Elastic.FileSystemCrawler.Models;
using Nest;
using Newtonsoft.Json;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;

namespace HOK.Elastic.FileSystemCrawler.WebAPI.DAL.Models
{

    //https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-6.0&tabs=visual-studio
    public class HostedJobInfo
    {
        private CancellationTokenSource _thisTokenSource;
        private CancellationTokenSource _linkedTokenSource;
        public int Id { get; set; }
        public DateTime? WhenCreated { get; set; }
        public DateTime? WhenCompleted { get; set; }
        public SettingsJobArgsDTO SettingsJobArgsDTO { get; set; }
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
        public bool IsCompleted => Status >= State.complete;
        private Exception _exception;
        public bool HasException => _exception != null;
        public Exception GetException() { return _exception; }
        public Exception Exception { set { _exception = value; } }

        /// <summary>
        /// Detault Constructor called when deserializing.
        /// </summary>
        public HostedJobInfo()
        {
            _thisTokenSource = new CancellationTokenSource();
            _linkedTokenSource = new CancellationTokenSource();
        }
        public HostedJobInfo(SettingsJobArgsDTO settingsJob, CancellationToken cancellationToken)
        {

            Status = State.unstarted;
            SettingsJobArgsDTO = settingsJob;
            _thisTokenSource = new CancellationTokenSource();
            _linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _thisTokenSource.Token);
#if DEBUG
            _linkedTokenSource.CancelAfter(120000);//just for fun to give a sample of jobs running.
#endif
            WhenCreated = DateTime.Now;
        }

        public override string ToString()
        {
            var json = JsonConvert.SerializeObject(this,Formatting.Indented);
            return json;
        }
        public CancellationToken GetCancellationToken() => _linkedTokenSource.Token;
        public void Cancel()
        {
            _thisTokenSource.Cancel();
            Status = State.cancelled;
        }
    }
}
