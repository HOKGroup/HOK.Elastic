using HOK.Elastic.DAL.Models;
using Microsoft.Extensions.Logging;
using Nest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HOK.Elastic.DAL
{
    public class InitializationIndex : Initialization
    {
        public const string SMB_PATH_H = "SMBpath_h";
        public const string SMB_PATH_H_REVERSE = "SMBpath_h_reverse";
        public const string SMB_PATH_Text = "SMBpath_text";
        public const string SMB_PATH_H_Search = "SMBpath-h_search";
        public const string EMAILADDRESS = "emailaddress";
        public const string EMAILADDRESSSEARCH = "emailaddresssearch";
        public const string LOWERCASE = "lowercase";
        public const string KEYWORD = "keyword";
        public const string NONWHITESPACEEDGE = "nonwhitespaceedge";
        public const string NONWHITESPACEEDGESEARCH = "nonwhitespaceedgesearch";
        public const string EDGENGRAM10 = "edgengram10";
        public const string TRUNCATE10 = "truncate10";
        public readonly Nest.Time RefreshInterval = new Time(TimeSpan.FromSeconds(30));

        public InitializationIndex(Uri elastiSearchServerUrl, Logger.Log4NetLogger logger) : base(elastiSearchServerUrl, logger)
        {
        }

        public bool PreFlightFail()
        {            
            bool fail = false;
            string[] indicies = new string[] { FSOdirectory.indexname, FSOfile.indexname, FSOdocument.indexname, FSOemail.indexname };
            for (int i = 0; i < indicies.Length; i++)
            {
                bool exists = client.Indices.Exists(indicies[i]).Exists;
                if (!exists)
                {
                    if (ilwarn) _il.LogWarn("Index doesn't exist", "", indicies[i]);
                    fail = true;
                }
            }
#if DEBUG
            PromptToDelete();
#endif
            return fail;
        }
        /// <summary>
        /// If in DEBUG mode 
        /// </summary>
        public void Put(bool runningInteractively = false)
        {
            BuildIndexFSODirectory();
            BuildIndexFsoFile();
            BuildIndexFSODocument(runningInteractively);
            BuildIndexFSOemail(runningInteractively);
        }
        /// <summary>
        /// Todo we should check for existing documents prior to deleting.
        /// </summary>
        public void PromptToDelete()
        {
#if DEBUG
            if (ilwarn) _il.LogWarn($"Delete {StaticIndexPrefix.Prefix}* indicies?....type {{yes}} and {{enter}} to delete...or just {{enter}} to skip.");
            if (string.Equals(Console.ReadLine(), "yes", StringComparison.OrdinalIgnoreCase))
            {
                string[] indiciesToDelete = new string[] { FSOdirectory.indexname, FSOdocument.indexname, FSOemail.indexname, FSOfile.indexname };
                foreach (string index in indiciesToDelete)
                {
                    DeleteIndexResponse response = client.Indices.Delete(Indices.Index(index));
                    WriteResponse(response);
                }
                if (ilwarn) _il.LogWarn("DeleteIndexComplete. Press any key to continue");
                Console.ReadLine();
            }

#else
            if (ilwarn) _il.LogWarn("Call was made to delete indicies but ignored as this is running in release build.");
#endif
        }

        private int GetUserInteractiveInput(string message, int min, int max)
        {
            while (true)
            {
                Console.WriteLine(message);
                if (Int32.TryParse(Console.ReadLine(), out int result))
                {
                    if (result < min || result > max)
                    {
                        Console.WriteLine($"Number must be between {min} and {max}.");
                    }
                    else
                    {
                        return result;
                    }
                }
                else
                {
                    Console.WriteLine("Couldn't be converted to number..please try again");
                }
            }
        }
   
        public void BuildIndexFSODirectory()
        {
            CreateIndexResponse createFileResponse = client.Indices.Create(FSOdirectory.indexname, i => i
                .Settings(s => s
                   .NumberOfReplicas(0)
                   .NumberOfShards(1)//maybe we should either check how many nodes and calculate or prompt?
                   .RefreshInterval(RefreshInterval)//default is 1s refresh rate
                   .Analysis(a => a = GetCommonAnalysisDescriptor())
                   .FinalPipeline(InitializationPipeline.PIPEvalidate)
                    )
                .Map<FSOdirectory>(map => map
                    .Properties(property => property = GetDefaultPropertyMappingDescriptor<FSOdirectory>())
                    .AutoMap<FSOdirectory>()
            )
           );
            WriteResponse(createFileResponse, false);
        }

        private void BuildIndexFSOemail(bool runningInteractively)
        {
            int shards = runningInteractively ? GetUserInteractiveInput("How many shards for email index?", 1, 64) : 1;
            //AnalysisDescriptor
            var emailAnalysisDescriptor = GetCommonAnalysisDescriptor();
            //Use default SMB tokenizer and analyzer definition and specify additonal email support
            emailAnalysisDescriptor.Tokenizers(tok => tok = GetCommonTokenizersDescriptor()
                .CharGroup(EMAILADDRESS, a => a
                    .TokenizeOnCharacters(" ", ",", "<", ">", "@", "."))
                );

            emailAnalysisDescriptor.Analyzers(an => an = GetCommonAnalyzerDescriptor()
                .Custom(EMAILADDRESS, a => a
                    .Tokenizer(EMAILADDRESS).Filters(LOWERCASE, EDGENGRAM10))
                .Custom(EMAILADDRESSSEARCH, sa => sa
                     .Tokenizer(EMAILADDRESS).Filters(LOWERCASE,TRUNCATE10))
                );

            CreateIndexResponse createFileResponse = client.Indices.Create(FSOemail.indexname, i => i
            .Settings(s => s
                .NumberOfReplicas(0)
                .NumberOfShards(shards)
                .RefreshInterval(RefreshInterval)
                .Analysis(analysis => analysis = emailAnalysisDescriptor)
                .DefaultPipeline(InitializationPipeline.PIPEEmail)
                .FinalPipeline(InitializationPipeline.PIPEvalidate)
                )                
            .Map<FSOemail>(map => map      
                .Properties(
                    property => property = GetDefaultPropertyMappingDescriptor<FSOemail>()
                        //[Text(Analyzer = InitializationIndex.EMAILADDRESS, SearchAnalyzer = InitializationIndex.EMAILADDRESSSEARCH)]
                        .Text(p => p.Name(n => n.From)
                        .Analyzer(EMAILADDRESS)
                        .SearchAnalyzer(EMAILADDRESSSEARCH)
                        )
                        .FieldAlias(f => f.Name("sender").Path(p => p.From))
                    )
                .AutoMap()
                .Dynamic(false)
                )
            ); 
            WriteResponse(createFileResponse, false);
        }


        private void BuildIndexFsoFile()
        {
            CreateIndexResponse createFileResponse = client.Indices.Create(FSOfile.indexname, i => i
                .Settings(s => s
                    .NumberOfReplicas(0)//Increase replicas once in production and bulk indexing is complete.
                    .NumberOfShards(1)// It's an option to increase shards later as required.
                    .RefreshInterval(RefreshInterval)//Default is 1s refresh rate
                    .Analysis(a => a = GetCommonAnalysisDescriptor())
                    .FinalPipeline(InitializationPipeline.PIPEvalidate)
                    )
                .Map<FSOfile>(map => map
                    .Properties(property => property = GetDefaultPropertyMappingDescriptor<FSOfile>())
                    .AutoMap<FSOfile>()
                    .Dynamic(false)                    
                    )
            );
            WriteResponse(createFileResponse, false);
        }
    
        public void BuildIndexFSODocument(bool runningInteractively)
        {
            int shards = runningInteractively ? GetUserInteractiveInput("How many shards for document index?", 1, 64) : 1;
            CreateIndexResponse createFileResponse = client.Indices.Create(FSOdocument.indexname, i => i
                .Settings(s => s
                   .NumberOfReplicas(0)
                   .NumberOfShards(shards)//maybe we should either check how many nodes and calculate or prompt?
                   .RefreshInterval(RefreshInterval)//default is 1s refresh rate
                   .Analysis(a => a = GetCommonAnalysisDescriptor())
                   .DefaultPipeline(InitializationPipeline.PIPEDocument)
                   .FinalPipeline(InitializationPipeline.PIPEvalidate)
                    )
                .Map<FSOdocument>(map => map                    
                    .Properties(property => property = GetDefaultPropertyMappingDescriptor<FSOdocument>())
                    .AutoMap<FSOdocument>()
                    .Dynamic(false)                    
                )
           );
            WriteResponse(createFileResponse, false);
        }

        private TypeMappingDescriptor<T> GetTypeMappingDescriptor<T>() where T : class,IFSO
        {
            TypeMappingDescriptor<T> typeMappingDescriptor = new TypeMappingDescriptor<T>();
            typeMappingDescriptor.AutoMap(typeof(T));
            typeMappingDescriptor.Properties(properties => properties = GetDefaultPropertyMappingDescriptor<T>());
            return typeMappingDescriptor;
        }
        private PropertiesDescriptor<T> GetDefaultPropertyMappingDescriptor<T>() where T : class, IFSO
        {
            PropertiesDescriptor<T> propertyMappingDescriptor;
            propertyMappingDescriptor = new PropertiesDescriptor<T>();
            propertyMappingDescriptor
                        .Text(t => t
                            .Name(n => n.Id)
                            .Analyzer(SMB_PATH_Text)
                            .Fields(f => f
                                .Keyword(k => k.Name("keyword").Normalizer(LOWERCASE).IgnoreAbove(512))
                                //.Text(tx => tx.Name("smbtreelower").Analyzer(SMB_PATH_H))//temporary for backwards compatability - not referenced in delete directory descendents.Revisit 2022-05
                                )
                            )
                        .Text(t => t
                            .Name(n => n.Parent)
                            .Analyzer(SMB_PATH_Text)
                            .Fields(f => f
                                .Keyword(k => k.Name("keyword").Normalizer(LOWERCASE).IgnoreAbove(512))
                                .Text(tx => tx.Name("smbtreelower").Analyzer(SMB_PATH_H).SearchAnalyzer(SMB_PATH_H_Search))
                                .Text(tx => tx.Name("smbtreelowerreverse").Analyzer(SMB_PATH_H_REVERSE).SearchAnalyzer(SMB_PATH_H_Search))
                                )
                            )
                        .Text(t => t
                            .Name(n => n.Category)
                            .Analyzer(SMB_PATH_Text)
                            .Fields(f => f
                                .Keyword(k => k.Name("keyword").Normalizer(LOWERCASE).IgnoreAbove(512))
                                )
                            )
                        .Object<ProjectId>(o=>o
                            .Name(n=>n.Project)
                            .AutoMap()
                            .Properties(p=>p                            
                            .Text(t=>t
                                .Name(n=>n.FullName)//in v3 maybe we could make project.number have a keyword field.
                                .Analyzer(NONWHITESPACEEDGE)
                                .SearchAnalyzer(NONWHITESPACEEDGESEARCH)
                                .Fields(f=>f
                                    .Keyword(k=>k.Name("keyword").Normalizer(LOWERCASE))//specifying keyword name results in 
                                    )
                                )
                            .Text(t => t
                                .Name(n=>n.Number)//in v3 maybe we could make project.number have a keyword field.
                                .Analyzer(NONWHITESPACEEDGE)
                                .SearchAnalyzer(NONWHITESPACEEDGESEARCH)
                                .Fields(f => f
                                    .Keyword(k => k.Name("keyword"))
                                    )
                                )
                            )   
                        )
                        .FieldAlias(fa => fa
                            .Name("wBS1")
                            .Path(pa=>pa.Project.Number)
                        )
                        ;
            return propertyMappingDescriptor;
        }
  

        /// <summary>
        /// In future, we could compare the analyzer in code with cluster and warn on mismatch
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="t1"></param>
        /// <param name="analysisDescriptor"></param>
        /// <returns></returns>
        private bool AnalysisDescriptorIsStd<T>(T t1, AnalysisDescriptor analysisDescriptor) where T : FSO
        {
            var indexname = t1.IndexName;
            var t = client.Indices.GetSettings(indexname);//, s => s.Index(FSOemail.indexname));
            var tt = t.Indices[indexname];
            var analysis = tt.Settings.Analysis;
            var mapping = tt.Mappings;
            foreach (var a in analysis.Analyzers)
            {
                var methods = a.GetType().GetMembers();
                if (ildebug) _il.LogDebugInfo(a.Key, a.Value?.Type, methods);
            }
            return true;
        }

        private AnalysisDescriptor GetCommonAnalysisDescriptor()
        {
            var analysisDescriptor = new AnalysisDescriptor();
            //Get standard SMB path tokenizer common to all indicies as well as set up additional tokenizer that is common
            analysisDescriptor.Tokenizers(tok => tok = GetCommonTokenizersDescriptor());
            analysisDescriptor.Analyzers(an => an = GetCommonAnalyzerDescriptor());
            analysisDescriptor.TokenFilters(tf => tf = GetCommonTokenFiltersDescriptor());
            analysisDescriptor.Normalizers(norm => norm = GetLowerCaseNormalizer());
            return analysisDescriptor;
        }

        private NormalizersDescriptor GetLowerCaseNormalizer()
        {
            var n = new NormalizersDescriptor();
            n.Custom(LOWERCASE, c => c.Filters(LOWERCASE));
            return n;
        }

        private TokenizersDescriptor GetCommonTokenizersDescriptor()
        {
            //Tokenizers
            var smb_path = new PathHierarchyTokenizer()
            {
                Delimiter = '\\',
                Skip = 0//todo see if a value of 2 still works
            };
            var smb_path_reverse = new PathHierarchyTokenizer()
            {
                Delimiter = '\\',
                Reverse = true
            };
            var smb_path_text = new PatternTokenizer()
            {//is this a SimplePattern...no so we have to use Pattern but specify to capture the group?
             // Pattern = @"([+]|([a-zA-Z0-9]+))",
                Pattern = @"([+]|[\w\.\'\-]+)",
                Group = 1
            };
            //TokenDescriptor to hold Tokenizers
            var tokenizerDescriptor = new TokenizersDescriptor();
            tokenizerDescriptor.UserDefined(SMB_PATH_H, smb_path);
            tokenizerDescriptor.UserDefined(SMB_PATH_H_REVERSE, smb_path_reverse);
            tokenizerDescriptor.UserDefined(SMB_PATH_Text, smb_path_text);
            tokenizerDescriptor.CharGroup(NONWHITESPACEEDGE, c => c.TokenizeOnCharacters(" "));
            return tokenizerDescriptor;
        }

        private AnalyzersDescriptor GetCommonAnalyzerDescriptor()
        {
            var analyzerDescriptor = new AnalyzersDescriptor();
            analyzerDescriptor.Custom(SMB_PATH_H, z => z.Filters(LOWERCASE).Tokenizer(SMB_PATH_H));
            analyzerDescriptor.Custom(SMB_PATH_H_REVERSE, z => z.Filters(LOWERCASE).Tokenizer(SMB_PATH_H_REVERSE));
            analyzerDescriptor.Custom(SMB_PATH_Text, z => z.Filters(LOWERCASE).Tokenizer(SMB_PATH_Text));
            analyzerDescriptor.Custom(SMB_PATH_H_Search, z => z.Filters(LOWERCASE).Tokenizer(KEYWORD));
            analyzerDescriptor.Custom(NONWHITESPACEEDGE, a => a
                .Tokenizer(NONWHITESPACEEDGE)
                .Filters(LOWERCASE, EDGENGRAM10));
            analyzerDescriptor.Custom(NONWHITESPACEEDGESEARCH, a => a
                .Tokenizer(NONWHITESPACEEDGE)
                .Filters(LOWERCASE, TRUNCATE10));
            return analyzerDescriptor;
        }

        private TokenFiltersDescriptor GetCommonTokenFiltersDescriptor()
        {
            var descriptor = new TokenFiltersDescriptor();
            descriptor.EdgeNGram(EDGENGRAM10, e => e
                         .MinGram(1)
                         .MaxGram(10));
            descriptor.Truncate(TRUNCATE10, t => t
                .Length(10));
            return descriptor;
        }
    }
}
