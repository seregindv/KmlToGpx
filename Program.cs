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
        private static string Trim(string s)
        {
            return s?.Trim('\n', '\r', ' ');
        }

        private static void Main(string[] args)
        {
            try
            {
                Do(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private static void Do(string[] args)
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
            var ext = Path.GetExtension(args[0]);

            XDocument doc;
            if (ext == ".kmz")
            {
                var zip = ZipFile.OpenRead(args[0]);
                var kml = zip.GetEntry("doc.kml");
                if (kml == null)
                {
                    Console.WriteLine("KML not found in " + args[0]);
                    return;
                }
                doc = XDocument.Load(kml.Open());
            }
            else
                doc = XDocument.Load(args[0]);

            var firstElement = doc.Elements().First();
            var ns = firstElement.GetDefaultNamespace();
            var nsManager = new XmlNamespaceManager(new NameTable());
            nsManager.AddNamespace("kml", ns.NamespaceName);

            var folders = new List<Folder>();
            foreach (var kmlFolder in doc.XPathSelectElements("//kml:Folder", nsManager))
                ProcessFolder(kmlFolder, nsManager, folders);
            if (folders.Count == 0)
                foreach (var kmlDocument in doc.XPathSelectElements("//kml:Document", nsManager))
                    ProcessFolder(kmlDocument, nsManager, folders);

            var fullPath = Path.GetFullPath(args[0]);
            var path = Path.GetDirectoryName(fullPath);
            SaveFolders(folders, path);
        }

        private static void ProcessFolder(XElement kmlFolder, XmlNamespaceManager nsManager, List<Folder> folders)
        {
            var folderName = kmlFolder.XPathSelectElement("kml:name", nsManager).Value;
            var folder = new Folder { Name = folderName };
            folders.Add(folder);
            foreach (var kmlPlacemark in kmlFolder.XPathSelectElements("kml:Placemark", nsManager))
            {
                var placemarkName = kmlPlacemark.XPathSelectElement("kml:name", nsManager).Value;
                var placemarkPoint = kmlPlacemark.XPathSelectElement("kml:Point/kml:coordinates", nsManager);
                var placemarkDescription = kmlPlacemark.XPathSelectElement("kml:description", nsManager)?.Value;
                var styleUrl = kmlPlacemark.XPathSelectElement("kml:styleUrl", nsManager)?.Value;
                string color = null;
                if (styleUrl != null)
                {
                    var match = Regex.Match(styleUrl, @"(?:\-)([ABCDEF\d]{6})(?:\-|$)");
                    if (match.Success)
                        color = match.Groups[1].Value;
                }
                if (placemarkPoint != null)
                {
                    var coordinates = placemarkPoint.Value.Split(',');
                    folder.Points.Add(new Point
                    {
                        Name = placemarkName,
                        Latitude = Trim(coordinates[1]),
                        Longtitude = Trim(coordinates[0]),
                        Description = placemarkDescription,
                        Color = color
                    });
                    continue;
                }
                var lineStringCoordinates = kmlPlacemark.XPathSelectElement("kml:LineString/kml:coordinates", nsManager);
                if (lineStringCoordinates != null)
                {
                    var lineFolder = new Folder { Name = placemarkName, Type = FolderType.Path };
                    folders.Add(lineFolder);
                    using (var sr = new StringReader(lineStringCoordinates.Value))
                    {
                        string coordinateLine;
                        while ((coordinateLine = sr.ReadLine()) != null)
                        {
                            if (String.IsNullOrWhiteSpace(coordinateLine))
                                continue;
                            var coordinates = coordinateLine.Split(',');
                            lineFolder.Points.Add(new Point
                            {
                                Latitude = Trim(coordinates[1]),
                                Longtitude = Trim(coordinates[0])
                            });
                        }
                    }
                }
            }
        }

        private static void SaveFolders(List<Folder> folders, string path)
        {
            foreach (var folder in folders)
            {
                var outDoc = folder.Type == FolderType.Points ? GetPoints(folder) : GetPath(folder);
                var fileName = TrimIllegalChars(folder.Name);
                var i = 0;
                string filePath;
                do
                {
                    var filePathWithoutExtension = Path.Combine(path, fileName);
                    if (i != 0)
                        filePathWithoutExtension += " [" + i + "]";
                    filePath = filePathWithoutExtension + ".gpx";
                    if (!File.Exists(filePath))
                        break;
                    ++i;
                } while (true);
                outDoc.Save(filePath);
            }
            Console.WriteLine("Done! " + folders.Count + " file(s) saved.");
        }

        private static XDocument GetPoints(Folder folder)
        {
            return new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("gpx",
                    from point in folder.Points
                    select new XElement("wpt",
                        new XAttribute("lat", point.Latitude),
                        new XAttribute("lon", point.Longtitude),
                        new XElement("name", point.Name),
                        point.Color == null ? null
                        : new XElement("extensions",
                            new XElement("color", "#" + point.Color)),
                        point.Description==null? null
                        : new XElement("desc", point.Description)
                    )
                )
            );
        }

        private static XDocument GetPath(Folder folder)
        {
            return new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("gpx",
                    new XElement("trk",
                        new XElement("trkseg",
                            folder.Points.Select(
                                point =>
                                    new XElement("trkpt",
                                        new XAttribute("lon", point.Longtitude),
                                        new XAttribute("lat", point.Latitude)
                                )
                            )
                        )
                    )
                )
            );
        }

        private static string TrimIllegalChars(string path)
        {
            return String.Concat(path.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        }

    }
}
