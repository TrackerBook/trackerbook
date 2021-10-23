using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace tb_ui.Views
{
    public partial class Tag : UserControl
    {
        public Tag()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
