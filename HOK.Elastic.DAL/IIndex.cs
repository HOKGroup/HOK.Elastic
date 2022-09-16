using HOK.Elastic.DAL.Models;

namespace HOK.Elastic.DAL
{
    public interface IIndex : IBase
    {
        long DeleteDirectoryDescendants(string directoryPublishedPath, string[] indicies);
        long Delete(string key, string index);
        long Delete(string[] key, string index);
        void Insert<T>(T item) where T : class, IFSO;
        void Update<T>(T item) where T : class, IFSO;
        void InsertEmail(FSOemail item);
        void InsertTikaDoc(FSOdocument item);
        void BulkInsert(IFSO[] dws, bool crawlContent = false);
    }
}