namespace HOK.Elastic.DAL.Models
{
    public interface IFSOfile : IFSO
    {
        string Extension { get; set; }
        string Owner { get; set; }
        double LengthKB { get; set; }
    }
}