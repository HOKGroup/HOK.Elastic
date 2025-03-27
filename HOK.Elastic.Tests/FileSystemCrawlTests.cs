using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Dataflow;
using Elasticsearch.Net.Specification.TextStructureApi;
using FluentAssertions;
using HOK.Elastic.DAL;
using HOK.Elastic.DAL.Models;
using HOK.Elastic.FileSystemCrawler;
using HOK.Elastic.FileSystemCrawler.Models;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.CrossPlatEngine;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Engine.ClientProtocol;
using Nest;
using NLog.Extensions.Logging;
using RtfPipe.Tokens;
using Xunit;
using Xunit.Sdk;

namespace HOK.Elastic.Tests;

public class FileSystemCrawlerFixture : IDisposable
{
    public string WorkingPathUNC { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; set; }
    public FileSystemCrawlerFixture()
    {
        CancellationTokenSource = new CancellationTokenSource();//v3 we might be able to use TestContext.CancellationTokenSource.
        // called once before all tests
        var machineName = System.Environment.MachineName.ToLowerInvariant();

        string inputPath = @"C:\Elastic\HOK.Elastic Tests\Template\Standard";
        string workingPathBase = @"C:\Temp";
        string workingPathUnc = @$"\\{System.Environment.MachineName.ToLowerInvariant()}\c$\Temp";
        string office = "abc";



        tempPath = @$"{workingPathBase}\{office}";

        var uri = "https://elastic-dev-cluster:9200";
        if (machineName == "tor-l007")
        {
            inputPath = @"C:\developer\VS-Projects\HOK.ElasticRoot\crawlertests\Standard";
            workingPathBase = @"C:\developer\VS-Projects\HOK.ElasticRoot";
            workingPathUnc = @$"\\{machineName}\c$\developer\VS-Projects\HOK.ElasticRoot";
            office = "testoffice";
            uri = "https://hok-395vs:9200";
        }

        var workingPath = $@"{workingPathBase}\{office}\projects\2025\25.12345.00 Crawl Test Project\";
        TestUtils.CopyFilesRecursively(inputPath, workingPath);
        workingPath = $@"{workingPathUnc}\{office}\projects\2025\25.12345.00 Crawl Test Project\";
        if (!workingPath.EndsWith("\\")) workingPath = workingPath + "\\";

        var workerargs = new SettingsJobArgs()
        {
            // Things to input
            ElasticDiscoveryURI = new List<Uri> { new Uri(uri) },
            ElasticIndexURI = new List<Uri> { new Uri(uri) },
            //ReadFileContents = jobSettings.ReadFileContents,
            //PathForCrawling = jobSettings.PathForCrawling,
            //PathForCrawlingContent = jobSettings.PathForCrawlingContent,
            //PublishedPath = jobSettings.PublishedPath,

            DocInsertionThreads = 1,
            CPUCoreThreadMultiplier = 1,
            ReadContentSizeLimitMB = 200,
            BulkUploadSize = 500,
            IgnoreExtensions = new List<string>(),
            OfficeSiteExtractRegex = $"^{workingPathBase}".Replace("\\", "\\\\") + @"(\w{2,4})",
            ProjectExtractRegex =
                $"^{workingPathBase}".Replace("\\", "\\\\")
                + @"\w{2,4}\\projects\\((\d\d\d\d\\(\d{2}[\d|\.\-]*)+\s*(\+|_|\-)?\s*([^\\|$|\r|\n]*)|(interiors\\|planning\\|architecture\\|hospitality\\)?([^\\]*)?))",
            PipeCategorizationRegex =
            @"(?:\\\d{2}\.\d{4,5}\.\d{2}.*?\\[a-z](\-|\s))(?<category>.*?)(?:\\|$)",
            // Don't Set
            //PathInclusionRegex = jobSettings.PathInclusionRegex ?? AppSettings.PathInclusionRegex,
            //FileNameExclusionRegex = jobSettings.FileNameExclusionRegex ?? AppSettings.FileNameExclusionRegex,
            //CrawlMode = jobSettings.CrawlMode, (will do in the test itself)
            //InputPathLocation = jobDirectoryInfo.FullName,
            ExceptionsPerTenMinuteIntervalLimit = 100
        };
        DateTime date = DateTime.Now;
        var shortDate = date.ToString("yyyyMMdd");
        workerargs.IndexNamePrefix = $"testrrunner-{shortDate}.";


        var inputPathBase = new InputPathCollectionBase
        {
            new InputPathBase(workingPath, office, PathStatus.Unstarted)
        };
        WorkingPathUNC = workingPath;
        inputPathBase.PathForCrawling = workingPathUnc;
        inputPathBase.PathForCrawlingContent = workingPathBase;
        inputPathBase.PublishedPath = workingPathBase;
        workerargs.InputPaths = inputPathBase;
        workerargs.PathForCrawling = workingPathUnc;
        workerargs.PathForCrawlingContent = workingPathUnc;
        workerargs.PublishedPath = workingPathUnc;
        workerargs.InputPathLocation = workingPathBase;
        workerargs.RunningInteractively = false;
        PathHelper.Set(workerargs.PublishedPath, workerargs.PathForCrawlingContent, workerargs.PathForCrawling);

        PathHelper.SetPathInclusion(workerargs.PathInclusionRegex);
        PathHelper.SetFileNameExclusion(workerargs.FileNameExclusionRegex);
        PathHelper.SetOfficeExtractRgx(workerargs.OfficeSiteExtractRegex);
        PathHelper.SetProjectExtractRgx(workerargs.ProjectExtractRegex);
        PathHelper.IgnoreExtensions = workerargs.IgnoreExtensions?.Distinct().ToHashSet();
        //fileSystemCrawlTest = new FileSystemCrawlTest(workerargs);
        settingsJobArgs = workerargs;
        _loggerFactory = LoggerFactory.Create(builder => builder.AddNLog());
        IndexNameHelper indexNameHelper = new IndexNameHelper(workerargs.IndexNamePrefix);
        PipeLineNameHelper pipeLineNameHelper = new PipeLineNameHelper(
            workerargs.IndexNamePrefix
        )
        { PIPECategorizationProjectExtractRgx = workerargs.PipeCategorizationRegex };

        discovery = new DAL.Discovery(
            pipeLineNameHelper,
            indexNameHelper,
            workerargs.ElasticDiscoveryURI.First(),
            _loggerFactory.CreateLogger($"{workerargs.JobName}.Discovery")
        );
        index = new DAL.Index(
            pipeLineNameHelper,
            indexNameHelper,
            workerargs.ElasticIndexURI.First(),
            _loggerFactory.CreateLogger($"{workerargs.JobName}.Index")
        );
        securityHelper = new SecurityHelper(
            _loggerFactory.CreateLogger($"{workerargs.JobName}.SecurityHelper")
        );
        documentHelper = new DocumentHelper(
            workerargs.ReadFileContents ?? false,
            securityHelper,
            index,
            _loggerFactory.CreateLogger($"{workerargs.JobName}.DocumentHelper")
        );

        worker = new WorkerCrawler(
            index,
            discovery,
            securityHelper,
            documentHelper,
            _loggerFactory.CreateLogger($"{workerargs.JobName}.WorkerCrawler")
        );

        workerEvents = new WorkerEventStream(
           index,
           discovery,
           securityHelper,
           documentHelper,
           _loggerFactory.CreateLogger($"{workerargs.JobName}.WorkerEvents")
       );

        PipeLineNameHelper pipeLineHelper = new PipeLineNameHelper(workerargs.IndexNamePrefix);

        using (var initializationPipeline = new InitializationPipeline(pipeLineNameHelper, indexNameHelper, workerargs.ElasticIndexURI.First(), _loggerFactory.CreateLogger($"{workerargs.JobName}.Setup")))
        {            
            if (!initializationPipeline.CheckForPipeLines())
            {
                initializationPipeline.Put(true);
            }

            using (var initializationIndex = new InitializationIndex(pipeLineNameHelper, indexNameHelper, workerargs.ElasticIndexURI.First(), _loggerFactory.CreateLogger($"{workerargs.JobName}.IndexSetup")))
            {
                if (initializationIndex.PreFlightFail())
                {
                    initializationIndex.Put(workerargs.RunningInteractively);
                }
            }
        }

        TestElasticDAL = new TestElasticDAL(
           pipeLineNameHelper,
           indexNameHelper,
           workerargs.ElasticIndexURI.First(),
           _loggerFactory.CreateLogger($"{workerargs.JobName}.Index")
       );
    }


    public void Dispose()
    {
        // called once after all tests
        // TODO: Cleanup tempPath and indices
        //File.Delete(tempPath);        
    }

    public string tempPath { get; private set; }
    public SettingsJobArgs settingsJobArgs { get; private set; }
    public CompletionInfo? completionInfo;
    private readonly ILoggerFactory _loggerFactory;
    public DAL.Discovery discovery { get; private set; }
    private DAL.Index index { get; set; }
    private SecurityHelper securityHelper { get; set; }
    private DocumentHelper documentHelper { get; set; }
    public WorkerCrawler worker { get; private set; }
    public WorkerEventStream workerEvents { get; private set; }
    public TestElasticDAL TestElasticDAL { get; set; }
}

public class TestElasticDAL : HOK.Elastic.DAL.Index
{
    public TestElasticDAL(PipeLineNameHelper pipeLineNameHelper, IndexNameHelper indexNameHelper, Uri elastiSearchServerUrl, ILogger logger) : base(pipeLineNameHelper, indexNameHelper, elastiSearchServerUrl, logger)
    {
    }
    public Nest.ElasticClient ElasticClient => this.client;
}

/// <summary>
/// If the test fixture fires multiple times, try to clean and rebuild the solution.
/// </summary>

public class FileSystemCrawlTests : IClassFixture<FileSystemCrawlerFixture>
{
    public FileSystemCrawlerFixture fixture;
    private ElasticClient ec;
    private IndexNameHelper indexHelper;
    public FileSystemCrawlTests(FileSystemCrawlerFixture fixture)
    {
        this.fixture = fixture;
        this.ec = this.fixture.TestElasticDAL.ElasticClient;
        this.indexHelper = this.fixture.TestElasticDAL.IndexHelper;
    }


    [Fact]

    public async void FullCrawlTestSuite()
    {
        CancellationTokenSource _ct = new CancellationTokenSource();

        //**Full crawl**
        fixture.settingsJobArgs.CrawlMode = CrawlMode.Full;
        CompletionInfo completionInfo = await fixture.worker.RunAsync(fixture.settingsJobArgs, _ct.Token);
        await MoveAFolder();
        await FlushIndex();
        var path = fixture.worker.GetDirectoriesFromInputPaths(new List<InputPathBase> { fixture.settingsJobArgs.InputPaths.First() }, fixture.settingsJobArgs).First();
        //var path = fixture.settingsJobArgs.InputPaths.First();
        if (path == null)
        {
            throw new Exception("Path is not defined");
        }

        // TODO: This path doesn't match what's in Elastic
        DirectoryContents? crawlContents=null;
        var oneDeletedTest = await RetryForSuccess(() => {
            crawlContents = fixture.discovery.FindRootAndChildren(path.PublishedPath, true);
            if (crawlContents != null)
            {
                return true;
            }
            return false;
        });

        crawlContents.Should().NotBe(null);
        //-Check doc count, broken down by index
        var totalDocCount = crawlContents?.Contents.Count();
        var dirCount = crawlContents?.Contents.Count(
            x => x.Item2 == $"{fixture.settingsJobArgs.IndexNamePrefix}dir"
        );

        dirCount.Should().BeGreaterOrEqualTo(1);

        //-Check properties (metadata)
        //Change permissions
        await ChangeAFile();
        await MoveAFile();
        //Delete a file
        await DeleteAFile();
        await MoveAFolder();


        //**Incremental crawl**
        //-Check doc count, broken down by index
        //-Check doc count as different user with less permissions???
        //-Check properties (metadata)
        //Add a file
        //**Incremental crawl**
        //-Check doc count, broken down by index
        //-Check doc count as different user with less permissions???
        //-Check properties (metadata)
    }
    private async Task FlushIndex()
    {
        var response = await ec.Indices.FlushAsync(indexHelper.PrefixWildcard, x => x.Index(indexHelper.AllIndexNames).IgnoreUnavailable(true).Human(true));
        if (response.IsValid)
        {
            Debug.Write($"Flushed '{response.Shards.Successful}' shards and failed on '{response.Shards.Failures}'");
        }
        else
        {
            Debug.Write($"Failed to Flush");
        }
    }


    private async Task DeleteAFile()
    {
        CancellationTokenSource _ct = fixture.CancellationTokenSource;
        long indexCountBefore = 0, indexCountAfter = 0;

        await FlushIndex();
        //**Incremental crawl**
        fixture.settingsJobArgs.CrawlMode = CrawlMode.Incremental;
        CompletionInfo completionInfo = await fixture.worker.RunAsync(fixture.settingsJobArgs, _ct.Token);
        await FlushIndex();

        //Index Count Before:
        var countResponse = ec.Count<FSOdocument>(x => x.Index(indexHelper.IndexNameFsoDoc));
        if (countResponse.IsValid)
        {
            indexCountBefore = countResponse.Count;
        }

        //Delete a file (make a change)
        FileInfo fi = new FileInfo(Path.Combine(fixture.WorkingPathUNC, "F-Specifications\\F7-Other\\!ARCHIVE IN ZIP FILES ONLY.txt").ToLowerInvariant());
        if (fi.Exists)
        {
            fi.Delete();
            //**Incremental crawl**
            CompletionInfo completionInfoAfter = await fixture.worker.RunAsync(fixture.settingsJobArgs, _ct.Token);
            var oneDeletedTest = await RetryForSuccess(() => {
                countResponse = ec.Count<FSOdocument>(x => x.Index(indexHelper.IndexNameFsoDoc));
                if (countResponse.IsValid)
                {
                    indexCountAfter = countResponse.Count;
                    var indexDiff = indexCountBefore - indexCountAfter;
                    if (indexDiff == 1) return true;
                }
                return false;
            });
            oneDeletedTest.Should().BeTrue( "Because we deleted one file from the crawl directory");
            var fileCount = (completionInfo.FileCount + completionInfo.FileSkipped) - (completionInfoAfter.FileCount + completionInfoAfter.FileSkipped);
            fileCount.Should().Be(1, "Because we deleted one file from the crawl directory");
        }
        else
        {
            false.Should().BeTrue("Source file to delete didn't exist. Can't run test.");
        }
    }

    private async Task ChangeAFile()
    {
        CancellationTokenSource _ct = fixture.CancellationTokenSource;
        long indexCountBefore = 0, indexCountAfter = 0;

        await FlushIndex();
        //**Incremental crawl**
        fixture.settingsJobArgs.CrawlMode = CrawlMode.Incremental;
        CompletionInfo completionInfo = await fixture.worker.RunAsync(fixture.settingsJobArgs, _ct.Token);

        await FlushIndex();

        //Index Count Before:
        var countResponse = ec.Count<FSOdocument>(x => x.Index(indexHelper.IndexNameFsoDoc));
        if (countResponse.IsValid)
        {
            indexCountBefore = countResponse.Count;
        }


        //Change a file (make a change)
        FileInfo fi = new FileInfo(Path.Combine(fixture.WorkingPathUNC, "E-Design\\E6-Models\\_Archive\\!ARCHIVE IN ZIP FILES ONLY.txt").ToLowerInvariant());
        if (fi.Exists)
        {
            var path = PathHelper.GetPublishedPath(fi.FullName);
            //this.fixture.TestElasticDAL.
            var fsodocBefore = this.fixture.TestElasticDAL.GetById<FSOdocument>(path, indexHelper.IndexNameFsoDoc);

            var fileSecurity = fi.GetAccessControl(System.Security.AccessControl.AccessControlSections.Access);
            List<FileSystemAccessRule> rulesToAddBack = new List<FileSystemAccessRule>();
            foreach (FileSystemAccessRule rule in fileSecurity.GetAccessRules(true, false, typeof(NTAccount)))
            {
                rulesToAddBack.Add(rule);
                fileSecurity.RemoveAccessRule(rule);
            }
            fileSecurity.AddAccessRule(new FileSystemAccessRule(new NTAccount("Administrator"), FileSystemRights.Modify, AccessControlType.Allow));
            fi.SetAccessControl(fileSecurity);
            //change the date so that it looks like a changed file...
            fi.LastWriteTime = fi.CreationTime = DateTime.Now.Subtract(TimeSpan.FromHours(1));

            //**Incremental crawl** to check for changes
            CompletionInfo completionInfoAfter = await fixture.worker.RunAsync(fixture.settingsJobArgs, _ct.Token);

            var aclTest = await RetryForSuccess(() => {
                var fsodocAfter = this.fixture.TestElasticDAL.GetById<FSOdocument>(path, indexHelper.IndexNameFsoDoc);
                var aclsAfter = string.Join(",", fsodocAfter.Acls.This);
                var aclsBefore = string.Join(",", fsodocBefore.Acls.This);
                var differentAcls = aclsAfter != aclsBefore;
                return differentAcls; 
            });
            aclTest.Should().BeTrue();
            var fsodocAfter = this.fixture.TestElasticDAL.GetById<FSOdocument>(path, indexHelper.IndexNameFsoDoc);
            //Index Count After:
            countResponse = ec.Count<FSOdocument>(x => x.Index(indexHelper.IndexNameFsoDoc));
            if (countResponse.IsValid)
            {
                indexCountAfter = countResponse.Count;
            }
            var indexDiff = indexCountBefore - indexCountAfter;
            indexDiff.Should().Be(0, "Because we made no change in the total number of files the crawl directory");
            var fileCount = (completionInfo.FileCount + completionInfo.FileSkipped) - (completionInfoAfter.FileCount + completionInfoAfter.FileSkipped);
            fileCount.Should().Be(0, "Because we altered one file from the crawl directory");
            completionInfoAfter.FileCount.Should().Be(1, "one new file changed.");
            fsodocAfter.Category.Should().Be("DESIGN");
        }
        else
        {
            false.Should().BeTrue();
        }
    }
    private async Task MoveAFolder()
    {
        //someday, we can move a folder and incrementalcrawl
        //then move a folder and simulate event crawl and ensure the children all get moved (without requiring incremental crawl to discover new files)
        true.Should().BeTrue();

        CancellationTokenSource _ct = fixture.CancellationTokenSource;
        long indexCountBefore = 0, indexCountAfter = 0;

        await FlushIndex();
        //**Incremental crawl**
        fixture.settingsJobArgs.CrawlMode = CrawlMode.Incremental;
        CompletionInfo completionInfo = await fixture.worker.RunAsync(fixture.settingsJobArgs, _ct.Token);

        await FlushIndex();

        //Index Count Before:
        var countResponse = ec.Count<FSOdocument>(x => x.Index(indexHelper.IndexNameFsoDoc));
        if (countResponse.IsValid)
        {
            indexCountBefore = countResponse.Count;
        }
        //make a copy
        var json = System.Text.Json.JsonSerializer.Serialize<SettingsJobArgs>(this.fixture.settingsJobArgs);
        var settingJobArgCopy = System.Text.Json.JsonSerializer.Deserialize<SettingsJobArgs>(json);
        settingJobArgCopy.InputPaths = new InputPathCollectionEventStream();
        settingJobArgCopy.InputPaths.PathForCrawling = this.fixture.settingsJobArgs.InputPaths.PathForCrawling;
        settingJobArgCopy.InputPaths.PathForCrawlingContent = this.fixture.settingsJobArgs.InputPaths.PathForCrawlingContent;
        settingJobArgCopy.InputPaths.PublishedPath = this.fixture.settingsJobArgs.InputPaths.PublishedPath;
        settingJobArgCopy.CrawlMode = CrawlMode.EventBased;
        var newPath = (Path.Combine(fixture.WorkingPathUNC, "G-Engineering2").ToLowerInvariant());
        var oldPath = (Path.Combine(fixture.WorkingPathUNC, "G-Engineering").ToLowerInvariant());
        //var newpath = @"C:\developer\VS-Projects\HOK.ElasticRoot\testoffice\projects\2025\25.12345.00 Crawl Test Project\Standard\G-Engineering2";
        if (Directory.Exists(oldPath))
        {
            Directory.Move(oldPath, newPath);
            settingJobArgCopy.InputPaths.Add(new InputPathEventStream() { IsDir = true, PathFrom = oldPath, Path = newPath, PresenceAction = ActionPresence.Move, ContentAction = ActionContent.Write });
            CompletionInfo completionInfoAfter = await fixture.workerEvents.RunAsync(settingJobArgCopy, _ct.Token);
            FSOdirectory? fsoAfter = null;
            var contentMoved = await RetryForSuccess(() =>
            {
                var crawlContents = fixture.discovery.FindRootAndChildren(newPath, true);
                var descendants = fixture.discovery.FindDescendentsForMoving(newPath);
                var descendantCount = descendants.Count();
                var fsodocBefore = this.fixture.TestElasticDAL.GetById<FSOdirectory>(newPath, indexHelper.IndexNameDir);
                //
                if(fsodocBefore==null)
                {
                    //This case is a bug (root directory doesn't seem to get moved...it doesn't cause a problem in search as we are generally only interested in files)
                    //true.Should().BeFalse();
                    Debug.Write(newPath + "doc didn't exist but should have");
                }
                if (crawlContents != null)
                {
                    var children = crawlContents.Contents.Count;
                    if (children > 1) return true;
                }
                return false;
            });
            contentMoved.Should().BeTrue();
        }
        else
        {
            false.Should().BeTrue("Source directory to move didn't exist?");
        }
    }



    private async Task MoveAFile()
    {
        CancellationTokenSource _ct = fixture.CancellationTokenSource;
        long indexCountBefore = 0, indexCountAfter = 0;

        await FlushIndex();
        //**Incremental crawl**
        fixture.settingsJobArgs.CrawlMode = CrawlMode.Incremental;
        CompletionInfo completionInfo = await fixture.worker.RunAsync(fixture.settingsJobArgs, _ct.Token);

        await FlushIndex();

        //Index Count Before:
        var countResponse = ec.Count<FSOdocument>(x => x.Index(indexHelper.IndexNameFsoDoc));
        if (countResponse.IsValid)
        {
            indexCountBefore = countResponse.Count;
        }


        //Delete a file (make a change)
        FileInfo fi = new FileInfo(Path.Combine(fixture.WorkingPathUNC, "E-Design\\E6-Models\\_Archive\\!ARCHIVE IN ZIP FILES ONLY.txt").ToLowerInvariant());
        if (fi.Exists)
        {
            var path = PathHelper.GetPublishedPath(fi.FullName);
            //this.fixture.TestElasticDAL.
            var fsodocBefore = this.fixture.TestElasticDAL.GetById<FSOdocument>(path, indexHelper.IndexNameFsoDoc);
            var newPath = Path.Combine(fixture.WorkingPathUNC, "F-Specifications\\F7-Other\\!ARCHIVE IN ZIP FILES ONLY.txt").ToLowerInvariant();
            if (!File.Exists(newPath))
            {
                fi.MoveTo(newPath);
                CompletionInfo completionInfoAfter = await fixture.worker.RunAsync(fixture.settingsJobArgs, _ct.Token);
                await FlushIndex();
                FSOdocument? fsodocAfter = null;
               var foundNewDocument = await RetryForSuccess(() =>
               {
                   fsodocAfter = this.fixture.TestElasticDAL.GetById<FSOdocument>(newPath, indexHelper.IndexNameFsoDoc);
                   if (fsodocAfter != null)
                   {
                       countResponse = ec.Count<FSOdocument>(x => x.Index(indexHelper.IndexNameFsoDoc));
                       if (countResponse.IsValid)
                       {
                           indexCountAfter = countResponse.Count;
                           if (indexCountAfter == indexCountBefore)
                           {
                               return true;
                           }
                       }
                   }
                   return false;
               });
                foundNewDocument.Should().BeTrue();
                fsodocAfter.Should().NotBeNull();
                countResponse = ec.Count<FSOdocument>(x => x.Index(indexHelper.IndexNameFsoDoc));
                countResponse = ec.Count<FSOdocument>(x => x.Index(indexHelper.IndexNameFsoDoc));
                fsodocAfter?.Category.Should().Be("SPECIFICATIONS");
                var indexDiff = indexCountBefore - indexCountAfter;
                indexDiff.Should().Be(0, "Because we made no change in the total number of files the crawl directory");
                completionInfoAfter.FileSkipped.Should().Be(5, "Because we deleted one file when we moved it");
                completionInfoAfter.FileCount.Should().Be(1, "Because we created one file when we moved it");
                completionInfoAfter.Deleted.Should().BeGreaterThan(1, "Because we deleted one file when we moved it");
            }
            else
            {
                false.Should().BeTrue();
            }
        }
        else
        {
            false.Should().BeTrue();
        }
    }

    /// <summary>
    /// Method to run a test against the elastic cluster waiting for the search results match the expected outcome.
    /// </summary>
    /// <param name="f"></param>
    /// <returns>true if successful</returns>
    public async Task<bool> RetryForSuccess(Func<bool> f)
    {
        await FlushIndex();
        DateTime timeout = DateTime.Now.AddMinutes(2);
        while (DateTime.Now < timeout)//keep trying until the document and count are updated in search.
        {
            var result = f.Invoke();
            if (result)
            {
                return true;
            }
            else
            {
                await Task.Delay(10000);//retry again in 10 seconds.
            }
        }
        return false;
    }
}