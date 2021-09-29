using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using bc_ui.Models;
using ReactiveUI;

namespace bc_ui.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel()
        {
            var result = new List<Item>();
            for (var i = 0; i < 100; i++)
            {
                result.Add(new Item { Name = "Name of the book" + i, Path = "C:\\Windows\\Local\\Documents\\test" + i });
            }
            Items = new ObservableCollection<Item>(result);
        }

        private string searchText = string.Empty;
        public string SearchText
        {
            get => searchText;
            set => this.RaiseAndSetIfChanged(ref searchText, value);
        }

        private string fileNames = string.Empty;
        public string FileNames
        {
            get => fileNames;
            set => this.RaiseAndSetIfChanged(ref fileNames, value);
        }

        public ObservableCollection<Item> Items { get; private set; }
    }
}
