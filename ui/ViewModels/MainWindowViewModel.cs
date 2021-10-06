using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using bc_ui.Models;
using bcollection.app;
using bcollection.domain;
using bcollection.infr;
using ReactiveUI;

namespace bc_ui.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel(IBCollection bCollection, IItemCreator itemCreator)
        {
            this.bCollection = bCollection;
            this.itemCreator = itemCreator;
            Items = new ObservableCollection<UiItem>(bCollection.GetItems().Select(x => UiItem.Map(x)));
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

        public void OnClickCommand(string checksum)
        {
            bCollection.DeleteItem(checksum);
        }

        public async Task UploadFiles(IEnumerable<string> fileNames)
        {
            foreach (var fileName in fileNames)
            {
                // TODO move to a service
                var data = await File.ReadAllBytesAsync(fileName);
                var item = await itemCreator.Create(fileName, data);
                
                var result = bCollection.AddItem(item);
                if (result is Added)
                {
                    Console.WriteLine("Added" + item.checksum.value);
                }
                else if (result is AlreadyExists)
                {
                    Console.WriteLine("AlreadyExists" + item.checksum.value);
                }
            }
        }

        private readonly IBCollection bCollection;
        private readonly IItemCreator itemCreator;

        public ObservableCollection<UiItem> Items { get; private set; }
    }
}
