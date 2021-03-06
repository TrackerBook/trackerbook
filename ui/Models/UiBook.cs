using System;
using System.Collections.ObjectModel;
using System.Linq;
using tb_lib.domain;

namespace tb_ui.Models
{
    public class UiBook
    {
        public string Checksum {get; set; } = string.Empty;
        public string Name { get; set;} = string.Empty;
        public string Path { get; set;} = string.Empty;
        public bool Deleted { get; set; } = false;
        public bool Finished { get; set; } = false;

        public char FirstLetter { get { return Name == string.Empty ? '-' : Name[0]; }}

        public DateTime Created { get; set; } = DateTime.Now;

        public byte[] Image { get; set; } = Array.Empty<byte>();

        public bool NoImage { get { return Image.Length == 0; }}

        public ObservableCollection<string> Tags { get; set; } = new ObservableCollection<string>();

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
                Checksum = item.Id,
                Deleted = item.Deleted,
                Finished = item.Read,
                Tags = new ObservableCollection<string>(item.Tags.Select(x => x.ToString()))
            };
        } 
    }
}
