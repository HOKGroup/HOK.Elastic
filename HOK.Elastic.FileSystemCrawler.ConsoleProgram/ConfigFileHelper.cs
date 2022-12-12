using HOK.Elastic.FileSystemCrawler.Models;
using log4net;
using log4net.Appender;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Schema.Generation;
using System;
using System.IO;

namespace HOK.Elastic.FileSystemCrawler.ConsoleProgram
{
    internal partial class ConfigFileHelper
    {
        public static void ChangeLog4netOutputpaths(string outputpaths, FileInfo configfile)//todo this is temp perhaps if we can just load up an alternate config file 
        {
            log4net.Config.XmlConfigurator.Configure(configfile);
            log4net.Repository.Hierarchy.Hierarchy h =
            (log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository();// (logRepository.Name);
            foreach (IAppender a in h.Root.Appenders)
            {
                string defaultLogFile;
                if (a is RollingFileAppender)
                {
                    RollingFileAppender fa = (RollingFileAppender)a;
                    defaultLogFile = fa.File;
                    if (fa.StaticLogFileName == false)
                    {
                        var logFileLocation = outputpaths + @"\";
                        fa.File = logFileLocation;
                        fa.ActivateOptions();
                    }
                    else
                    {
                        FileInfo fi = new FileInfo(fa.File);
                        fa.File = Path.Combine(outputpaths, fi.Name);
                        fa.ActivateOptions();
                    }
                    try
                    {
                        File.Delete(defaultLogFile);//delete the log file that gets created in the wrong location
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                }
                else if (a is FileAppender fa)
                {
                    defaultLogFile = fa.File;
                    // fa.File = outputpaths + @"\";                    
                    FileInfo fi = new FileInfo(fa.File);
                    fa.File = Path.Combine(outputpaths, fi.Name);
                    fa.ActivateOptions();
                    try
                    {
                        File.Delete(defaultLogFile);//delete the log file that gets created in the wrong location
                    }
                    catch(Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// Utility to generate jsonschema file.
        /// </summary>
        public static void MakeJsonSchemaFileForAppSettings()
        {
            JSchemaGenerator generator = new JSchemaGenerator();
            JSchema settingsschema = generator.Generate(typeof(HOK.Elastic.FileSystemCrawler.Models.SettingsApp));
            JSchema settingsjobschema = generator.Generate(typeof(HOK.Elastic.FileSystemCrawler.Models.SettingsJob));
            try
            {
                File.WriteAllText(nameof(settingsjobschema) + ".json", settingsjobschema.ToString());
                File.WriteAllText(nameof(settingsschema) + ".json", settingsschema.ToString());
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}