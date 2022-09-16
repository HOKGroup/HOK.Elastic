using Elasticsearch.Net;
using HOK.Elastic.Logger;
//using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nest;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.DirectoryServices;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HOK.Elastic.RoleMappingGroupSync
{
    partial class Program
    {
        private static Log4NetLogger _il;
        /// <summary>
        /// variables we need to set from config file.
        /// </summary>
        //private static string GlobalCatalogFallbackServer = @"GC://LDAP-SVR.group.contoso.com";
        //private static string GlobalCatalogIncludeSuffix = "group.contoso.com";
        //private static string LDAPIncludeSuffix = "group.contoso.com";
        //private static string LDAPFallbackServer = @"LDAP://LDAP-SVR.group.contoso.com/dc=group,dc=contoso,dc=com";
        //private static string LDAPDefaultQueryPath = @"LDAP://dc=group,dc=contoso,dc=com";
        //private static string RolesAndRoleMappingNamePrefix = "AD";
        //private static string SIDSuffixBase = @"S-1-1-11-1111111111-111111111-111111111-";
        private static RoleMappingSettings OurRoleMappingSettings;

        static void Main(string[] args)
        {
            const string ROLEMAPPINGSETTINGS = "rolemappingsettings.json";

            try
            {
                if (args.Length != 1)
                {
                    throw new ArgumentException(string.Format("Incorrect number of arguments passed to the application; This received {0} arguments. Check if the path contains spaces and is quoted.\r\n\r\n Example: {1} 'd:\\nasuni jobs\\usfull'", args.Length, System.Reflection.Assembly.GetExecutingAssembly().CodeBase));
                }
                var conf = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var configFilePath = conf.FilePath;
                _il = new Logger.Log4NetLogger("ConsoleProgram", Logger.Log4NetProvider.Parselog4NetConfigFile("app.config"));
                log4net.Config.XmlConfigurator.Configure();
                string serverUrl = args.First();
                //load settings.json
                if (File.Exists(ROLEMAPPINGSETTINGS))
                {
                    try
                    {
                        string json = File.ReadAllText(ROLEMAPPINGSETTINGS);
                        var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<RoleMappingSettings>(json);
                        if (settings != null)
                        {
                            OurRoleMappingSettings = settings;
                        }
                    }
                    catch (Exception e)
                    {
                        if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error)) _il.LogErr("Error reading" + ROLEMAPPINGSETTINGS + e.Message);
                        throw;
                    }
                }
                else throw new FileNotFoundException(ROLEMAPPINGSETTINGS + "is a required file.");
                //
                //comment
                if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
                {
                    _il.LogInfo("starting");
                    _il.LogInfo(string.Format("Checking connection to server at: {0}", serverUrl));
                    _il.LogInfo("settings", null, OurRoleMappingSettings);
                }
                else
                {
                    Console.WriteLine(string.Format("Server: {0}", serverUrl));
                }
                var connectionPool = new SingleNodeConnectionPool(new Uri(serverUrl));
                var ourElasticCluster = new Elastic(OurRoleMappingSettings.SIDSuffixBase, connectionPool, _il);
                var result = ourElasticCluster.GetClientStatus();

                if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
                {
                    _il.LogInfo("ClusterStatus" + result);
                    _il.LogInfo("Getting list of current elastic groups....");
                }
                List<KeyValuePair<string, XPackRoleMapping>> allroleMappingInElastic = ourElasticCluster.GetCurrentRoleMappingList<ADUserResult>();
                List<KeyValuePair<string, XPackRole>> allrolesAndMappingInElastic = ourElasticCluster.GetCurrentRoleList<ADUserResult>();
                if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
                {
                    _il.LogInfo("Getting list of current AD users....");
                }
                var adUsers = GetADusers();
                if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
                {
                    _il.LogDebugInfo(string.Format("Found '{0}' users", adUsers.Count));
                }
                foreach (var user in adUsers)
                {
                    string key = "tokenGroupsSIDs";
                    List<string> tokenGroupSIDs = user.tokenGroupSIDs;
                    var matchedUserRole = allrolesAndMappingInElastic.Find(x => x.Key == user.ElasticFriendlyName);
                    // If there is a match
                    if (!matchedUserRole.Equals(new KeyValuePair<string, XPackRole>()))
                    {
                        if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
                        {
                            _il.LogDebugInfo("Already present", user.DN);
                        }
                        bool metadataNeedsUpdating = false;

                        if (matchedUserRole.Value.Metadata.Keys.Contains(key))
                        {
                            List<object> metadata = matchedUserRole.Value.Metadata[key] as List<object>;
                            List<string> metadataAsString = metadata.Cast<string>().ToList();
                            // Check query SIDs against user SIDs for equality
                            // If not equal, metadata needs updating
                            metadataNeedsUpdating = !(tokenGroupSIDs.All(metadataAsString.Contains) && tokenGroupSIDs.Count == metadataAsString.Count);
                        }
                        else
                        {
                            // If metadata value not found in Elastic Role
                            metadataNeedsUpdating = true;
                        }
                        //lowercase check
                        try
                        {
                            var roleMapping = allroleMappingInElastic.Where(x => x.Key == user.ElasticFriendlyName).FirstOrDefault();
                            var fieldRoleMappingRule = roleMapping.Value.Rules as FieldRoleMappingRule;
                            var userNameRule = fieldRoleMappingRule.Field as UsernameRule;
                            var userName = userNameRule?.Where(x => x.Key == "username")?.FirstOrDefault().Value as string;
                            if (!user.Name.Equals(userName, StringComparison.Ordinal))
                            {
                                if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
                                {
                                    _il.LogDebugInfo("Updating rolemapping for user due to username mismatch.", user.ElasticFriendlyName);
                                }
                                ourElasticCluster.PutUserRoleMapping(new string[] { user.ElasticFriendlyName }, user.Name, user.ElasticFriendlyName);
                            }
                        }
                        catch (Exception e)
                        {
                            if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error))
                            {
                                _il.LogErr("Error", user.Name, e.Message, e);
                            }
                        }
                        // Update if necessary
                        if (metadataNeedsUpdating)
                        {
                            if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
                            {
                                _il.LogDebugInfo("Updating query for role", user.ElasticFriendlyName);
                            }
                            ourElasticCluster.PutUserRole(user.ElasticFriendlyName, tokenGroupSIDs, key);
                        }
                    }
                    else
                    {
                        if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
                        {
                            _il.LogInfo("Adding " + user.ElasticFriendlyName, user.DN);
                        }
                        ourElasticCluster.PutUserRole(user.ElasticFriendlyName, tokenGroupSIDs, key);
                        ourElasticCluster.PutUserRoleMapping(new string[] { user.ElasticFriendlyName }, user.Name, user.ElasticFriendlyName);
                    }
                }
                if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
                {
                    _il.LogInfo("Checking for abandoned roles....");
                }
                var accountFriendlyNamesinElastic = allroleMappingInElastic.Select(x => x.Key).Concat(allrolesAndMappingInElastic.Where(b => !allrolesAndMappingInElastic.Where(w => w.Key == b.Key).Any()).Select(s => s.Key));
                foreach (var existingrole in accountFriendlyNamesinElastic)
                //    foreach (var existingrole in allrolesAndMappingInElastic)
                {
                    if (!adUsers.Exists(x => x.ElasticFriendlyName == existingrole))
                    {
                        if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
                        {
                            _il.LogInfo("Removing abandoned: " + existingrole);
                        }
                        ourElasticCluster.RemoveRoleAndRoleMapping(existingrole);
                    }
                }
                if (_il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
                {
                    _il.LogInfo("Complete");
                }
            }
            catch (Exception ex)
            {
                if (_il != null && _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error))
                {
                    _il.LogErr("Main Error", null, null, ex);
                }
                else
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        public static List<ADUserResult> GetADusers()
        {
            List<ADUserResult> usersAndSids = new List<ADUserResult>();
            string[] propertiesToLoad = new string[] { "DistinguishedName", "SamAccountName" };
            //https://docs.microsoft.com/en-us/windows/win32/ad/searching-for-groups-by-scope-or-type-in-a-domain
            string filterString = @"(&(sAMAccountType=805306368)(|(employeeType=Employee)(employeeType=Temp)))";
            int pageSize = 500;
            DirectoryEntry rootEntry = new DirectoryEntry(OurRoleMappingSettings.LDAPDefaultQueryPath);
            string dc = rootEntry.Options.GetCurrentServerName();
            if (!dc.EndsWith(OurRoleMappingSettings.LDAPIncludeSuffix))
            {
                rootEntry = new DirectoryEntry(OurRoleMappingSettings.LDAPFallbackServer);
            }
            var searcher = new DirectorySearcher(rootEntry);
            searcher.PropertiesToLoad.AddRange(propertiesToLoad);
            searcher.PageSize = pageSize;
            searcher.ServerTimeLimit = TimeSpan.FromMinutes(5);
            searcher.Filter = filterString;
            SearchResultCollection results = searcher.FindAll();
            foreach (SearchResult user in results)
            {
                string userDN = user.Properties["DistinguishedName"][0] as string;
                string username = user.Properties["SamAccountName"][0] as string;

                List<string> userTokenGroups = GetNestedGroupMembershipsByTokenGroup(userDN);
                // Add 'Authenticated Users' and 'Everyone' as a group to always check / add
                userTokenGroups.Add("S-1-5-11");
                userTokenGroups.Add("S-1-1-0");
                userTokenGroups.Sort();
                ADUserResult adUser = new ADUserResult(username, userDN, userTokenGroups);
                usersAndSids.Add(adUser);
            }
            return usersAndSids;
        }

        private static List<string> GetNestedGroupMembershipsByTokenGroup(string userDN)
        {
            List<string> nestedGroups = new List<string>();

            DirectoryEntry userEntry = new DirectoryEntry("GC://" + userDN);
            string dc = userEntry.Options.GetCurrentServerName();
            if (!dc.EndsWith(OurRoleMappingSettings.GlobalCatalogIncludeSuffix))
            {
                userEntry = new DirectoryEntry(OurRoleMappingSettings.GlobalCatalogFallbackServer + "/" + userDN);
            }
            // Use RefreshCach to get the constructed attribute tokenGroups.
            userEntry.RefreshCache(new string[] { "tokenGroups" });

            foreach (byte[] sid in userEntry.Properties["tokenGroups"])
            {
                string groupSID = new System.Security.Principal.SecurityIdentifier(sid, 0).ToString();
                nestedGroups.Add(groupSID);
            }
            return nestedGroups;
        }


        public interface IADResult
        {
            static string NamePrefix;
        }

        public class ADGroupResult : IADResult
        {
            private static System.Text.RegularExpressions.Regex re = new System.Text.RegularExpressions.Regex(@"[\W]", System.Text.RegularExpressions.RegexOptions.Compiled);
            public static string NamePrefix = OurRoleMappingSettings.RolesAndRoleMappingNamePrefix + "_";
            public string DN { get; private set; }
            public string Name { get; private set; }
            public string SID { get; private set; }
            public string ElasticFriendlyName
            {
                get
                {
                    string result = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Name);
                    result = result.Replace(" ", "");
                    result = NamePrefix + re.Replace(result, "_");
                    return result;
                }
            }

            public ADGroupResult(string name, string distinguishedName, string SIDstring)
            {
                Name = name;
                DN = distinguishedName;
                SID = SIDstring;
            }
        }

        public class ADUserResult : IADResult
        {
            private static System.Text.RegularExpressions.Regex re = new System.Text.RegularExpressions.Regex(@"[\W]", System.Text.RegularExpressions.RegexOptions.Compiled);
            public static string NamePrefix = OurRoleMappingSettings.RolesAndRoleMappingNamePrefix + "User_";
            public string DN { get; private set; }
            public string Name { get; private set; }
            public List<string> tokenGroupSIDs { get; private set; }
            public string ElasticFriendlyName
            {
                get
                {
                    string result = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Name);
                    result = result.Replace(" ", "");
                    result = NamePrefix + re.Replace(result, "_");
                    return result;
                }
            }


            /// <summary>
            /// 
            /// </summary>
            /// <param name="name">We will store this in lowercase to avoid mismatch during LDAP lookups for UserRoleMapping</param>
            /// <param name="distinguishedName"></param>
            /// <param name="SIDgroups"></param>
            public ADUserResult(string name, string distinguishedName, List<string> SIDgroups)
            {
                Name = CultureInfo.CurrentCulture.TextInfo.ToLower(name);
                DN = distinguishedName;
                tokenGroupSIDs = SIDgroups;
            }
        }

        //[Flags]
        //public enum attrGroupTypes : uint
        //{
        //    ADS_CreatedBySystem = 1,
        //    ADS_GROUP_TYPE_GLOBAL_GROUP = 2,
        //    ADS_GROUP_TYPE_DOMAIN_LOCAL_GROUP = 4,
        //    ADS_GROUP_TYPE_UNIVERSAL_GROUP = 8,
        //    ADS_ARP_BASIC = 16,
        //    ADS_ARP_QUERY = 32,
        //    ADS_GROUP_TYPE_SECURITY_ENABLED = 0x80000000
        //}

    }

}
