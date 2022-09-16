using System;

namespace HOK.Elastic.DAL
{
    public class ApiKey
    {
        public string Id;
        public string Secret;
        public DateTimeOffset Expiration;
        public DateTimeOffset ExpirationSunset;
    }
}