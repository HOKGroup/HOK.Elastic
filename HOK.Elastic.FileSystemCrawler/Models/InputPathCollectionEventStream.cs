using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    /// <summary>
    /// Collection of inputpaths for the Crawler but only used in the nasuniapi as well as an associated PathSubstitution object that can be used to build alternate paths (DFS/UNC path, alternate Nasuni filer source)
    /// </summary>
    public class InputPathCollectionEventStream : InputPathCollectionBase
    {

        private int _duplicates = 0;
        public int DuplicatesCount { get { return _duplicates; } }
        internal new Dictionary<string, InputPathEventStream> _items = new Dictionary<string, InputPathEventStream>();//we'll use dictionary for events for fast lookup
        public IQueryable<InputPathEventStream> Query { get { return _items.Values.AsQueryable(); } }

        public InputPathCollectionEventStream() : base()
        {
        }
        public void Add(InputPathEventStream[] newItems)
        {
            if (newItems != null)
            {
                for (int i = 0; i < newItems.Length; i++)
                {
                    Add(newItems[i]);
                }
            }
        }
        public new void Add(InputPathEventStream newItem)
        {
            if (newItem == null) return;

            if (newItem.PresenceAction == ActionPresence.Move && newItem.PathFrom != null)
            {
                //newItem is a Move
                if (newItem.IsDir)
                {
                    //newItem is Move and Directory...therefore, update any affected items currently in the collection. Always ensure events passed to this method are newer than existing items.
                    var keys = _items.Keys.Where(x => x.StartsWith(newItem.PathFrom + '\\', StringComparison.InvariantCultureIgnoreCase) || x.Equals(newItem.PathFrom, StringComparison.InvariantCultureIgnoreCase)).ToList();

                    foreach (var key in keys)
                    {
                        if (_items.TryGetValue(key, out InputPathEventStream affectedItem))
                        {
                            affectedItem.Path = affectedItem.Path.Replace(newItem.PathFrom, newItem.Path);
                            _items.Remove(key);//take out the old item under the old key
                            AddProcessedItem(affectedItem);//insert it back into a new place..but use the add subroutine to check for existing item conflicts.
                        }
                    }
                }
                else
                {
                    //newItem is Move File...check if affecting a file event in the queue for the source/pathFrom...
                    if (_items.TryGetValue(newItem.PathFrom, out InputPathEventStream affectedItem))//
                    {
                        affectedItem.PresenceAction = ActionPresence.Delete;
                        AddProcessedItem(affectedItem);//insert it back into a new place..but use the add subroutine to check for existing item conflicts.
                    }
                    if (HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreFile(newItem.PathFrom))
                    {
                        newItem.PathFrom = null;
                        newItem.PresenceAction = ActionPresence.None;
                        newItem.ContentAction = ActionContent.Write;
                    }
                    else if (HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreFile(newItem.Path))
                    {
                        newItem.Path = newItem.PathFrom;
                        newItem.PresenceAction = ActionPresence.Delete;
                        newItem.PathFrom = null;
                    }
                }
            }
            else//not a move
            {
                if (newItem.IsDir ? HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreDirectory(newItem.Path) : HOK.Elastic.DAL.Models.PathHelper.ShouldIgnoreFile(newItem.Path))
                {
                    return;
                }
            }
            //Lastly, add the newItem.
            AddProcessedItem(newItem);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newItem"></param>
        private void AddProcessedItem(InputPathEventStream newItem)
        {
            string key = newItem.Path;
            if (!_items.TryGetValue(key, out InputPathEventStream existingItem))
            {
                _items.Add(key, newItem);//todo I think we could check if a pathto exists...and then update the item.
            }
            else
            {
                _duplicates++;
                if (existingItem.TimeStampUtc <= newItem.TimeStampUtc)//many events for a single file have the same timestamp. So process them in the order they are read.
                {
                    newItem = AggregateEvaluate(existingItem, newItem);
                }
                else
                {
                    newItem = AggregateEvaluate(newItem, existingItem);
                }
                _items[existingItem.Path] = newItem;//todo not sure why we don't use 'key' variable here.also could existingItem be null
            }
        }


        private static InputPathEventStream AggregateEvaluate(InputPathEventStream referenceItem, InputPathEventStream newerItem)
        {
            //sorted by date earliest to most recent.
            //path from is filtered exclusions.
            //Presence:
            if (newerItem.PresenceAction == ActionPresence.Delete)
            {
                referenceItem.PresenceAction = ActionPresence.Delete;
                referenceItem.ContentAction = ActionContent.None;
            }
            else if (newerItem.PresenceAction == ActionPresence.Move)
            {
                referenceItem.PresenceAction = ActionPresence.Move;
                if (string.IsNullOrEmpty(referenceItem.PathFrom)) referenceItem.PathFrom = newerItem.PathFrom;
                //carry the referenceItem's PathFrom forward (do not overwrite it as if any object is likely to exist in elastic(and therefore possibly retrieved and reinserted, it would be the PathFrom of the earliest event)
            }
            else
            {
                referenceItem.PresenceAction = ActionPresence.None;
            }
            //ContentAction:
            if (newerItem.ContentAction > referenceItem.ContentAction)//'upgrade' to content reading mode from either none, or setacl to write, but once set, never downgrade.
            {
                referenceItem.ContentAction = newerItem.ContentAction;
            }
            referenceItem.TimeStampUtc = newerItem.TimeStampUtc;
            return referenceItem;
        }

        public void TestVerifyOutput(string outputPath)
        {
            System.IO.File.WriteAllText(outputPath, "\r\n");
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            SemaphoreSlim ss = new SemaphoreSlim(1, 1);
            try
            {
                Parallel.ForEach(_items.Values, (auditEvent) =>
                {
                    auditEvent.Path = auditEvent.Path.Replace(@"\\?\\now\internal\", @"\\tor-01fs\canadmin\internal\");
                    auditEvent.PathFrom = auditEvent.PathFrom?.Replace(@"\\?\\now\internal\", @"\\tor-01fs\canadmin\internal\");
                    var mystring = auditEvent.TimeStampUtc.ToString() + "\t" + auditEvent.PresenceAction.ToString() + "\t" + auditEvent.ContentAction.ToString() + "\t" + auditEvent.Path + "\t" + auditEvent.PathFrom + "\r\n";
                    bool istrue = false;

                    if (auditEvent.PresenceAction == ActionPresence.Delete)
                    {
                        if (auditEvent.IsDir)
                        {
                            istrue = !System.IO.Directory.Exists(auditEvent.Path);
                        }
                        else
                        {
                            istrue = !System.IO.File.Exists(auditEvent.Path);
                        }
                    }
                    else
                    {
                        if (auditEvent.IsDir)
                        {
                            istrue = System.IO.Directory.Exists(auditEvent.Path);
                        }
                        else
                        {
                            istrue = System.IO.File.Exists(auditEvent.Path);
                        }
                    }
                    ss.Wait();
                    sb.Append(istrue.ToString() + "\t" + mystring);
                    ss.Release();
                }
                );
                System.IO.File.AppendAllText(outputPath, sb.ToString());
            }
            finally
            {
                ss.Dispose();
            }
        }

        /// <summary>
        /// ensure the paths are passed as lowercase
        /// </summary>
        /// <param name="item"></param>
        public bool RemoveAsLowerCase(string item)
        {
            if (string.IsNullOrEmpty(item)) return false;
            return _items.Remove(item);
        }
    }
}
