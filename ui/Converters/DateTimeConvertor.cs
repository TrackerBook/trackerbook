using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace tb_ui.Converters
{
    public static class DateTimeConvertor
    {
        public static readonly IValueConverter ToDateString =
            new FuncValueConverter<DateTime, string>(x => x.Date.ToShortDateString());
    }
}
