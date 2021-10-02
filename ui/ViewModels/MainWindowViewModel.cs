using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using bc_ui.Models;
using bcollection.app;
using ReactiveUI;

namespace bc_ui.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel(IBCollection bCollection)
        {
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

        public ObservableCollection<UiItem> Items { get; private set; }
    }
}
