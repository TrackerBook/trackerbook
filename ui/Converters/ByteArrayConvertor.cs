using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace tb_ui.Converters
{
    public static class ByteArrayConvertor
    {
        public static readonly IValueConverter ToBitmap =
            new FuncValueConverter<byte[], Bitmap?>(value =>
            {
                if (value == null || value.Length == 0)
                {
                    return null;
                }

                using var stream = new MemoryStream(value);
                return Bitmap.DecodeToHeight(stream, 100);
            });
    }
}
