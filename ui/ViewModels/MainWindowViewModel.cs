﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Logging;
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
            Logger.Sink.Log(LogEventLevel.Information, nameof(MainWindowViewModel), "constructor", "init");
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

        public bool IsPopupVisible => !string.IsNullOrEmpty(notificationMessage);

        private string notificationMessage = string.Empty;

        public string NotificationMessage
        {
            get => notificationMessage;
            set
            {
                this.RaiseAndSetIfChanged(ref notificationMessage, value);
                this.RaisePropertyChanged(nameof(IsPopupVisible));
            }
        }

        public void OnNotificationCloseCommand()
        {
            NotificationMessage = string.Empty;
        }

        private string fileNames = string.Empty;
        

        public string FileNames
        {
            get => fileNames;
            set => this.RaiseAndSetIfChanged(ref fileNames, value);
        }

        private string ShortChecksum(string checksum) => checksum.Substring(0, 8);

        public void OnClickCommand(string checksum)
        {
            var result = bCollection.DeleteItem(checksum);
            if (result is Deleted deletedItem)
            {
                var existingItem = Items.FirstOrDefault(x => x.Checksum == checksum);
                if (existingItem is not null)
                {
                    Items.Remove(existingItem);
                }
                NotificationMessage = $"Deleted '{ShortChecksum(deletedItem.item.checksum.value)}'";
            }
        }

        public async Task UploadFiles(IEnumerable<string> fileNames)
        {
            foreach (var fileName in fileNames)
            {
                // TODO move to a service
                var data = await File.ReadAllBytesAsync(fileName);
                var item = await itemCreator.Create(fileName, data);
                
                var result = bCollection.AddItem(item);
                if (result is Added added)
                {
                    Items.Add(UiItem.Map(added.item));
                    NotificationMessage = $"Added '{ShortChecksum(added.item.checksum.value)}'";
                }
                else if (result is AlreadyExists existingItem)
                {
                    NotificationMessage = $"Already exists '{ShortChecksum(existingItem.item.checksum.value)}'";
                }
            }
        }

        private readonly IBCollection bCollection;
        private readonly IItemCreator itemCreator;

        public ObservableCollection<UiItem> Items { get; private set; }
    }
}
