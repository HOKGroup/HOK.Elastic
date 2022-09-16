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
    public class InputPathCollectionBase<T> : InputPathCollectionBaseAbstract<T>, ICollection<T> where T : IInputPathBase
    {
        internal Dictionary<string, T> _items = new Dictionary<string, T>();//we'll use dictionary for events for fast lookup
        public bool IsReadOnly => false;

        public override int Count
        {
            get { return _items.Count; }
        }
 

        public void Clear()
        {
            _items.Clear();
        }

        public bool Contains(T item)
        {
            return _items.Keys.Contains(item.Path);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _items.Values.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        public void Add(T value)
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

        public bool Remove(T item)
        {
            if (item == null) return false;
            return _items.Remove(item.Path);
        }
        public void RemoveWhere(Predicate<T> predicate)
        {
            var keysToRemove = _items.Where(x => predicate(x.Value)).Select(x=>x.Key);
            foreach(var key in keysToRemove)
            {
                _items.Remove(key);
            }
        }
    }
}