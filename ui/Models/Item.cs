using System;
using System.Drawing;
using System.IO;
using System.Linq;
using bcollection.domain;

namespace bc_ui.Models
{
    public class UiItem
    {
        public string Name { get; set;} = string.Empty;
        public string Path { get; set;} = string.Empty;

        public DateTime Created { get; set; } = DateTime.Now;

        public byte[] Image { get; set; } = Array.Empty<byte>();

        public override string ToString()
        {
            return Name;
        }

        public static UiItem Map(Item item)
        {
            var dt = (item.metadata.FirstOrDefault(x => x.name == "createdDate")?.value as MetaDateTime)?.dateTime;
            //var ttt = (item.metadata.FirstOrDefault(x => x.name == "cover")?.value as MetaFile)?.value ?? Array.Empty<byte>();
            return new UiItem
            {
                Name = (item.metadata.FirstOrDefault(x => x.name == "name")?.value as MetaString)?.value ?? string.Empty,
                Created = dt.HasValue ? dt.Value : DateTime.Now,
                Path = (item.metadata.FirstOrDefault(x => x.name == "path")?.value as MetaString)?.value ?? string.Empty,
                Image = (item.metadata.FirstOrDefault(x => x.name == "cover")?.value as MetaFile)?.value ?? Array.Empty<byte>(),
            };
        } 
    }
}
