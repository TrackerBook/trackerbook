using System;
using System.Drawing;
using System.IO;
using System.Linq;
using tb_lib.domain;

namespace tb_ui.Models
{
    public class UiBook
    {
        public string Checksum {get; set; } = string.Empty;
        public string Name { get; set;} = string.Empty;
        public string Path { get; set;} = string.Empty;

        public char FirstLetter { get { return Name == string.Empty ? '-' : Name[0]; }}

        public DateTime Created { get; set; } = DateTime.Now;

        public byte[] Image { get; set; } = Array.Empty<byte>();

        public bool NoImage { get { return Image.Length == 0; }}

        public override string ToString()
        {
            return Name;
        }

        public static UiBook Map(Book item)
        {
            return new UiBook
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
