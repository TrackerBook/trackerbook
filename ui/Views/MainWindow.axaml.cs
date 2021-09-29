using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using bc_ui.ViewModels;

namespace bc_ui.Views
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
            void DragOver(object sender, DragEventArgs e)
            {
                e.DragEffects = e.DragEffects & DragDropEffects.Link;

                if (!e.Data.Contains(DataFormats.FileNames))
                {
                    e.DragEffects = DragDropEffects.None;
                }
            }

            void Drop(object sender, DragEventArgs e)
            {
                if (e.Data.Contains(DataFormats.FileNames))
                {
                    ((MainWindowViewModel)this.DataContext).FileNames = string.Join(Environment.NewLine, e.Data.GetFileNames());
                }
            }


            AddHandler(DragDrop.DropEvent, Drop);
            AddHandler(DragDrop.DragOverEvent, DragOver);
        }

    }
}
