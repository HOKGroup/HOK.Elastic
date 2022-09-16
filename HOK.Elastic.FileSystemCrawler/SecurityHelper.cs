using HOK.Elastic.DAL;
using HOK.Elastic.DAL.Models;
using HOK.Elastic.Logger;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;


namespace HOK.Elastic.FileSystemCrawler
{
    /// <summary>
    /// This class purpose is a helper to read the file system security. We want to store the security info as SID format rather than friendly names as names frequently change. 
    /// User SIDS also change ocassionally; however, group SIDS seldom do. But something to consider
    /// Also, because we may encounter very similar security group schemes in production (folders being permissioned similarly) we can store the hash and lookup the values from our cache when we encounter it again
    /// </summary>

    public class SecurityHelper : IDisposable
    {
        public static readonly Type sidType = typeof(System.Security.Principal.SecurityIdentifier);
        public static readonly Type ntType = typeof(System.Security.Principal.NTAccount);
        ////do not set size requirement on cache as the cache is shared and in future should we need to use the cache with Dependency Injection or EF, that cache implementation doesn't use the size parameter.
        private readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions() { CompactionPercentage = 0.2 });
        private const System.Security.AccessControl.FileSystemRights ReadAccessFlag = System.Security.AccessControl.FileSystemRights.Read;
        private readonly HOK.Elastic.Logger.Log4NetLogger _il;
        private readonly bool isDebugEnabled;
        private bool disposedValue;
#if DEBUG
        private int _hits = 0;
        private int _misses = 0;
#endif

        public SecurityHelper(Log4NetLogger logger)
        {       
            if(logger !=null)
            {
                _il = logger;
                isDebugEnabled = _il.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug);
            }
        }

        /// <summary>
        /// Returns a friendly string name of the person at index time (for historical purposes) but it may not match when a person has a name change unless recrawled, or the document is updated or synononym are used.
        /// </summary>
        /// <param name="fileInfo">FileInfo object to get owner information for</param>
        /// <returns></returns>
        public static string GetOwner(FileInfo fileInfo)
        {
            try
            {
                FileSecurity secinfo = fileInfo.GetAccessControl(AccessControlSections.Owner);
                return secinfo.GetOwner(ntType).Value;
            }
            catch (IdentityNotMappedException)
            {
                return "Absent";
            }
            catch (UnauthorizedAccessException)
            {
                return "Unauthorized";
            }
            catch (Exception)
            {
                return "Unknown";
            }
        }


        /// <summary>
        /// Get Document's security as well as the security of the ancedant folder where permissions are protected/explictly set.
        /// </summary>
        /// <param name="fsi"></param>
        /// <returns></returns>
        public DAL.ACLs GetDocACLs(FileSystemInfo fsi, Tuple<List<string>, string> guardian = null)
        {
            DAL.ACLs acls = new ACLs();
            if (fsi is FileInfo fi)
            {
                var fs = fi.GetAccessControl(AccessControlSections.Access);
                var sacls = GetReadAccessGuidsFromCacheOrFileSystem(fs);
                acls.This = sacls.ToList();
                if (guardian == null)
                {
                    guardian = GetGuardian(fi.Directory);
                }
            }
            else
            {
                var di = fsi as DirectoryInfo;
                var fs = di.GetAccessControl(AccessControlSections.Access);
                var sacls = GetReadAccessGuidsFromCacheOrFileSystem(fs);
                acls.This = sacls.ToList();
                if (guardian == null)
                {
                    guardian = GetGuardian(di);
                }
            }
            acls.Guardian = guardian.Item1;
            acls.GuardianPath = guardian.Item2;
            return acls;
        }


        /// <summary>
        /// Returns a list of read access or greater members/groups in their Sid format as a string array.
        /// </summary>
        /// <param name="fs"></param>
        /// <returns>Returns a list of read access or greater members/groups in their Sid format as a string array.</returns>
        /// <exception cref="System.UnauthorizedAccessException">Native access Exceptions aren't handled</exception>
        private IEnumerable<string> GetReadAccessGuidsFromCacheOrFileSystem(FileSystemSecurity fs)
        {
            //use the hashcode to see if the entry already exists (we have already looked up a very similar hit)
            int SddlHash = fs.GetSecurityDescriptorSddlForm(AccessControlSections.Access).GetHashCode();
            if (!_cache.TryGetValue(SddlHash, out IEnumerable<string> cacheEntry))
            {
                cacheEntry = GetAccesslist(fs);
                _cache.Set(SddlHash, cacheEntry);
#if DEBUG
                Interlocked.Increment(ref _misses);
                if (isDebugEnabled)
                {
                    _il.LogDebugInfo($"We missed but have {_misses} misses and {_hits} hits so far");
                }
#endif
            }
            else
            {
#if DEBUG
                Interlocked.Increment(ref _hits);
#endif
            }
            return cacheEntry;
        }

        /// <summary>
        /// Builds a distinct list of all SIDS with Read or Greater access to the file. Deny rules are not accounted for.
        /// </summary>
        /// <param name="fs">fileSystemSecurity can be pre-filtered to include only access sections</param>
        /// <returns></returns>
        private static IEnumerable<string> GetAccesslist(FileSystemSecurity fs)
        {
            List<string> sidlist = new List<string>();
            //else this a new hashcode...some variation on permissions, let's find out all SIDS and add to the map/dictionary.
            foreach (System.Security.AccessControl.FileSystemAccessRule accessrule in fs.GetAccessRules(true, true, sidType))
            {
                if (accessrule.AccessControlType == System.Security.AccessControl.AccessControlType.Allow && (accessrule.FileSystemRights & ReadAccessFlag) == ReadAccessFlag)
                {
                    //allow read access flag
                    string sid = accessrule.IdentityReference.Value;
                    if (MoreThanFourHyphens(sid))
                    {
                        sidlist.Add(sid);
                    }
                    else
                    {
                        ///https://support.microsoft.com/en-us/help/243330/well-known-security-identifiers-in-windows-operating-systems eg 'S-1-5-18' == 'Local Admins'
                        SecurityIdentifier s = (SecurityIdentifier)accessrule.IdentityReference.Translate(sidType);
                        if (s.IsWellKnown(WellKnownSidType.CreatorOwnerSid) || s.IsWellKnown(WellKnownSidType.LocalSystemSid))
                        {
                            //ignore the most common items we don't care about.
                        }
                        else
                        {
                            sidlist.Add(sid);
                        }
                    }
                }
            }
            return sidlist.Distinct().OrderBy(o => o);//duplicates can exist as there maybe different rules for the same SID. Order them to make future comparison faster (and look neater)
        }

        /// <summary>
        /// Navigates up the tree searching for protected access rules (intentional changes in security scheme that aren't to be overridden and therefore deemed important)
        /// </summary>
        /// <param name="di"></param>
        /// <returns>List of SIDS and the guardian/protected/important folder that had them</returns>
        private Tuple<List<string>, string> GetGuardian(DirectoryInfo di)
        {
            Stack<DirectoryInfo> stack = new Stack<DirectoryInfo>();
            stack.Push(di.Parent ?? di);//we really would rather get the guardian from one folder above current
            DirectorySecurity ds;
            while (stack.Count > 0)
            {
                di = stack.Pop();
                if (di == null||!di.Exists) break;
                    ds = di.GetAccessControl(AccessControlSections.Access);
                    if (ds.AreAccessRulesProtected)//inheritance is disabled....start of new explicit security rules
                    {
                        var guardian = GetReadAccessGuidsFromCacheOrFileSystem(ds).ToList();
                        var guardianPath = PathHelper.GetPublishedPath(di.FullName.ToLowerInvariant());
                        return new Tuple<List<string>, string>(guardian, guardianPath);
                    }
                if(di.Parent!=null) stack.Push(di.Parent);
            }
            return new Tuple<List<string>, string>(new List<string>(), PathHelper.GetPublishedPath(di.FullName.ToLowerInvariant()));
        }

        /// <summary>
        /// Generally SIDS with more than 4 hyphens are the interesting ones that represent groups and users in our domain. Builtin SIDS have 3(most common) or 4 hyphens.
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        private static bool MoreThanFourHyphens(string text)
        {
            int count = 0;
            for (int i = 0; i < text.Length; ++i)
            {
                if (text[i].Equals('-'))
                {
                    count++;
                    if (count > 4) return true;
                }
            }
            return false;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    _cache.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~SecurityHelper()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}