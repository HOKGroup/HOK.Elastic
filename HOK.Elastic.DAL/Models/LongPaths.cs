using System;

namespace HOK.Elastic.DAL.Models
{
    public class LongPaths
    {
        private static bool? _isSupported;
        private const string LEGACYUNC = @"\\?\unc\";
        private const string LEGACYLOCALPATH = @"\\?\";

        public static string GetLegacyLongPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string longpath;
            if (path.StartsWith(LEGACYUNC, StringComparison.OrdinalIgnoreCase))
            {
                path = @"\\" + path.Substring(8);//we remove the legacy long path prefix so we can add it back again. ToDO...check why we don't just return the passed path in this case. I'm sure there is a reason.
            }

            if (path.StartsWith(LEGACYLOCALPATH, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(4);
            }
            if (path.StartsWith(@"\\") && !path.StartsWith(LEGACYUNC))//shouldn't this just be the second check?
            {
                longpath = LEGACYUNC + path.Substring(2);
            }
            else
            {
                longpath = LEGACYLOCALPATH + path;
            }
            return longpath;
        }
        public static string GetShorterPath(string path)
        {
            if (path.StartsWith(LEGACYUNC, StringComparison.OrdinalIgnoreCase))
            {
                path = @"\\" + path.Substring(8);//turn this into a standard unc path like '\\server\share\folder'
            }
            else
            {
                path = path.Replace(LEGACYLOCALPATH, "");//path could start with '\\?\' or '\\' or ''(as in 'c:\temp\...')
            }
            return path;
        }

    }
}

