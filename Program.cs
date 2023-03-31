using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace KmlToGpx
{
    class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    Console.WriteLine("Usage: " + Assembly.GetExecutingAssembly().GetName().Name + " <file.kml|file.kmz>");
                    return;
                }
                if (!File.Exists(args[0]))
                {
                    Console.WriteLine(args[0] + " doesn't exist");
                    return;
                }
                var converter = new KmlConverter();
                converter.Process(args[0]);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
