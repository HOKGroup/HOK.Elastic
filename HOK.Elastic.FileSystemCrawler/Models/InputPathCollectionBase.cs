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
    public class InputPathCollectionBase : InputPathCollectionBaseAbstract, ICollection<IInputPathBase>
    {
        internal Dictionary<string, IInputPathBase> _items = new Dictionary<string, IInputPathBase>();//we'll use dictionary for events for fast lookup
        public bool IsReadOnly => false;

        public override int Count
        {
            get { return _items.Count; }
        }
 

        public void Clear()
        {
            _items.Clear();
        }

        public bool Contains(IInputPathBase item)
        {
            return _items.Keys.Contains(item.Path);
        }

        public void CopyTo(IInputPathBase[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<IInputPathBase> GetEnumerator()
        {
            return _items.Values.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        public void Add(IInputPathBase value)
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

        public bool Remove(IInputPathBase item)
        {
            if (item == null) return false;
            return _items.Remove(item.Path);
        }
        public void RemoveWhere(Predicate<IInputPathBase> predicate)
        {
            var keysToRemove = _items.Where(x => predicate(x.Value)).Select(x=>x.Key);
            foreach(var key in keysToRemove)
            {
                _items.Remove(key);
            }
        }
    }
}