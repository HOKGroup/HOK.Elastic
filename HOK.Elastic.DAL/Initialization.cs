using Microsoft.Extensions.Logging;
using Nest;
using System;

namespace HOK.Elastic.DAL
{
    public partial class Initialization : Base
    {
        public Initialization(Uri elastiSearchServerUrl, Logger.Log4NetLogger logger) : base(elastiSearchServerUrl, logger)
        {
        }


        public string Prefix
        {
            set => DAL.StaticIndexPrefix.Prefix = value;
        }


    }
}
