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
using System;

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
            ExistingTags = new ObservableCollection<string>(GetAllTags());
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

        public ObservableCollection<string> ExistingTags { get; set; }

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
                if (value.Length > Tag.MaxSize) return;
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
            var newValue = uiBook.Finished;
            var result = bCollection.UpdateItem(item with { Read = newValue });
            if (result is Updated updatedItem)
            {
                var existingItem = Items.FirstOrDefault(x => x.Checksum == checksum);
                if (existingItem is not null)
                {
                    if (showFinished)
                    {
                        var index = Items.IndexOf(existingItem);
                        existingItem.Finished = newValue;
                        Items.RemoveAt(index);
                        Items.Insert(index, existingItem);
                    }
                    else
                    {
                        Items.Remove(existingItem);
                    }
                }
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
            var result = bCollection.UpdateItem(item with { Deleted = true });
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
            var result = bCollection.UpdateItem(item with { Deleted = false });
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
            var added = book.Tags.Add(new Tag(NewTag));
            if (!added) return;
            var result = bCollection.UpdateItem(book);
            if (result is Updated updated)
            {
                var uiBook = Items.SingleOrDefault(x => x.Checksum == updated.item.Id);
                if (uiBook is null) return;
                if (uiBook.Tags.Contains(NewTag)) return;
                uiBook.Tags.Add(NewTag);
                if (!ExistingTags.Contains(NewTag))
                {
                    ExistingTags.Add(NewTag);
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
            var deleted = book.Tags.Remove(new Tag(tagValue));
            if (!deleted) return;
            var result = bCollection.UpdateItem(book);
            if (result is Updated updated)
            {
                var uiBook = Items.SingleOrDefault(x => x.Checksum == updated.item.Id);
                if (uiBook is null) return;
                if (!uiBook.Tags.Contains(tagValue)) return;
                uiBook.Tags.Remove(tagValue);
                var index = Items.IndexOf(uiBook);
                if (ExistingTags.Contains(tagValue))
                {
                    ExistingTags.Remove(tagValue);
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
            // TODO move to bCollection
            bCollection.GetItems().SelectMany(x => x.Tags).Select(x => x.ToString()).Distinct().ToList();

        private List<UiBook> GetBooksToDisplay() =>
            bCollection.GetItems()
                .Where(x => showDeleted || !x.Deleted)
                .Where(x => showFinished || !x.Read)
                .Where(x => SearchText.Length < 3
                    || x.Tags.Any(t => t.Value.Contains(SearchText, StringComparison.InvariantCultureIgnoreCase))
                        || x.Name.Contains(SearchText, StringComparison.InvariantCultureIgnoreCase))
                .Select(x => UiBook.Map(x)).ToList();

        public void UpdateDisplayedBooks()
        {
            var previousSelected = SelectedItem;
            SelectedItem = null;
            var filtered = GetBooksToDisplay();
            var toAdd = filtered.Where(x => Items.All(i => i.Checksum != x.Checksum)).ToList();
            var toDelete = Items.Where(x => filtered.All(f => f.Checksum != x.Checksum)).ToList();
            foreach (var item in toDelete)
            {
                Items.Remove(item);
            }
            foreach (var item in toAdd)
            {
                Items.Add(item);
            }
            if (previousSelected != null && Items.Any(x => x.Checksum == previousSelected.Checksum))
            {
                SelectedItem = Items.Single(x => x.Checksum == previousSelected.Checksum);
            }
        }

        public async Task UploadFiles(ICollection<string>? fileNames)
        {
            if (fileNames is null) return;
            var index = 1;
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
                    NotificationMessage = $"Added {index}/{fileNames.Count}:'{ShortChecksum(added.item.Id)}'";
                    SelectedItem = itemToAdd;
                }
                else if (result is Updated updated)
                {
                    var itemToAdd = UiBook.Map(updated.item);
                    Items.Add(itemToAdd);
                    NotificationMessage = $"Restore {index}/{fileNames.Count}:'{ShortChecksum(updated.item.Id)}'";
                    SelectedItem = itemToAdd;
                }
                else if (result is AlreadyExists existingItem)
                {
                    NotificationMessage = $"Already exists {index}/{fileNames.Count}:'{ShortChecksum(existingItem.item.Id)}'";
                    SelectedItem = Items.FirstOrDefault(x => x.Checksum == existingItem.item.Id);
                }
                index++;
            }
        }

        private readonly IBCollection bCollection;
        private readonly IBookCreator itemCreator;

        public ObservableCollection<UiBook> Items { get; private set; }
    }
}
