using System.Collections.Generic;

namespace KmlToGpx
{
    class Folder
    {
        public Folder()
        {
            Points = new List<Point>();
            Type = FolderType.Points;
        }

        public FolderType Type;
        public string Name;
        public List<Point> Points;
        public string Color;
    }
}