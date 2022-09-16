using HOK.Elastic.DAL.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    /// <summary>
    /// collection of inputpaths used for full,incremental and missing content crawls
    /// </summary>
    public class InputPathCollectionCrawl<T> : InputPathCollectionBase<InputPathBase>
    {
        public InputPathCollectionCrawl(ICollection<InputPathBase> inputPaths)
        {
            AddRange(inputPaths);
        }
        public new void Add(InputPathBase value)
        {
            AddRange(new InputPathBase[] { value });
        }

        public void AddRange(ICollection<InputPathBase> values)
        {
            foreach (var value in values)
            {
                ValidateInputPath(value);
                if (_items.ContainsKey(value.Path))
                {
                    _items[value.Path] = value;
                }
                else
                {
                    _items.Add(value.Path, value);
                }
            }
            //remove any duplicates or items that are subpaths of the next path(If given similar paths (for example from unfinished paths...we don't want to crawl the parent folder AND the child folder) 
            List<string> keys = new List<string>();
            foreach(var item in _items)
            {
                keys.AddRange(_items.Keys.Where(x => x.StartsWith(item.Key + '\\', StringComparison.OrdinalIgnoreCase)));
               
            }
            foreach (var key in keys)
            {
                _items.Remove(key);
            }
        }

        private bool ValidateInputPath(InputPathBase inputPath)
        {
            if (PathHelper.CrawlRoot == null) return true;
            if (inputPath != null)//these might be null during serialization
            {
                if (!string.IsNullOrEmpty(inputPath.Path) && inputPath.Path.StartsWith(PathHelper.CrawlRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                else
                {
                    throw new ArgumentException(string.Format("'{0}' must start with '{1}'", inputPath.Path, PathHelper.CrawlRoot), nameof(inputPath));
                }
            }
            else
            {
                throw new ArgumentException("Null", nameof(inputPath));
            }
        }

        public static explicit operator InputPathCollectionCrawl<T>(List<InputPathBase> v)
        {
            InputPathCollectionCrawl<T> i = new InputPathCollectionCrawl<T>(v);
            return i;
        }
    }
}