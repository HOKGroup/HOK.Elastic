using HOK.Elastic.DAL.Models;
using Microsoft.Extensions.Logging;
using Nest;
using System;

namespace HOK.Elastic.DAL
{
    public partial class InitializationBase : Base
    {

        public InitializationBase(PipeLineNameHelper pipeLineNameHelper, IndexNameHelper indexNameHelper, Uri elastiSearchServerUrl, Logger.Log4NetLogger logger) : base(pipeLineNameHelper, indexNameHelper,elastiSearchServerUrl, logger)
        {

        }
    }
}
