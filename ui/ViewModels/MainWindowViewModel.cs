﻿using System;
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
            ExistingTags = GetAllTags();
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

        private const int notificationMessageHideDelay = 3000;
        public string NotificationMessage
        {
            get => notificationMessage;
            set
            {
                this.RaiseAndSetIfChanged(ref notificationMessage, value);
                this.RaisePropertyChanged(nameof(IsPopupVisible));
                Task.Delay(notificationMessageHideDelay).ContinueWith(x =>
                {
                    notificationMessage = string.Empty;
                    this.RaisePropertyChanged(nameof(IsPopupVisible));
                });
            }
        }

        public List<string> existingTags = new List<string>();

        public List<string> ExistingTags
        {
            get => existingTags;
            set => this.RaiseAndSetIfChanged(ref existingTags, value);
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

        private bool showAddTagWindow = false;

        public bool ShowAddTagWindow
        {
            get => showAddTagWindow;
            set
            {
                this.RaiseAndSetIfChanged(ref showAddTagWindow, value);
            }
        }

        private string addTagChecksum = string.Empty;
        private string newTag = string.Empty;

        public string NewTag
        {
            get => newTag;
            set
            {
                this.RaiseAndSetIfChanged(ref newTag, value);
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

        public void OnAddTag(string checksum)
        {
            ShowAddTagWindow = true;
            addTagChecksum = checksum;
        }

        public void OnAddTagClick()
        {
            ShowAddTagWindow = false;
            var book = bCollection.GetItems().SingleOrDefault(x => x.Id == addTagChecksum);
            if (book is null) return;
            if (book.Tags.Contains(NewTag)) return;
            book.Tags.Add(NewTag);
            var result = bCollection.UpdateItem(book);
            if (result is Updated updated)
            {
                var uiBook = Items.SingleOrDefault(x => x.Checksum == updated.item.Id);
                if (uiBook is null) return;
                if (uiBook.Tags.Contains(NewTag)) return;
                uiBook.Tags.Add(NewTag);
                var index = Items.IndexOf(uiBook);
                Items.RemoveAt(index);
                Items.Insert(index, uiBook);
                if (!ExistingTags.Contains(NewTag))
                {
                    ExistingTags.Add(NewTag);
                    var updatedExistingTags = new List<string>(ExistingTags);
                    ExistingTags = updatedExistingTags;
                }
                NewTag = string.Empty;
            }
            else if (result is Error error)
            {
                NotificationMessage = error.message;
            }
        }

        public void OnTagDelete(IReadOnlyCollection<object> values)
        {
            // TODO: pass argument without collection
            var checksum = values.First().ToString();
            var tagValue = values.Skip(1).First().ToString()!;
            var book = bCollection.GetItems().FirstOrDefault(x => x.Id == checksum);
            if (book is null) return;
            if (!book.Tags.Contains(tagValue)) return;
            book.Tags.Remove(tagValue);
            var result = bCollection.UpdateItem(book);
            if (result is Updated updated)
            {
                var uiBook = Items.SingleOrDefault(x => x.Checksum == updated.item.Id);
                if (uiBook is null) return;
                if (!uiBook.Tags.Contains(tagValue)) return;
                uiBook.Tags.Remove(tagValue);
                var index = Items.IndexOf(uiBook);
                Items.RemoveAt(index);
                Items.Insert(index, uiBook);
                if (ExistingTags.Contains(tagValue))
                {
                    ExistingTags.Remove(tagValue);
                    // TODO: how to rebind without creating a new list
                    var updatedExistingTags = new List<string>(ExistingTags);
                    ExistingTags = updatedExistingTags;
                }
                NotificationMessage = $"Updated {ShortChecksum(updated.item.Id)}";  
            }
            else if (result is Error error)
            {
                NotificationMessage = $"Error {error.message}";
            }
        }

        public void OnAddTagWindowClose()
        {
            ShowAddTagWindow = false;
            addTagChecksum = string.Empty;
            NewTag = string.Empty;
        }

        private List<string> GetAllTags() =>
            bCollection.GetItems().SelectMany(x => x.Tags).Distinct().ToList();

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
