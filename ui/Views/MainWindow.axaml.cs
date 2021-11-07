using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
                var fileNames = e.Data.GetFileNames();
                if (fileNames is null) return;
                await ((MainWindowViewModel)this.DataContext).UploadFiles(fileNames.ToList());
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

        private void SearchTextInput(object s, KeyEventArgs e)
        {
            if (e is null) return;
            if (this.DataContext is null) return;
            ((MainWindowViewModel)this.DataContext).UpdateDisplayedBooks();
        }

        private async void OnFilesUpload(object s, RoutedEventArgs args)
        {
            if (this.DataContext is null) return;
            var dialog = new OpenFileDialog();
            dialog.Filters.Add(new FileDialogFilter { Name = "Books", Extensions = { "fb2", "pdf", "djvu", "epub" } });
            dialog.AllowMultiple = true;

            var result = await dialog.ShowAsync(this);

            if (result != null)
            {
                await ((MainWindowViewModel)this.DataContext).UploadFiles(result);
            }
        }
    }
}
