using HOK.Elastic.FileSystemCrawler.Models;
using HOK.NasuniAuditEventAPI.DAL.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HOK.NasuniAuditEventAPI.DAL
{
    public sealed class NasuniEventReader : IDisposable // : IAuditLogHostedService,IHostedService
    {
        public const int MaximumNumberOfItemsAllowedToRequestHardCoded = 1000;//TODO set this to some reasonable number or change from const to a formula of available RAM etc.
        private int _maxN;
        private int _minimumAgeMinutes;
        private readonly InputPathCollectionEventStream _inputPaths;
        private static Dictionary<string, long> logsWeHaveRead = new Dictionary<string, long>();
        private SemaphoreSlim _sync = new SemaphoreSlim(1,1);
        private bool disposedValue;
        private readonly ILogger<NasuniEventReader> _logger;
        private Task _task;

        public int EventQueueLength { get { return _inputPaths.Count; } }
        public int EventDuplicates { get { return _inputPaths.DuplicatesCount; } }
        public int EventsConsumed { get; private set; }
        public int PathSkipped { get; private set; }
        public int MaxItemsToReturn { get => _maxN; }
        public int MinageInMinutes { get => _minimumAgeMinutes; }
        public List<string> LogsRead { get=> logsWeHaveRead.Keys.ToList(); }
        public string FolderTowatch { get; private set; }

        public TimeSpan MinimumAgeOfLogsToRead
        {
            //Always re-add a specific number of hours worth of events (regardless if they have already been read during a previous instance))
            //Because MinageInMinutes prevents delays events from publishing to the crawler, after a restart of the API, we want to re-read all those logs (plus some additional logs for good measure)
            get
            {
#if DEBUG
                return TimeSpan.FromMinutes(0);
#endif
                if (MinageInMinutes <= 240) return TimeSpan.FromHours(12);
                else if (MinageInMinutes <= 540) return TimeSpan.FromDays(1);
                else if (MinageInMinutes <= 720) return TimeSpan.FromDays(2);
                else return TimeSpan.FromDays(3);
            }
        }

        public NasuniEventReader(ILogger<NasuniEventReader> logger, string folderToWatch, int maxItemsPerRequest, int minAgeInMinutes)
        {
            _maxN = maxItemsPerRequest;
            _minimumAgeMinutes = minAgeInMinutes;
            FolderTowatch = folderToWatch;
            _inputPaths = new InputPathCollectionEventStream()
            {
                //These properties aren't referenced? ///TODO verify if true and possibly simplify the class if there will never be a use-case for these properties.
                PublishedPath = null,
                PathForCrawling = null,
                PathForCrawlingContent = null,
            };

            _logger = logger;
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInfo("tailing logs", FolderTowatch, _inputPaths);
                _logger.LogInfo(string.Format("events must be atleast {0} minutes old before they are returned in the api.", _minimumAgeMinutes));
            }
        }


        public Task Start(CancellationToken cts = default)
        {
            _task = WatchLoop(cts);
            _task.Start();
            return _task;
        }

        public Task WatchLoop(CancellationToken cts)
        {
            return new Task(async () =>
            {
                while (true)
                {
                    try
                    {
                        cts.ThrowIfCancellationRequested();
                        await ReadLogsInDirectoryAsync(FolderTowatch).ConfigureAwait(false);
                        await Task.Delay(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException)
                        {
                            if (_logger.IsEnabled(LogLevel.Information))
                            {
                                _logger.LogWarn($"{nameof(NasuniEventReader)} Cancelled.");
                                break;
                            }
                        }
                        if (_logger.IsEnabled(LogLevel.Error))
                        {
                            _logger.LogErr("Error watching logs", FolderTowatch, null, ex);
                        }
                    }
                }
            });
        }


        public async Task<List<InputPathEventStream>> GetTopEvents(int top)
        {
            var datecutoff = DateTime.UtcNow - TimeSpan.FromMinutes(Math.Max(1, _minimumAgeMinutes));
            await _sync.WaitAsync().ConfigureAwait(false);
            try
            {
                var itemsToReturn = _inputPaths.Query.OrderBy(x => x.TimeStampUtc).Where(x => x.TimeStampUtc < datecutoff).Take(top).ToList();//get oldest items in the queue that are within the time cutoff.
                EventsConsumed += itemsToReturn.Count;
                return itemsToReturn;
            }
            finally
            {
                _sync.Release();
            }
        }
        /// <summary>
        /// After a web request, we want to remove those items from the InputPathCollection so that the paths aren't handed out more than once.
        /// </summary>
        /// <param name="listOfItemsToRemove"></param>
        /// <returns></returns>
        public async Task RemoveAsync(List<string> listOfItemsToRemove)
        {
            await _sync.WaitAsync();
            try
            {
                foreach (var item in listOfItemsToRemove)
                {
                    _inputPaths.RemoveAsLowerCase(item);
                }
            }
            finally
            {
                _sync.Release();
            }
        }

        /// <summary>
        /// Called by Watch method loop.
        /// </summary>
        /// <param name="pathToMonitor"></param>
        /// <returns></returns>
        private async Task ReadLogsInDirectoryAsync(string pathToMonitor)
        {
            ///todo - we need to persist where we are with the logs...
            /////we need to read the events from oldest to newest so that if we have some old filepaths affected by a newer event (a rename of parent folder for example) the paths are already registred and can be updated.
            DirectoryInfo di = new DirectoryInfo(pathToMonitor);
            bool fileWasRead = false;
            // MinageInMinutes
            var files = di.EnumerateFiles("*.log").Where(x => x.Attributes.HasFlag(FileAttributes.Archive) || x.LastWriteTimeUtc > DateTime.Now.Subtract(MinimumAgeOfLogsToRead)).OrderBy(x => x.LastWriteTimeUtc).ToList();//only look at one file for now.
            foreach (var fileinfo in files)
            {
                try
                {
                    fileWasRead = logsWeHaveRead.ContainsKey(fileinfo.Name);
                    if (fileWasRead)
                    {
                        if (logsWeHaveRead[fileinfo.Name] == fileinfo.Length)
                        {
                            fileinfo.Attributes &= ~FileAttributes.Archive;
                            //skip. We have already read this file and the length hasn't changed.
                        }
                        else
                        {
                            //read the file at pos calculated by length minus last cursor position.
                            var pos = Math.Max(0, fileinfo.Length - logsWeHaveRead[fileinfo.Name]);
                            await TailFileAsync(fileinfo.FullName, pos);
                            logsWeHaveRead[fileinfo.Name] = fileinfo.Length;
                        }
                    }
                    else
                    {
                        await TailFileAsync(fileinfo.FullName, (int)fileinfo.Length);
                        logsWeHaveRead.Add(fileinfo.Name, (int)fileinfo.Length);
                    }
                }
                catch (Exception ex)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogErr("Error reading logs", FolderTowatch, null, ex);
                    }
                }
            }
        }


        private async Task TailFileAsync(string filename, long offsetFromEnd)
        {
            const Int32 BufferSize = 4096;//TOOD set offset.
            using (FileStream fs = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(-offsetFromEnd, SeekOrigin.End);
                using (var streamReader = new StreamReader(fs, Encoding.UTF8, true, BufferSize))
                {
                    String line;
                    int buffercounter = 0;
                    InputPathEventStream[] eventsBuffer = new InputPathEventStream[50];
                    while ((line = await streamReader.ReadLineAsync()) != null)
                    {
                        if (line.StartsWith("{"))
                        {
                            try
                            {
                                var nasuniAuditRecord = JsonConvert.DeserializeObject<NasuniEventRecord>(line);
                                if (nasuniAuditRecord == null) continue;
                                //we shouldn't filter out any bad paths here as bad a path may be relevant.
                                //goodpath to badpath =(keep) effectively delete goodpath location...unless a subsequent badpath to goodpath event happens (renaming the temp file back to the original file for example)..therefore we need to keep this event.
                                //badpath to goodpath =(keep) we need to read the file
                                //badpath to null =(discard) we can ignore it
                                //badpath to badpath =(discard) we can ignore it.
                                if (nasuniAuditRecord.is_dir ?
                                    (HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreDirectory(nasuniAuditRecord.path) == false || HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreDirectory(nasuniAuditRecord.newpath) == false) :
                                    (HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreFile(nasuniAuditRecord.path) == false || HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreFile(nasuniAuditRecord.newpath) == false)
                                    )
                                {
                                    InputPathEventStream pathToCrawl = CastAuditStreamInputToInputPathAuditObject(nasuniAuditRecord);
                                    if (pathToCrawl != null)
                                    {
                                        eventsBuffer[buffercounter] = pathToCrawl;
                                        buffercounter++;
                                    }
                                    else
                                    {

                                    }
                                }
                                else
                                {
                                    if (_logger.IsEnabled(LogLevel.Debug))
                                    {
                                        _logger.LogDebugInfo("skipping", nasuniAuditRecord.path, line);
                                    }
                                    PathSkipped++;
                                }
                            }
                            catch (Exception ex)//TODO work on the json conversion of dates.
                            {
                                if (_logger.IsEnabled(LogLevel.Error))
                                {
                                    _logger.LogErr("Error deserializing json", "", line, ex);
                                };
                            }
                        }
                        //write out the buffered content if the buffer is full
                        if (buffercounter >= eventsBuffer.Length)
                        {
                            await _sync.WaitAsync();
                            try
                            {
                                _inputPaths.Add(eventsBuffer);
                                buffercounter = 0;
                            }
                            finally
                            {
                                _sync.Release();
                            }
                        }
                    }
                    //write out any unwritten items from the buffer (buffercount<buffersize)
                    //It is not safe to re-add items that might have been added above inside the while loop
                    //(for example elements [48,49,50] if the last loop only had 47 items in the buffer as the timestamps could be identical for multiple records)
                    //We assume that while records may identical timestamps, the most recently read line from the log is newest.
                    await _sync.WaitAsync();
                    for (int i = 0; i < buffercounter; i++)
                    {
                        _inputPaths.Add(eventsBuffer[i]);
                    };
                    _sync.Release();
                }
            }
        }
        /// <summary>
        /// convenience method to cast.
        /// </summary>
        /// <returns>Null if event isn't relevant</returns>
        public InputPathEventStream CastAuditStreamInputToInputPathAuditObject(NasuniEventRecord auditStream)
        {
            InputPathEventStream inputPathAudit = new InputPathEventStream();// auditStream.path, "tor", PathStatus.Unstarted);
                                                                             //Internal/FWR
            ///now/Internal/site/departments/graphics/images/test.PNG"

            inputPathAudit.Path = auditStream.newpath ?? auditStream.path;
            inputPathAudit.IsDir = auditStream.is_dir;
            inputPathAudit.TimeStampUtc = DateTimeOffset.FromUnixTimeSeconds(auditStream.timestamp).UtcDateTime;
            switch (auditStream.event_type)
            {
                case NasuniEventRecord.event_types.AUDIT_RENAME:
                    inputPathAudit.PresenceAction = ActionPresence.Move;
                    inputPathAudit.PathFrom = auditStream.path;
                    break;
                case NasuniEventRecord.event_types.AUDIT_SETXATTR:
                    if (!auditStream.username.EndsWith("$"))//acl settings by SYSTEM/Computer object aren't associated with regular permissions changes that we care about. TODO investigate.
                    {
                        inputPathAudit.ContentAction = ActionContent.ACLSet;
                    }
                    else
                    {
                        return null;
                    }
                    break;
                case NasuniEventRecord.event_types.AUDIT_WRITE:
                    inputPathAudit.ContentAction = ActionContent.Write;
                    break;
                case NasuniEventRecord.event_types.AUDIT_UNLINK:
                    inputPathAudit.PresenceAction = ActionPresence.Delete;
                    break;
                default:
                    return null;//we don't care about the other event types.
            }
            return inputPathAudit;
        }

        public bool TestReadLogs()
        {
            var logTasks = ReadLogsInDirectoryAsync(FolderTowatch);
            logTasks.Wait();
            return logTasks.IsCompleted;
        }
        public void TestVerifyOutput(string outPutPath)
        {
            _inputPaths.TestVerifyOutput(outPutPath);
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _sync.Dispose();
                    _task.Dispose();
                    // TODO: dispose managed state (managed objects)
                }
                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~AuditStreamAccess()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
