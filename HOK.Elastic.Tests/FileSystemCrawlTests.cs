using FluentAssertions;
using HOK.Elastic.DAL;
using HOK.Elastic.DAL.Models;
using HOK.Elastic.FileSystemCrawler;
using HOK.Elastic.FileSystemCrawler.Models;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Xunit;

namespace HOK.Elastic.Tests;

public class FileSystemCrawlerFixture : IDisposable
{
    public FileSystemCrawlerFixture()
    {
        // called once before all tests
        string inputPath = @"C:\Elastic\HOK.Elastic Tests\Template\Standard";
        string workingPathBase = @"C:\Temp";
        string workingPathUnc = @$"\\{System.Environment.MachineName.ToLowerInvariant()}\c$\Temp";
        string office = "abc";
        tempPath = @$"{workingPathBase}\{office}";

        var uri = "https://elastic-dev-cluster:9200";
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
                $"^{workingPathBase}".Replace("\\", "\\\\")
                + @"(\w{2,4})\\projects\\.*?\\[a-z]\s?\-\s?(?<category>.*?)\\",
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

        var workingPath =
            $@"{workingPathBase}\{office}\projects\2025\25.12345.00 Crawl Test Project\";
        TestUtils.CopyFilesRecursively(inputPath, workingPath);
        workingPath =
            $@"{workingPathUnc}\{office}\projects\2025\25.12345.00 Crawl Test Project\";
        var inputPathBase = new InputPathCollectionBase
        {
            new InputPathBase(workingPath, office, PathStatus.Unstarted)
        };
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
        );

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
}

public class FileSystemCrawlTests : IClassFixture<FileSystemCrawlerFixture> {
    public FileSystemCrawlerFixture fixture;
    public FileSystemCrawlTests(FileSystemCrawlerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async void FullCrawlTestSuite()
    {
        CancellationTokenSource _ct = new CancellationTokenSource();

        //**Full crawl**
        fixture.settingsJobArgs.CrawlMode = CrawlMode.Full;
        CompletionInfo completionInfo = await fixture.worker.RunAsync(fixture.settingsJobArgs, _ct.Token);

        var path = fixture.worker.GetDirectoriesFromInputPaths(new List<InputPathBase> { fixture.settingsJobArgs.InputPaths.First() }, fixture.settingsJobArgs).First();
        //var path = fixture.settingsJobArgs.InputPaths.First();
        if (path == null)
            throw new Exception("Path is not defined");

        // TODO: This path doesn't match what's in Elastic
        var crawlContents = fixture.discovery.FindRootAndChildren(path.PublishedPath, true);

        //-Check doc count, broken down by index
        var totalDocCount = crawlContents.Contents.Count();
        var dirCount = crawlContents.Contents.Count(
            x => x.Item2 == $"{fixture.settingsJobArgs.IndexNamePrefix}dir"
        );

        dirCount.Should().BeGreaterOrEqualTo(1);

        //-Check properties (metadata)
        //Change permissions
        //Delete a file
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
}