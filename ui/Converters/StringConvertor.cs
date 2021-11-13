using Avalonia.Data.Converters;

namespace tb_ui.Converters
{
    public static class StringConvertor
    {
        public static readonly IValueConverter ToShort =
            new FuncValueConverter<string, string?>(value =>
                value.Length > 10 ? value.Substring(0, 10) + "..." : value);
    }
}
