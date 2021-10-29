using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Logging;
using Avalonia.Markup.Xaml;
using tb_ui.Models;
using tb_ui.ViewModels;

namespace tb_ui.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            SetupDnd();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        void SetupDnd()
        {
            AddHandler(DragDrop.DropEvent, Drop);
            AddHandler(DragDrop.DragOverEvent, DragOver);
        }

        private async void Drop(object? sender, DragEventArgs e)
        {
            if (e is null) return;
            if (this.DataContext is null) return;
            if (e.Data is null) return;
            if (e.Data.Contains(DataFormats.FileNames))
            {
                await ((MainWindowViewModel)this.DataContext).UploadFiles(e.Data.GetFileNames());
            }
        }

        private void DragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.DragEffects & DragDropEffects.Link;

            if (!e.Data.Contains(DataFormats.FileNames))
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        public void SearchTextInput(object s, KeyEventArgs e)
        {
            if (e is null) return;
            if (this.DataContext is null) return;
            ((MainWindowViewModel)this.DataContext).UpdateDisplayedBooks();
        }
        public void OnAddTagClick(object s, RoutedEventArgs e)
        {
            if (s is null) return;
            var btn  = (Button)s;
            if (this.DataContext is null) return;
            if (btn.DataContext is null) return;
            var checksum = ((UiBook)btn.DataContext).Checksum;
            var autoComplete = this.FindControl<AutoCompleteBox>("NewTagName");
            if (!autoComplete.IsFocused) autoComplete.Focus();
            Logger.Sink.Log(LogEventLevel.Information, nameof(OnAddTagClick), "OnAddTagClick", "" + autoComplete.IsFocused);
            ((MainWindowViewModel)this.DataContext).OnAddTag(checksum);
            Logger.Sink.Log(LogEventLevel.Information, nameof(OnAddTagClick), "OnAddTagClick", "" + autoComplete.IsFocused);
        }
    }
}
