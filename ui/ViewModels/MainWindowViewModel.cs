using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        public MainWindowViewModel(IBCollection bCollection, IBookCreator itemCreator)
        {
            Logger.Sink.Log(LogEventLevel.Information, nameof(MainWindowViewModel), "constructor", "init");
            this.bCollection = bCollection;
            this.itemCreator = itemCreator;
            Items = new ObservableCollection<UiBook>(GetBooksToDisplay());
        }

        private UiBook? selecteditem;
        public UiBook? SelectedItem
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

        private bool showDeleted = false;

        public bool ShowDeleted
        {
            get => showDeleted;
            set
            {
                this.RaiseAndSetIfChanged(ref showDeleted, value);
            }
        }

        private bool showFinished = false;

        public bool ShowFinished
        {
            get => showFinished;
            set
            {
                this.RaiseAndSetIfChanged(ref showFinished, value);
            }
        }

        private string ShortChecksum(string checksum) => checksum.Substring(0, 8);

        public void FinishBook(string checksum)
        {
            var uiBook = Items.SingleOrDefault(x => x.Checksum == checksum);
            if (uiBook == null) return;
            var item = bCollection.GetItems().SingleOrDefault(x => x.Id == checksum);
            if (item is null) return;
            var result = bCollection.UpdateItem(item with {Read = uiBook.Finished});
            if (result is Updated updatedItem)
            {
                NotificationMessage = $"Updated '{ShortChecksum(updatedItem.item.Id)}'";
            }
            else if (result is Error error)
            {
                NotificationMessage = $"Error '{error.message}'";
            }
        }

        public void OnDeleteCommand(string checksum)
        {
            var item = bCollection.GetItems().SingleOrDefault(x => x.Id == checksum);
            if (item is null) return;
            var result = bCollection.UpdateItem(item with {Deleted = true});
            if (result is Updated deletedItem)
            {
                var existingItem = Items.FirstOrDefault(x => x.Checksum == checksum);
                if (existingItem is not null)
                {
                    if (showDeleted)
                    {
                        var index = Items.IndexOf(existingItem);
                        existingItem.Deleted = true;
                        Items.RemoveAt(index);
                        Items.Insert(index, existingItem);
                    }
                    else
                    {
                        Items.Remove(existingItem);
                    }
                }
                NotificationMessage = $"Deleted '{ShortChecksum(deletedItem.item.Id)}'";
            }
            else if (result is Error error)
            {
                NotificationMessage = $"Error '{ShortChecksum(error.message)}'";
            }
        }

        public void OnRestoreCommand(string checksum)
        {
            var item = bCollection.GetItems().SingleOrDefault(x => x.Id == checksum);
            if (item is null) return;
            var result = bCollection.UpdateItem(item with {Deleted = false});
            if (result is Updated restoredItem)
            {
                var existingItem = Items.FirstOrDefault(x => x.Checksum == checksum);
                if (existingItem is null)
                {
                    Items.Add(UiBook.Map(restoredItem.item));
                }
                else
                {
                    var index = Items.IndexOf(existingItem);
                    existingItem.Deleted = false;
                    Items.RemoveAt(index);
                    Items.Insert(index, existingItem);
                }
                NotificationMessage = $"Restored '{ShortChecksum(restoredItem.item.Id)}'";
            }
            else if (result is Error error)
            {
                NotificationMessage = $"Error '{ShortChecksum(error.message)}'";
            }
        }

        private List<UiBook> GetBooksToDisplay() =>
            bCollection.GetItems()
                .Where(x => showDeleted || !x.Deleted)
                .Where(x => showFinished || !x.Read)
                .Select(x => UiBook.Map(x)).ToList();

        public void DeleteFinishedSwitched()
        {
            var previousSelected = SelectedItem;
            SelectedItem = null;
            var toDisplay = GetBooksToDisplay();
            var toDelete = Items.Where(y => toDisplay.All(x => x.Checksum != y.Checksum)).ToList();
            foreach (var book in toDisplay.Where(y => !Items.Any(x => x.Checksum == y.Checksum)))
            {
                Items.Add(book);
            }
            foreach (var book in toDelete)
            {
                Items.Remove(book);
            }
            if (previousSelected != null && Items.Any(x => x.Checksum == previousSelected.Checksum))
            {
                SelectedItem = Items.Single(x => x.Checksum == previousSelected.Checksum);
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
                    var itemToAdd = UiBook.Map(added.item);
                    Items.Add(itemToAdd);
                    NotificationMessage = $"Added '{ShortChecksum(added.item.Id)}'";
                    SelectedItem = itemToAdd;
                }
                else if (result is Updated updated)
                {
                    var itemToAdd = UiBook.Map(updated.item);
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
        private readonly IBookCreator itemCreator;

        public ObservableCollection<UiBook> Items { get; private set; }
    }
}
