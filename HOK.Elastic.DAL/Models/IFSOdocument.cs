using Nest;

namespace HOK.Elastic.DAL.Models
{
    public interface IFSOdocument : IFSOfile
    {
        Attachment Attachment { get; set; }
        byte[] Content { get; set; }
    }
}