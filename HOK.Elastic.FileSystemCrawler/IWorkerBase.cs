using HOK.Elastic.DAL;
using HOK.Elastic.FileSystemCrawler.Models;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
namespace HOK.Elastic.FileSystemCrawler
{
    public interface IWorkerBase
    {
        SecurityHelper SecurityHelper { get; }
        DocumentHelper DocumentHelper { get; }
        CompletionInfo Run(ISettingsJobArgs args, CancellationToken ct = default);
        Task<CompletionInfo> RunAsync(ISettingsJobArgs args, CancellationToken ct = default);
    }
}
