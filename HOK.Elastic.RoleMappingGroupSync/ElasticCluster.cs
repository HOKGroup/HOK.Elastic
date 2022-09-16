using Elasticsearch.Net;
using Nest;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HOK.Elastic.RoleMappingGroupSync
{
    partial class Program
    {
        public class Elastic : HOK.Elastic.DAL.Base
        {
            private string _SIDPrefixBase;
            public Elastic(string SIDPrefixBase, IConnectionPool connectionPool, Logger.Log4NetLogger logger) : base(connectionPool, logger)
            {
                if (string.IsNullOrEmpty(SIDPrefixBase)) throw new ArgumentException(nameof(SIDPrefixBase) + " can't be null");
                _SIDPrefixBase = SIDPrefixBase;
            }


            /// <summary>
            /// PUT /_security/role/(roleName)
            /// {
            ///      "indices" : 
            ///       [
            ///             {
            ///                "names" : [ "*" ],
            ///                "privileges" : [ "read" ],
            ///                 "query" : {
            ///                    "terms" : { "access" : [tokenGroupSids...] }
            ///                 }
            ///             }
            ///       ]
            /// } 
            /// </summary>
            /// <param name="roleName"></param>
            /// <param name="tokenGroupSids"></param>
            /// <param name="key"></param>
            public void PutUserRole(string roleName, List<string> tokenGroupSids, string key)
            {
                var roleRequest = new PutRoleRequest(roleName);
                var indiciesPrivileges = new List<IndicesPrivileges>();
                var privileges = new List<string>() { "read" };
                int maxGroups = 320;
                QueryContainer queryContainer;

                if (tokenGroupSids.Count > maxGroups)
                {
                    // Split tokenGroupSids into those with a common base (BASE_ID)
                    // and those with a unique base (for the Terms Query)
                    var tokenGroupSidsCommonBase = tokenGroupSids.Where(x => x.StartsWith(_SIDPrefixBase));

                    var tokenGroupSidsUniqueBase = tokenGroupSids.Except(tokenGroupSidsCommonBase);

                    // Setup base Terms Query for acls.guardian and acls.this
                    var shouldAclsGuardian = new List<QueryContainer>
                    {
                        new TermsQuery
                        {
                            Field = "acls.guardian",
                            Terms = tokenGroupSidsUniqueBase

                        }
                    };
                    var shouldAclsThis = new List<QueryContainer>
                    {
                        new TermsQuery
                        {
                            Field = "acls.this",
                            Terms = tokenGroupSidsUniqueBase

                        }
                    };

                    var subIds = tokenGroupSidsCommonBase.Select(x => x.Replace(_SIDPrefixBase, ""));
                    shouldAclsGuardian = addRegexToQuery(shouldAclsGuardian, _SIDPrefixBase, subIds.ToList(), "acls.guardian");
                    shouldAclsThis = addRegexToQuery(shouldAclsGuardian, _SIDPrefixBase, subIds.ToList(), "acls.this");

                    queryContainer = new QueryContainer(new BoolQuery
                    {
                        Filter = new List<QueryContainer>
                        {
                            new  BoolQuery
                            {
                                Should = shouldAclsGuardian,
                                MinimumShouldMatch = 1
                            },
                            new  BoolQuery
                            {
                                Should = shouldAclsThis,
                                MinimumShouldMatch = 1
                            },
                        }
                    });
                }
                else
                {
                    queryContainer = new QueryContainer(new BoolQuery
                    {
                        Filter = new List<QueryContainer>
                            {
                                new TermsQuery
                                {
                                    Field = "acls.this",
                                    Terms = tokenGroupSids

                                },
                                new TermsQuery
                                {
                                    Field = "acls.guardian",
                                    Terms = tokenGroupSids
                                },
                            }
                    });
                }
                indiciesPrivileges.Add(new IndicesPrivileges() { Names = "*", Privileges = privileges, Query = queryContainer });
                roleRequest.Indices = indiciesPrivileges;
                roleRequest.Metadata = new Dictionary<string, object>();
                roleRequest.Metadata.Add(key, tokenGroupSids);
                var response = client.Security.PutRole(roleRequest);
                if (!response.IsValid)
                {
                    Console.WriteLine($"failed to add role {roleName}");
                }
            }

            /// <summary>
            //PUT /_security/role_mapping/<PREFIX_FriendlyNameWithoutSpaces>
            //{
            //  "roles" : [ "(PREFIX_FriendlyNameWithoutSpaces)" ],
            //  "rules" : { "field" : { "dn" : "(USER_DN)" } },
            // "enabled": true
            //} 
            /// </summary>
            /// <param name="roles"></param>
            /// <param name="username"></param>
            /// <param name="roleMappingName"></param>
            public void PutUserRoleMapping(string[] roles, string username, string roleMappingName)
            {
                var roleMappingRequest = new PutRoleMappingRequest(roleMappingName);
                // Roles
                roleMappingRequest.Roles = roles;
                // Rules
                var userNameRule = new UsernameRule(username);
                roleMappingRequest.Rules = userNameRule;
                //Enabled
                roleMappingRequest.Enabled = true;
                var response = this.client.Security.PutRoleMapping(roleMappingRequest);
            }

            internal void RemoveRoleAndRoleMapping(string existingrole)
            {
                var responseDeleteRoleMapping = this.client.Security.DeleteRoleMapping(existingrole, x => x.RequestConfiguration(r => r.RequestTimeout(TimeSpan.FromMinutes(5))));
                var responseDeleteRole = this.client.Security.DeleteRole(existingrole);
            }

            internal List<KeyValuePair<string, XPackRole>> GetCurrentRoleList<T>() where T : IADResult
            {
                var response = this.client.Security.GetRole("");
                if (response.IsValid)
                {
                    var results = response.Roles.Where(x => x.Key.Contains(ADUserResult.NamePrefix));
                    return results.ToList();
                }
                return null;
            }

            internal List<KeyValuePair<string, XPackRoleMapping>> GetCurrentRoleMappingList<T>() where T : IADResult
            {
                //var response = this.client.Security.GetRoleMapping("HOKADUser_James_Blackadar,HOKADUser_Dan_Siroky");
                try
                {
                    var response = this.client.Security.GetRoleMapping("");
                    if (response.IsValid)
                    {
                        var results = response.RoleMappings.Where(x => x.Key.Contains(ADUserResult.NamePrefix));
                        return results.ToList();
                    }
                }
                catch (Exception ex)
                {
                    var t = ex.InnerException;
                }
                return null;
            }

            internal List<QueryContainer> addRegexToQuery(List<QueryContainer> baseList, string baseId, List<string> subIds, string field)
            {
                // Try to create initial regex
                var r = $"{baseId}({String.Join("|", subIds)})";
                int numLists = (int)Math.Ceiling((double)r.Length / (1000));

                // If over regex is 1000 characters, split into multiple queries
                if (numLists > 1)
                {
                    int itemsPerList = (int)Math.Ceiling((double)subIds.ToList().Count / (numLists + 1));
                    var subIdsSubList = subIds
                        .Select((x, i) => new { Index = i, Value = x })
                        .GroupBy(x => (int)Math.Floor((double)(x.Index / itemsPerList)))
                        .Select(x => x.Select(v => v.Value).ToList())
                        .ToList();

                    foreach (var subList in subIdsSubList)
                    {
                        baseList.Add(new RegexpQuery
                        {
                            Field = field,
                            Value = $"{baseId}({String.Join("|", subList)})"
                        });
                    }
                }
                else
                {
                    // Otherwise, just add original regex
                    baseList.Add(new RegexpQuery
                    {
                        Field = field,
                        Value = r
                    });
                }
                return baseList;
            }


        }



    }
}
