using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    /// <summary>
    /// Basic implementation of InputPathCollection that stores the items based on path field.
    /// </summary>
    public class InputPathCollectionBase : InputPathCollectionBaseAbstract, ICollection<InputPathBase>
    {
        internal Dictionary<string, InputPathBase> _items = new Dictionary<string, InputPathBase>();//we'll use dictionary for events for fast lookup
        public bool IsReadOnly => false;

        public override int Count
        {
            get { return _items.Count; }
        }
 

        public void Clear()
        {
            _items.Clear();
        }

        public bool Contains(InputPathBase item)
        {
            return _items.Keys.Contains(item.Path);
        }

        public void CopyTo(InputPathBase[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<InputPathBase> GetEnumerator()
        {
            return _items.Values.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        public void Add(InputPathBase value)
        {
            if (Contains(value))
            {
                _items[value.Path] = value;
            }
            else
            {
                _items.Add(value.Path, value);
            }
        }

        public bool Remove(InputPathBase item)
        {
            if (item == null) return false;
            return _items.Remove(item.Path);
        }
        public void RemoveWhere(Predicate<InputPathBase> predicate)
        {
            var keysToRemove = _items.Where(x => predicate(x.Value)).Select(x=>x.Key);
            foreach(var key in keysToRemove)
            {
                _items.Remove(key);
            }
        }
    }
}