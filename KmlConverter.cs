using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml;
using System.Xml.XPath;

namespace KmlToGpx
{
    internal class KmlConverter
    {
        public void Process(string filePath)
        {
            var document = GetXmlDocument(filePath);
            if (document == null)
                return;

            var folders = GetFolders(document);

            var fullPath = Path.GetFullPath(filePath);
            var path = Path.GetDirectoryName(fullPath);
            SaveFolders(folders, path);
        }

        private XDocument GetXmlDocument(string filePath)
        {
            var ext = Path.GetExtension(filePath);

            if (ext == ".kmz")
            {
                var zip = ZipFile.OpenRead(filePath);
                var kml = zip.GetEntry("doc.kml");
                if (kml == null)
                {
                    Console.WriteLine("KML not found in " + filePath);
                    return null;
                }
                return XDocument.Load(kml.Open());
            }
            else
                return XDocument.Load(filePath);

        }

        private List<Folder> GetFolders(XDocument doc)
        {
            var firstElement = doc.Root;
            var ns = firstElement.GetDefaultNamespace();
            var nsManager = new XmlNamespaceManager(new NameTable());
            nsManager.AddNamespace("kml", ns.NamespaceName);

            var folders = new List<Folder>();
            var defaultName = doc.XPathSelectElement("/kml:kml/kml:Document/kml:name", nsManager)?.Value;
            foreach (var kmlFolder in doc.XPathSelectElements("//kml:Folder", nsManager))
                ProcessFolder(kmlFolder, nsManager, folders, defaultName);
            if (folders.Count == 0)
                foreach (var kmlDocument in doc.XPathSelectElements("//kml:Document", nsManager))
                    ProcessFolder(kmlDocument, nsManager, folders, defaultName);
            return folders;
        }

        private void ProcessFolder(XElement kmlFolder, XmlNamespaceManager nsManager, List<Folder> folders, string defaultName)
        {
            var folderName = kmlFolder.XPathSelectElement("kml:name", nsManager)?.Value ?? defaultName;
            var folder = new Folder { Name = folderName };
            folders.Add(folder);
            foreach (var kmlPlacemark in kmlFolder.XPathSelectElements("kml:Placemark", nsManager))
            {
                var placemarkName = kmlPlacemark.XPathSelectElement("kml:name", nsManager).Value;
                var placemarkDescription = kmlPlacemark.XPathSelectElement("kml:description", nsManager)?.Value;
                if (!TryAddPoint(folder, kmlPlacemark, placemarkName, placemarkDescription, nsManager))
                    TryAddPath(folder, folders, kmlPlacemark, placemarkName, placemarkDescription, nsManager);
            }
        }

        private bool TryAddPoint(Folder folder, XElement element, string name, string description, XmlNamespaceManager nsManager)
        {
            var placemarkPoint = element.XPathSelectElement("kml:Point/kml:coordinates", nsManager);
            if (placemarkPoint == null)
                return false;

            var color = GetColor(nsManager, element);
            var coordinates = placemarkPoint.Value.Split(',');
            folder.Points.Add(new Point
            {
                Name = name,
                Latitude = Trim(coordinates[1]),
                Longtitude = Trim(coordinates[0]),
                Description = description,
                Color = color
            });
            return true;
        }

        private bool TryAddPath(Folder folder, List<Folder> folders, XElement element, string name, string description, XmlNamespaceManager nsManager)
        {
            var lineStringCoordinates = element.XPathSelectElement("kml:LineString/kml:coordinates", nsManager)
                ?? element.XPathSelectElement("//kml:LinearRing/kml:coordinates", nsManager);
            if (lineStringCoordinates == null)
                return false;
            var pathColor = GetColor(nsManager, element);
            var lineFolder = new Folder
            {
                Name = folder.Name + " - " + (string.IsNullOrEmpty(name) ? description : name),
                Type = FolderType.Path,
                Color = pathColor
            };
            folders.Add(lineFolder);
            var lines = lineStringCoordinates.Value.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var coordinateLine in lines)
            {
                var coordinates = coordinateLine.Split(',');
                lineFolder.Points.Add(new Point
                {
                    Latitude = Trim(coordinates[1]),
                    Longtitude = Trim(coordinates[0])
                });
            }
            return true;
        }

        private string GetColor(XmlNamespaceManager nsManager, XElement kmlPlacemark)
        {
            // google
            var styleUrl = kmlPlacemark.XPathSelectElement("kml:styleUrl", nsManager)?.Value;
            if (styleUrl != null)
            {
                var match = Regex.Match(styleUrl, @"(?:\-)([ABCDEF\d]{6})(?:\-|$)");
                if (match.Success)
                    return match.Groups[1].Value;
            }
            // yandex
            var rgb = kmlPlacemark.XPathSelectElement("kml:Style/kml:IconStyle/kml:color", nsManager)?.Value;
            if (rgb != null)
            {
                if (rgb.Length > 6)
                    rgb = rgb.Substring(rgb.Length - 6, 6);
                if (rgb.Length == 6)
                    rgb = rgb.Substring(4, 2) + rgb.Substring(2, 2) + rgb.Substring(0, 2);
                return rgb;
            }
            return null;
        }

        private void SaveFolders(List<Folder> folders, string path)
        {
            foreach (var folder in folders)
            {
                if (folder.Points.Count == 0)
                    continue;
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

        private XDocument GetPoints(Folder folder)
        {
            return new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("gpx",
                    from point in folder.Points
                    select new XElement("wpt",
                        new XAttribute("lat", point.Latitude),
                        new XAttribute("lon", point.Longtitude),
                        new XElement("name", point.Name),
                        GetColor(point.Color),
                        point.Description == null ? null
                        : new XElement("desc", point.Description)
                    )
                )
            );
        }

        private XElement GetColor(string color)
        {
            return color == null
                ? null
                : new XElement("extensions",
                    new XElement("color", "#" + color));
        }

        private XDocument GetPath(Folder folder)
        {
            return new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("gpx",
                    new XElement("trk",
                        GetColor(folder.Color),
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

        private string TrimIllegalChars(string path)
        {
            return String.Concat(path.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        }

        private static string Trim(string s)
        {
            return s?.Trim('\n', '\r', ' ');
        }
    }
}
