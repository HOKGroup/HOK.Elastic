using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HOK.Elastic.Logger;
using Microsoft.Extensions.Logging;

namespace HOK.Elastic.ArchiveDiscovery
{
    internal class Repository<T> where T : class,new()
    {
        public Repository()
            {

            Value = new T();
            }
        private string _filePath = "storage.json";
        public void Load()
        {
            string jsonIn = File.ReadAllText(_filePath);
            Value = JsonConvert.DeserializeObject<T>(jsonIn);
        }

        public void Save()
        {
            string json = JsonConvert.SerializeObject(Value);   
                File.WriteAllText(_filePath, json);
        }
        
        public T Value { get; set; }
    }
}
