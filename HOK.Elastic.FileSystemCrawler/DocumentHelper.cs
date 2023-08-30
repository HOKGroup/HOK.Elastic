using HOK.Elastic.DAL;
using HOK.Elastic.DAL.Models;
using HOK.Elastic.Logger;
using Microsoft.Extensions.Logging;
using Nest;
using System;
using System.IO;
using System.Linq;

namespace HOK.Elastic.FileSystemCrawler
{
    public class DocumentHelper
    {
        private bool ildebug, ilinfo, ilwarn, ilerror;
        private bool readFileContents;
        private readonly string MachineName;
        private HOK.Elastic.Logger.Log4NetLogger _il;
        private DAL.IIndex _indexNode;
        private SecurityHelper sh;
        private readonly int _readLimitKBEmail = 200000;///<200MB is generally a reasonable size...there is one email in the Shire that is 300MB...but it says 'invalid structured storage' when opened.
        //if((fsodoc.Extension==".pdf"||fsodoc.Extension==".pptx")&&fsodoc.LengthKB>175000)//175MB....250MB PDF results in 1GB http payload which is beyond capacity
        //475MB works OK with 1900mb http.maxcontent limit.
        //       if (fsodoc.LengthKB > 275000)//175MB....250MB PDF results in 1GB http payload which is beyond capacity
        private readonly int _readLimitKBTika = 275000;

        public DocumentHelper(bool ReadFileContents, SecurityHelper securityHelper, IIndex indexNode, Log4NetLogger logger = null)
        {
            _il = logger;
            ildebug = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug);
            ilinfo = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information);
            ilwarn = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Warning);
            ilerror = _il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error);
            _indexNode = indexNode;
            readFileContents = ReadFileContents;
            MachineName = Environment.MachineName;
            sh = securityHelper;
        }
        public void Insert(IFSO item)
        {
            try
            {
                _indexNode.Insert(item);
            }
            catch (Exception ex)
            {
                if (ilerror) _il.LogErr("Insert", item.Id, null, ex);
            }
        }

        public void Insert(IFSO[] items)
        {
            try
            {
                _indexNode.BulkInsert(items);
            }
            catch (Exception ex)
            {
                if (ilerror) _il.LogErr("Bulk Insert", "", items.Length, ex);
            }
        }

        public void Update(IFSO item)
        {
            try
            {
                _indexNode.Update(item);
            }
            catch (Exception ex)
            {
                if (ilerror) _il.LogErr("Update", item.Id, null, ex);
            }
        }

        public bool IsBatchable(IFSO item)
        {
            if (item == null)
            {
                return false;
            }
            if (!readFileContents)
            {
                return true;
            }
            Type type = item.GetType();

            if (type != typeof(FSOdocument))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        /// <summary>
        /// Returns null if path doesn't exist,otherwise will return either FSOFile or FSODirectory with ACLs populated. 
        /// If isDir is not specified, check if path exists as file, then as a directory, otherwise null. 
        /// If isDir is specified(not null) look for file or directory as directed by the bool.
        /// </summary>
        /// <param name="path"></param>
        /// <returns>Basic document that still needs to be enhanced prior to insertion in index as it is missing required field (example: indexname)</returns>
        public IFSO MakeBasicDoc(string path, bool? isDir = null)
        {
            if (string.IsNullOrEmpty(path)) return null;
            IFSO doc;
            path = LongPaths.GetLegacyLongPath(path);
            bool shouldCheckFileExists = !isDir.HasValue || isDir.Value == false;
            bool shouldCheckDirectoryExists = !isDir.HasValue || isDir.Value == true;

            if (shouldCheckFileExists && File.Exists(path))
            {
                var fi = new FileInfo(path);
                doc = new FSOfile(fi)
                {
                    Acls = sh.GetDocACLs(fi)
                };
            }
            else if (shouldCheckDirectoryExists && Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                doc = new FSOdirectory(di)
                {
                    Acls = sh.GetDocACLs(di)
                };
            }
            else
            {
                doc = null;
            }
            return doc;
        }


        /// <summary>
        /// Calls InsertTransformFile if appropriate
        /// </summary>
        /// <param name="ifso"></param>
        /// <returns></returns>
        public IFSO InsertTransform(IFSO ifso)
        {
            ifso.Timestamp = DateTime.UtcNow;
            ifso.MachineName = MachineName;
            ifso.Version = StaticIndexPrefix.Version;
            if (ifso is FSOfile)
            {
                return InsertTransformFile(ifso as FSOfile);
            }
            else
            { 
                ifso.IndexName = FSOdirectory.indexname;
                return ifso;
            }
        }

        public IFSO InsertTransformFile(IFSOfile fsofile)
        {
            var fi = new FileInfo(fsofile.PathForCrawling);
            fsofile.Owner = SecurityHelper.GetOwner(fi);//TODO this seems to be pre-populated maybe we don't need to populate it twice.
            if (FSOemail.CanBeMadeFrom(fi))
            {
                var fsoEmail = new FSOemail(fsofile);
                fsoEmail.IndexName = FSOemail.indexname;
                if (!readFileContents)
                {
                    return fsoEmail;
                }
                else
                {
                    if (fsoEmail.LengthKB > _readLimitKBEmail)
                    {
                        fsoEmail.FailureCount = 4;
                        fsoEmail.FailureReason = "Content too large";
                        return fsoEmail;
                    }
                    try
                    {
                        ////// FYI default TIKA output generally looks something like this:                            
                        //////      "attachment" : {
                        //////          "date" : "2021-04-27T16:19:03Z",
                        //////          "content_type" : "application/vnd.ms-outlook",
                        //////          "author" : "James Blackadar",
                        //////          "language" : "en",
                        //////          "title" : "Nasuni CIFS locks",
                        //////          "content" : """Nasuni CIFS locks
                        //////From
                        //////John Doe
                        //////To
                        //////Justin Bob; Alice Doe; Dan Siroky
                        //////Cc
                        //////James Blackader
                        //////Recipients
                        //////justin.bob@example.com; alice.doe@hok.com; dan.siroky@example.com; james.blackader@example.com
                        //////Content body
                        string filename = fsoEmail.PathForCrawlingContent;
                        using (MsgReader.Outlook.Storage.Message eml = new MsgReader.Outlook.Storage.Message(filename))
                        {
                            fsoEmail.From = eml.GetEmailSender(false, false).ToLowerInvariant();
                            var recipientsTo = eml.GetEmailRecipients(MsgReader.Outlook.RecipientType.To, false, false);
                            var recipientsCc = eml.GetEmailRecipients(MsgReader.Outlook.RecipientType.Cc, false, false);
                            var recipientsList = recipientsTo.Split(';').Select(x => x.Trim().ToLowerInvariant()).ToList();
                            fsoEmail.To = recipientsList;
                            recipientsList.AddRange(recipientsCc.Split(';').Where(x=>!string.IsNullOrEmpty(x)).Select(x => x.Trim().ToLowerInvariant()));
                            fsoEmail.AllRecipients = recipientsList.Distinct().ToList();
                            fsoEmail.SentUTC = eml.SentOn;
                            fsoEmail.ConversationIndex = eml.ConversationIndex;
                            fsoEmail.AttachmentNames = eml.GetAttachmentNames();
                            string content;
                            if (!string.IsNullOrEmpty(eml.BodyText))
                            {
                                content = eml.BodyText;
                            }
                            else
                            {
                                content = string.Empty;
                            }

                            fsoEmail.Attachment = new Attachment()
                            {
                                Content = content,
                                ContentLength = content.Length,
                                ContentType = "application/vnd.ms-outlook",
                                Language = "en",//TODO see if we can detect language
                                Author = eml.Sender.DisplayName,//maybe extract from eml.sender.email 
                                Date = eml.SentOn,
                                //Name = eml.SubjectNormalized,//Name is not used by default in the Elastic Tika ingestion engine.
                                Title = eml.SubjectNormalized,//Elastic Ingest Plugin populates this field by default...we should use the same for compatibility.
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is OpenMcdf.CFException)
                        {
                            //these are expected exceptions and so we can just warn
                            if (ilwarn) _il.LogWarn("Error reading email", fsoEmail.PathForCrawlingContent, ex.Message);
                        }
                        else if (ilerror)
                        {
                            _il.LogErr("Error reading email", fsoEmail.PathForCrawlingContent, ex.Message, ex);
                        }
                        fsoEmail.Attachment = new Attachment();//empty attachment so failed document can still be inserted into the index for re-processing later.
                        fsoEmail.FailureCount++;
                        fsoEmail.FailureReason = ex.Message;
                    }
                    return fsoEmail;
                }
            }
            else if (FSOdocument.CanBeMadeFrom(fi))
            {
                var fsodoc = new FSOdocument(fsofile);
                fsodoc.IndexName = FSOdocument.indexname;
                if (fsodoc.LengthKB > _readLimitKBTika)
                {
                    fsodoc.FailureReason = "Content too large";
                    fsodoc.FailureCount = 4;
                }
                else
                {
                    if (readFileContents)
                    {
                        try
                        {
                            fsodoc.Content = fsodoc.GetContent();
                        }
                        catch (Exception ex)
                        {
                            if (ilerror)
                            {
                                _il.LogErr("error reading content", fsodoc.PathForCrawlingContent, null, ex);
                            }
                            return null;
                        }
                    }
                }
                return fsodoc;
                //we'll try and get the content at the last minute at index time.
            }
            else
            {
                fsofile.IndexName = FSOfile.indexname;
                return fsofile;
            }
        }

        public IFSO ReindexTransform(IFSO ifso)
        {
            if (ifso == null)
            {
                return null;
            }
            ifso.Timestamp = DateTime.UtcNow;
            ifso.MachineName = MachineName;
            //TODO do we want to update version when reindexing....I don't think so.        
            if (ifso is FSOdirectory)
            {
                ifso.IndexName = FSOdirectory.indexname;
                //In future, maybe we just want the document object to tell us the indexname instead of setting it.
                //But maybe not in case we want to have different index by office (although we could do that inside the object as well)
            }
            else if (ifso is FSOfile)
            {
                var fi = new FileInfo(ifso.PathForCrawling);
                if (FSOemail.CanBeMadeFrom(fi))
                {
                    ifso.IndexName = FSOemail.indexname;
                }
                else if (FSOdocument.CanBeMadeFrom(fi))
                {
                    ifso.IndexName = FSOdocument.indexname;
                }
                else
                {
                    ifso.IndexName = FSOfile.indexname;
                }
            }
            else
            {
                if (ilwarn) _il.LogWarn("not supported for reindextransform", ifso.Id, ifso.GetType());
            }
            return ifso;
        }
    }
}
