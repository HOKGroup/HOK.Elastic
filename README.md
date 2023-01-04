# HOK.Elastic
## Description
HOK.Elastic is a .NET solution for crawling filesystems and ingesting file + directory data into Elasticsearch. It also includes an Events API application and crawl mode for file change watching in cloud file storage applications (i.e. Nasuni).


The solution is comprised of multiple projects:

| Name | Description |
|-------| -------------|
| **HOK.FileSystemCrawler** | Library for crawling the filesystem |
| **HOK.Elastic.DAL** | Library for talking to Elastic cluster |
| **HOK.Elastic.Logger** | Library to help with logging |
| **HOK.Elastic.RoleMappingGroupSync** | Executable Program that reads Active Directory user accounts and adds an Elastic role document query and rolemapping rule for each user for granular permissions in the form of Access Control Lists (ACLs) for every document in the index.|
| **HOK.NasuniAuditEventAPI**| Web API to publish Nasuni's File System events from a log file source for consumption by a HOK.Elastic.FileSystemCrawler.ConsoleProgram running a job in 'AuditEventCrawl' mode.|
| **HOK.NasuniAuditEventAPI.DAL**| Library for reading and converting Nasuni Audit Event log records into a more generic, actionable event record.|



## Prerequisites
To run the crawler
- Elasticsearch cluster v7.x with X-Pack Security enabled
- .NET Core 3.1

Configure an Elastic cluster:
The initial connection from HOK.Elastic.FilesystemCrawler.ConsoleProgram to the Elastic cluster is made using Kerberos authentication which subsequently creates an API key for the session. You will need to modify your code or configure the Elastic cluster to use the Kerberos security realm.

_Note: To use and support document level security you will need to have X-Pack installed but it should be possible to have a successful installation without it._ 

## Installation
### Getting Started
1. Clone the repo
```
git clone https://github.com/HOKGroup/HOK.Elastic.git
cd HOK.Elastic
```

2. Build the solution
```
dotnet build
```
3. Configure the HOK.Elastic.FileSystemCrawler.ConsoleProgram application + create jobs. 
- Edit the `appsettings.example.json` with your configuration and rename to `appsettigs.json`
- Create one or more jobsettings folders and config files that tells the program where to crawl and in what mode. See the `jobsettings.example.json` for a sample.


## Getting Help

To get help, please [submit an issue] to this Github repository.
