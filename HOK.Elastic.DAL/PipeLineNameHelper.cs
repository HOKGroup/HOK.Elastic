namespace HOK.Elastic.DAL
{
    /// <summary>
    /// Utility service used to get standardized names when configuring the ingest pipelines during initial setup of indicies(if required)
    /// </summary>
    public class PipeLineNameHelper
    {
        public string _prefix;
        public PipeLineNameHelper(string prefix)
        {
            _prefix = prefix;
        }

        public string PIPEEmail
        {
            get
            {
                return _prefix + "pipe_email";
            }
        }
        /// <summary>
        /// Conditional Pipeline that decides sends document to categorization pipeline if category is unpopulated.
        /// </summary>
        public string PIPEvalidate
        {
            get
            {
                return _prefix + "pipe_validate";
            }
        }
        public string PIPECategorizationProjectExtractRgx { get; set; } = "(^$)";
        public string PIPECategorizationProject
        {
            get
            {
                return _prefix + "pipe_categoryproject";
            }
        }
        public string PIPEDocument
        {
            get
            {
                return _prefix + "pipe_document";
            }
        }

        public string PIPEofficecatproject
        {
            get
            {
                return _prefix + "pipe_officeprojectcategory";
            }
        }
    }
}
