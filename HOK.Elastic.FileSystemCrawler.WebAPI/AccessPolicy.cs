using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using System;
using Microsoft.Extensions.Configuration;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
    public static class AccessPolicy
    {
 

        /// <summary>
        /// This is a work in progress but we could have a dictionary of polices and an easy way of referring to them in various ways (as objects or strings)
        /// </summary>
        public static class PolicyNames
        {
            public const string Default = "Default";
        }

        public static Dictionary<string, AuthorizationPolicy> Policies = new Dictionary<string, AuthorizationPolicy>(){
             { PolicyNames.Default, new AuthorizationPolicyBuilder().RequireRole(Program.Config["GrantGroup"]).Build() }
};


private static HashSet<string> _exemptedEndPoints = new HashSet<string>() { };
        /// <summary>
        /// All paths not declared with authorization attributes should deny
        /// </summary>
        /// <param name="ahc"></param>
        /// <returns></returns>
        public static bool GetFallBack(AuthorizationHandlerContext ahc)
        {
            var httpContext = ahc.Resource as HttpContext;
            if (httpContext != null)
            {
                if (httpContext.Request.Path.HasValue)
                {
                    var isExempt = _exemptedEndPoints.Where(x => httpContext.Request.Path.Value.EndsWith(x, StringComparison.OrdinalIgnoreCase)).Any();
                    return isExempt;
                }
            }
            return false;
        }
    }
}
