using System;

namespace HOK.Elastic.DAL.Models
{
    public class LongPaths
    {
        private const string LEGACYUNC = @"\\?\unc\";
        private const string LEGACYLOCALPATH = @"\\?\";

        public static string GetLegacyLongPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            path = GetNormalPath(path);
            if (path.StartsWith(@"\\"))//shouldn't this just be the second check?
            {
                path = LEGACYUNC + path.Substring(2);
            }
            else
            {
                path = LEGACYLOCALPATH + path;
            }
            return path;
        }
        public static string GetNormalPath(string path)
        {
            if (path.StartsWith(LEGACYUNC, StringComparison.OrdinalIgnoreCase))
            {
                path = @"\\" + path.Substring(LEGACYUNC.Length);//turn this into a standard unc path like '\\server\share\folder'
            }
            else if (path.StartsWith(LEGACYLOCALPATH, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(LEGACYLOCALPATH.Length);
            }
            return path;
        }
    }
}

