using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;


namespace HOK.Elastic.Logger
{
    public class LifecycleManagement : IDisposable
    {
        private Log4NetLogger _il;
        private bool disposedValue;

        public LifecycleManagement(Log4NetLogger il)
        {
            _il = il;
        }
        public void Purge(string pathToRemoveLogsFrom, DateTime minimumDate, int minimumFileToKeep)
        {
            var di = new DirectoryInfo(pathToRemoveLogsFrom);
            int skippedfilecount = 0;
            if (di.Exists)
            {
                foreach (var fi in di.EnumerateFiles("*.log.*").OrderByDescending(fi => fi.LastWriteTime).Skip(minimumFileToKeep))
                {
                    try
                    {
                        if (fi.LastWriteTime < minimumDate)
                        {
                            fi.Delete();
                        }
                        else
                        {
                            skippedfilecount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
                        {
                            _il.LogErr($"Error deleting log because of {ex.Message}", fi.Name);
                        }
                    }
                }
                if (_il.IsEnabled(LogLevel.Debug))
                {
                    _il.LogDebugInfo($"{skippedfilecount} files were too new to be deleted.", pathToRemoveLogsFrom);
                }
            }
            else
            {
                if (_il.IsEnabled(LogLevel.Warning))
                {
                    _il.LogWarn($"Log folder didn't exist!", pathToRemoveLogsFrom);
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _il = null;
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~LifecycleManagement()
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

