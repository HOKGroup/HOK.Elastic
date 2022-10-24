using HOK.Elastic.DAL.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HOK.Elastic.FileSystemCrawler.Models
{
    public class InputPathBase : IComparable, IEquatable<InputPathBase>, IInputPathBase
    {
        internal string _path;
        /// <summary>
        /// Path always stored in lowercase.
        /// </summary>
        [Key]
        public string Path { get => LongPaths.GetLegacyLongPath(_path); set => _path = value?.ToLowerInvariant().TrimEnd('\\'); }//could be file or directory
        public string Office { get; set; }//calculate this if we have to otherwise pass it, otherwise calculate it at ingestion.todo
        public PathStatus PathStatus { get; set; }

        /// <summary>
        /// parameterless constructor to support easy json deserialization
        /// </summary>
        public InputPathBase()
        {

        }
        public InputPathBase(string path, string office, PathStatus status)
        {
            Path = path;
            Office = office;
            PathStatus = status;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as InputPathBase);
        }

        public bool Equals(InputPathBase other)
        {
            return other != null &&
                   Path == other.Path &&
                   Office == other.Office;
        }

        public override int GetHashCode()
        {
            int hashCode = 951739671;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Path);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Office);
            return hashCode;
        }

        int IComparable.CompareTo(object obj)
        {
            InputPathBase other = obj as InputPathBase;
            if (other is null)
            {
                return -1;
            }
            return string.Compare(Office, other.Office, StringComparison.OrdinalIgnoreCase) + string.Compare(Path, other.Path, StringComparison.OrdinalIgnoreCase);
        }

        public int CompareTo(InputPathBase other)
        {
            if (other is null)
            {
                return -1;
            }
            return string.Compare(Office, other.Office, StringComparison.OrdinalIgnoreCase) + string.Compare(Path, other.Path, StringComparison.OrdinalIgnoreCase);
        }


        public static int Compare(InputPathBase left, InputPathBase right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }
            if (left is null)
            {
                return -1;
            }
            return left.CompareTo(right);
        }


        public override string ToString()
        {
            return string.Join(";", Office, Path, PathStatus);
        }


        public static bool operator ==(InputPathBase left, InputPathBase right)
        {
            if (left is null)
            {
                return right is null;
            }
            return left.Equals(right);
        }
        public static bool operator !=(InputPathBase left, InputPathBase right)
        {
            return !(left == right);
        }
        public static bool operator <(InputPathBase left, InputPathBase right)
        {
            return Compare(left, right) < 0;
        }
        public static bool operator >(InputPathBase left, InputPathBase right)
        {
            return Compare(left, right) > 0;
        }
        public static bool operator <=(InputPathBase left, InputPathBase right)
        {
            return left == right || Compare(left, right) < 0;
        }
        public static bool operator >=(InputPathBase left, InputPathBase right)
        {
            return left == right || Compare(left, right) > 0;
        }
    }

    public enum PathStatus
    {
        Unstarted,
        Resume
    }
}
