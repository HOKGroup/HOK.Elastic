using HOK.Elastic.DAL.Models;
using System;
using System.Collections.Generic;

namespace HOK.Elastic.DAL
{
    public interface IDiscovery : IBase
    {
        IEnumerable<IFSO> FindDescendentsForMoving(string path);
        IEnumerable<T> GetIFSOdocumentsLackingContentV2<T>(string directoryPublishedPath, int retries, DateTime? dateTime = null) where T : class, IFSOdocument;
        IEnumerable<T> GetIFSOsByQuery<T>(string jsonQueryString, int retries, DateTime? dateTime = null) where T : class, IFSO;
        IEnumerable<T> FindGuardianDocuments<T>(string fullName, DateTime from, DateTime to, int pageSize = 100) where T : class, IFSO;
        /// <summary>
        /// Searches the default index for the document type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="indexName"></param>
        /// <returns></returns>
        T GetById<T>(string id, string indexName) where T : class, IFSO;
        DirectoryContents FindRootAndChildren(string id,bool includefullSource=false);
        IEnumerable<T> FindDescendentsForMoving<T>(string path,int pageSize) where T : class, IFSO;
        bool ValidateJsonStringQuery(string jsonQueryString);
    }
}