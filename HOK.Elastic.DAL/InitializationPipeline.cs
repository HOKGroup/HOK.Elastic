using HOK.Elastic.DAL.Models;
using Microsoft.Extensions.Logging;
using Nest;
using System;
using System.Linq;

namespace HOK.Elastic.DAL
{
    public class InitializationPipeline : Initialization
    {        
        public static string PIPEEmail 
        { 
            get 
            { 
                return StaticIndexPrefix.Prefix + "pipe_email"; 
            } 
        }
        /// <summary>
        /// Conditional Pipeline that decides sends document to categorization pipeline if category is unpopulated.
        /// </summary>
        public static string PIPEvalidate
        {
            get
            {
                return StaticIndexPrefix.Prefix + "pipe_validate";
            }
        }
        public string PIPECategorizationProjectExtractRgx { get; set; } = "(^$)";
        public static string PIPECategorizationProject
        {
            get
            {
                return StaticIndexPrefix.Prefix + "pipe_categoryproject";
            }
        }
        public static string PIPEDocument
        {
            get
            {
                return StaticIndexPrefix.Prefix + "pipe_document";
            }
        }

        private const int pipelinecharacterlimit = 100000;//-1 can possibly leave us open to this was set to 1000....which would limit how much text to extract.
        private const string regexPatternToFindMultipleLinebreaks = @"[\r\n]{1}[\s]+";
        public string[] PipeLines { get { return new string[] { PIPEEmail, PIPEDocument, PIPEvalidate, PIPECategorizationProject }; } }

        public InitializationPipeline(Uri elastiSearchServerUrl, Logger.Log4NetLogger logger) : base(elastiSearchServerUrl, logger)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        public void Put(bool throwOnError = false)
        {
            ///check if pipelines exist? Warn if they aren't standard.
            if (ilwarn) _il.LogWarn("Setting up Pipelines");
            //if (CheckForPipeLines())
            //{

            //}
            //else
            //{
                WriteResponse(PutPipeValidator(), throwOnError);
                WriteResponse(PutPipeCategoryProject(), throwOnError);
                WriteResponse(PutPipeTikaDoc(), throwOnError);
                WriteResponse(PutPipeMsg(), throwOnError);
            //}
        }
        /// <summary>
        /// In future, we might compare the quality of the pipeline to ensure consistency with the code.
        /// </summary>
        /// <returns>True if pipelines exist</returns>
        public bool CheckForPipeLines()
        {
            var clusterPipelineQuery = client.Ingest.GetPipeline(g => g.Id(string.Join(",", PipeLines)));
            if (clusterPipelineQuery.IsValid)
            {
                if (clusterPipelineQuery.Pipelines.Count == PipeLines.Length)
                {
                    return true;
                }
                else
                {
                    if (ilwarn) _il.LogWarn("Not all Pipelines matched!", "", clusterPipelineQuery.ApiCall.Uri);
                    return false;
                }
            }
            else
            {
                if (ilwarn) _il.LogWarn("Pipeline query failed!", "", clusterPipelineQuery.ApiCall.Uri);
                return false;
            }
        }


        /// <summary>
        /// When deleting the pipelines, this is temporary until we get a check working
        /// </summary>
        public void PromptToDelete(bool throwOnError = false)
        {
#if DEBUG
            if (ilwarn) _il.LogWarn($"About to Delete {StaticIndexPrefix.Prefix} pipelines....type {{yes}} and {{enter}} to delete...or just {{enter}} to skip.");
            if (string.Equals(Console.ReadLine(), "yes", StringComparison.OrdinalIgnoreCase))
            {
                var pipelinesToDelete = new string[] { PIPEvalidate, PIPEEmail, PIPEDocument,PIPECategorizationProject };
                foreach (var p in pipelinesToDelete)
                {
                    var response = this.client.Ingest.DeletePipeline(p);
                    WriteResponse(response, throwOnError);
                }
            }
#endif
        }

        /// <summary>
        /// Setup Ingest Pipelines For Emails
        /// </summary>
        /// <returns>PutPipeLineResponse</returns>
        private PutPipelineResponse PutPipeMsg()
        {
            PutPipelineResponse response = client.Ingest
            .PutPipeline(PIPEEmail, p => p
                .Description("Email msg pipeline, removes multiple linebreaks")
                .Processors(pr => pr
                    .Gsub<FSOemail>(gk => gk//condense multiple linebreaks in the body content
                        .Field(f => f.Attachment.Content)
                            .Pattern(regexPatternToFindMultipleLinebreaks)
                            .Replacement("\r\n")
                            .IgnoreMissing(true)
                            )
                    .Pipeline(p1 => p1.ProcessorName(PIPEvalidate)
                        )
                )
            );
            return response;
        }
        /// <summary>
        /// Setup Ingest Pipelines for Documents, PDFs, Excel files etc to be processed by Elastic Ingest Nodes (Tika)
        /// </summary>
        /// <returns>PutPipeLineResponse</returns>
        private PutPipelineResponse PutPipeTikaDoc()
        {
            ////https://www.elastic.co/guide/en/elasticsearch/client/net-api/current/pipelines.html
            PutPipelineResponse response = client.Ingest
            .PutPipeline(PIPEDocument, p => p
                .Description("Document attachment pipeline")
                .Processors(pr => pr
                    .Attachment<FSOdocument>(a => a
                        .If("ctx.content != null")
                        .Field(f => f.Content)
                        .IndexedCharacters(pipelinecharacterlimit)
                        .TargetField(f => f.Attachment)
                        .OnFailure(f => f
                            .Script(s => s
                                .Lang("painless")
                                .Source("ctx.failureCount++")
                                )
                            .Script(s => s
                                .Lang("painless")
                                .Source("ctx.failureReason = 'Tika pipeline failure'")
                                )
                            )
                        )
                    .Remove<FSOdocument>(r => r
                         .If("ctx.content != null")
                         .Field(f => f.Field(f1 => f1.Content))
                        )
                    .Gsub<FSOdocument>(gsub => gsub
                         .If("ctx.attachment?.content != null")
                        .Field(f => f.Attachment.Content)//if this doesn't exist 
                            .IgnoreMissing(true)
                            .Pattern(regexPatternToFindMultipleLinebreaks)
                            .Replacement("\r\n")
                            .IgnoreFailure(true)
                    )
                    .Pipeline(p1 => p1.ProcessorName(PIPEvalidate)//daisychain another pipeline.
                    )
                )
            );
            return response;
        }

        /// <summary>
        /// Pipeline that determines the category based on the path.
        /// </summary>
        private PutPipelineResponse PutPipeValidator()
        {
            PutPipelineResponse response = client.Ingest
            .PutPipeline(PIPEvalidate, p => p
                .Description("Conditional pipeline to determine if we need to do any additional processing or populate missing fields")
                .Processors(pr => pr
                    .Pipeline(pi => pi
                        //.If("ctx.category == null")//we might try and pre-process the category in code.
                        .ProcessorName(PIPECategorizationProject)
                        )
                    )
                );
            return response;
        }

        /// <summary>
        /// Pipeline that determines the category based on the path....only ran if the category is blank
        /// RegexPattern provided should contain a named capture group 'category' in order to extract (and populate) the category field
        /// example "^\\\\\\\\contoso\\\\projects\\\\.*?\\\\[a-z]\\s?\\-\\s?(?<category>.*?)\\\\",
        /// </summary>
        private PutPipelineResponse PutPipeCategoryProject()
        {   
            PutPipelineResponse response = client.Ingest
            .PutPipeline(PIPECategorizationProject, p => p
                .Description("Pipeline to assign Category based on filepath.")
                    .Processors(pr => pr
                        .Grok<FSO>(g=> g
                            .Field(f=>f.Id)
                            .PatternDefinitions(pd => pd.Add("CATEGORYPATTERN", PIPECategorizationProjectExtractRgx))
                            .Patterns("%{CATEGORYPATTERN:category}")
                            .IgnoreFailure(true)
                            .IgnoreMissing(true)
                            )
                        .Gsub<FSO>(g=>g
                            .Field(f=>f.Category)
                            .Pattern(@"\sand\s")
                            .Replacement(@"&")
                            .IgnoreMissing(true)
                            .IgnoreFailure(true)
                            )
                         .Gsub<FSO>(g => g
                            .Field(f => f.Category)
                            .Pattern(@"\s|\)|\(")
                            .Replacement(@"")
                            .IgnoreMissing(true)
                            .IgnoreFailure(true)
                            )
                          .Gsub<FSO>(g => g
                            .Field(f => f.Category)
                            .Pattern(@"\\")
                            .Replacement(@"-")
                            .IgnoreMissing(true)
                            .IgnoreFailure(true)
                            )
                        .Uppercase<FSO>
                            (u=>u.Field(f=>f.Category)
                            .IgnoreMissing(true)
                            .IgnoreFailure(true)
                            )                           
                        )                    
                    );
            return response;
        }
    }
}