using Nest;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;

namespace ConsoleTest
{

    internal class Tester
        {
        DocumentHelper d1 = new DocumentHelper();
        DocumentHelper d2 = new DocumentHelper();
        public void Test()
        {
            d1.tt = new DocumentHelper.FSOHelper("test");
            //d2.tt.MyFunc = new Func<DocumentHelper.FSO>(x => { return new DocumentHelper.FSO}) };
            //DocumentHelper.FsoHelperInstance = "a";
        }

        }
    internal class DocumentHelper
    {
        public FSOHelper FsoHelperInstance { get; set; }
        private FSOHelper t;
        internal FSOHelper tt;
        public class FSOHelper
        {
            private string appender;
            public Func<FSO> MyFunc { get; set; }
            public FSOHelper(string val)
            {
                appender = val;
            }
            public string Test(string input)
            {
                return input + appender;
            }
        }
        public class FSO
        {
            public string Path { get; set; }
            public void Test(string name)
            {
                //Console.WriteLine(fs);
            }
        }
        public class FSOdir : FSO
        {
            public new string Path { get; set; }
            public new void Test(string name)
            {
               // Console.WriteLine(FsoHelperInstance.Test(name));
            }
        }
    }
}
