using HOK.Elastic.DAL.Models;
using Nest;
using System;

namespace HOK.Elastic.DAL
{
    public class IndexNameHelper
    {
        private string _indexPrefix;
        private string _indexPrefixWildcard;
        /// <summary>
        /// Pass the indexprefix that will be used to generate the correct index names for reading and writing to the cluster.
        /// </summary>
        /// <param name="indexPrefix">Prefix to be used for all index names (without the '*' wildcard) </param>
        public IndexNameHelper(string indexPrefix)
        {
            if (indexPrefix.EndsWith("*")) throw new ArgumentException( $"{nameof(indexPrefix)} shouldn't end in '*'");
            if(string.IsNullOrEmpty(indexPrefix)) throw new ArgumentException($"{nameof(indexPrefix)} can't be empty");
            _indexPrefix = indexPrefix;
            _indexPrefixWildcard = indexPrefix + "*";
            _allIndexNames = new string[] { _indexPrefix + "dir", _indexPrefix + "fsofile", _indexPrefix + "fsomsg", _indexPrefix + "fsodoc" };
        }
        private string[] _allIndexNames;
        public string[] AllIndexNames => _allIndexNames;
        public string IndexNameDir => _allIndexNames[0];
        public string IndexNameFsoFile => _allIndexNames[1];
        public string IndexNameFsoMsg => _allIndexNames[2];
        public string IndexNameFsoDoc => _allIndexNames[3];
        /// <summary>
        /// Returns the index prefix without the '*' wildcard.
        /// </summary>
        public string Prefix => _indexPrefix;
        /// <summary>
        /// Returns the index prefix with a '*' wildcard suffix.
        /// </summary>
        public string PrefixWildcard => _indexPrefixWildcard;

        /// <summary>
        /// Utility to get the correct indexname for the type of file/document being inserted into elastic.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        public string GetNameFor<T>() where T : class, IFSO
        {
            var t = typeof(T);
            if (t.Name == nameof(FSOdirectory))
            {
                return IndexNameDir;
            }
            else if (t.Name == nameof(FSOfile))
            {
                return IndexNameFsoFile;
            }
            else if (t.Name == nameof(FSOemail))
            {
                return IndexNameFsoMsg;
            }
            else if (t.Name == nameof(FSOdocument))
            {
                return IndexNameFsoDoc;
            }
            throw new NotSupportedException($"No Index Name Support for {t.FullName}");
        }
    }
}
