using System;
using System.Drawing;
using System.IO;
using System.Linq;
using bcollection.domain;

namespace bc_ui.Models
{
    public class UiItem
    {
        public string Checksum {get; set; } = string.Empty;
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
            return new UiItem
            {
                Name = item.Name,
                Created = item.Created,
                Path = item.Path,
                Image = item.CoverImage.Data,
                Checksum = item.Id
            };
        } 
    }
}
