﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Logging;
using tb_ui.Models;
using tb_lib.app;
using tb_lib.domain;
using tb_lib.infr;
using ReactiveUI;

namespace tb_ui.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel(IBCollection bCollection, IItemCreator itemCreator)
        {
            Logger.Sink.Log(LogEventLevel.Information, nameof(MainWindowViewModel), "constructor", "init");
            this.bCollection = bCollection;
            this.itemCreator = itemCreator;
            Items = new ObservableCollection<UiItem>(bCollection.GetItems().Where(x => !x.Deleted).Select(x => UiItem.Map(x)));
        }

        private UiItem? selecteditem;
        public UiItem? SelectedItem
        {
            get => selecteditem;
            set => this.RaiseAndSetIfChanged(ref selecteditem, value);
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

        private string ShortChecksum(string checksum) => checksum.Substring(0, 8);

        public void OnClickCommand(string checksum)
        {
            try
            {
                var item = bCollection.GetItems().SingleOrDefault(x => x.Id == checksum);
                if (item is null) return;
                var result = bCollection.UpdateItem(item with {Deleted = true});
                if (result is Updated deletedItem)
                {
                    var existingItem = Items.FirstOrDefault(x => x.Checksum == checksum);
                    if (existingItem is not null)
                    {
                        Items.Remove(existingItem);
                    }
                    NotificationMessage = $"Deleted '{ShortChecksum(deletedItem.item.Id)}'";
                }
            }
            catch (Exception ex)
            {
                NotificationMessage = ex.Message;
            }
        }

        public async Task UploadFiles(IEnumerable<string>? fileNames)
        {
            if (fileNames is null) return;
            foreach (var fileName in fileNames)
            {
                // TODO move to a service
                var data = await File.ReadAllBytesAsync(fileName);
                var item = await itemCreator.Create(fileName, data);
                
                var result = bCollection.AddItem(item);
                if (result is Added added)
                {
                    var itemToAdd = UiItem.Map(added.item);
                    Items.Add(itemToAdd);
                    NotificationMessage = $"Added '{ShortChecksum(added.item.Id)}'";
                    SelectedItem = itemToAdd;
                }
                else if (result is Updated updated)
                {
                    var itemToAdd = UiItem.Map(updated.item);
                    Items.Add(itemToAdd);
                    NotificationMessage = $"Restore '{ShortChecksum(updated.item.Id)}'";
                    SelectedItem = itemToAdd;
                }
                else if (result is AlreadyExists existingItem)
                {
                    NotificationMessage = $"Already exists '{ShortChecksum(existingItem.item.Id)}'";
                    SelectedItem = Items.FirstOrDefault(x => x.Checksum == existingItem.item.Id);
                }
            }
        }

        private readonly IBCollection bCollection;
        private readonly IItemCreator itemCreator;

        public ObservableCollection<UiItem> Items { get; private set; }
    }
}
