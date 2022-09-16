using Nest;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HOK.Elastic.DAL
{
    /// <summary>
    /// Access Control List (ACL) class that will hold the relevant security information to be used for filtering
    /// </summary>
    public class ACLs : IEquatable<ACLs>
    {
        /// <summary>
        /// this represents the READ level ACLs on the filesysteminfo item. 
        /// </summary>
        [Keyword]
        public List<string> This { get; set; }
        /// <summary>
        /// guardian represents the READ level ACLs on the ancestor folder where inheritance has been broken and new permissons applied.
        /// </summary>
        [Keyword]
        public List<string> Guardian { get; set; }
        /// <summary>
        /// path to where the guardian ACLs were found
        /// </summary>
        [Keyword]
        public string GuardianPath { get; set; }

        public bool Equals(ACLs other)
        {
            return Equals(this, other);
        }

        bool Equals(ACLs x, ACLs y)
        {
            if (x?.This == null || y?.This == null || x.Guardian == null || y.Guardian == null || string.IsNullOrEmpty(x.GuardianPath) || string.IsNullOrEmpty(y.GuardianPath)) return false;
            if (x.This.Count != y.This.Count) return false;
            if (!x.GuardianPath.Equals(y.GuardianPath, StringComparison.OrdinalIgnoreCase)) return false;
            bool thisEqual = Enumerable.SequenceEqual(x.This.OrderBy(o => o), y.This.OrderBy(o => o));
            bool guardianEqual = Enumerable.SequenceEqual(x.Guardian.OrderBy(o => o), y.Guardian.OrderBy(o => o));
            return thisEqual && guardianEqual;
        }
    }
}
