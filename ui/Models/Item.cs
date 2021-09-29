using System;

namespace bc_ui.Models
{
    public class Item
    {
        public string Name { get; set;} = string.Empty;
        public string Path { get; set;} = string.Empty;

        public DateTime Created { get; set; } = DateTime.Now;

        public override string ToString()
        {
            return Name;
        }
    }
}
