
#region Architecture
//

using System;
using System.Collections.Generic;

namespace tb_lib.domain
{
    public record BookCoverRef
    {
        public BookCoverRef(string id)
        {
            this.Id = id;
        }
        public string Id { get; init; }
    }
    public record CoverImage
    {
        public CoverImage(BookCoverRef reference, string name, byte[] data)
        {
            this.Reference =reference;
            this.Name = name;
            this.Data = data;
        }
        public BookCoverRef Reference { get; init; }
        public string Name { get; init; }
        public byte[] Data { get; init; }
    }
    public record Book
    {
        public Book(string checksum, string name, string path, string extension, CoverImage coverImage,
            bool deleted, bool read, ISet<Tag> tags, DateTime created)
        {
            this.Id = checksum;
            this.Name = name;
            this.Path = path;
            this.Extension = extension;
            this.CoverImage = coverImage;
            this.Deleted = deleted;
            this.Read = read;
            this.Tags = tags;
            this.Created = created;
        }
        public string Id { get; init; }
        public string Name { get; init; }
        public string Path { get; init; }
        public string Extension { get; init; }
        public CoverImage CoverImage { get; init; }
        public bool Deleted { get; init; }
        public bool Read { get; init; }
        public ISet<Tag> Tags { get; init; }
        public DateTime Created { get; init; }
    }

    public record Tag
    {
        public const int MaxSize = 128;
        public Tag(string value)
        {
            if (value.Length > MaxSize) throw new ArgumentOutOfRangeException(nameof(value));
            this.Value = value;
        }
        public string Value { get; init; }
        public override string ToString() => Value;
    }
    public interface Result { };
    public record Updated(Book item) : Result;
    public record Added(Book item) : Result;
    public record NotFound(Book item) : Result;
    public record AlreadyExists(Book item) : Result;
    public record Error(string message) : Result;
}

namespace tb_lib.app
{
    using System.Threading.Tasks;
    using tb_lib.domain;

    public interface IBCollection
    {
        Book[] GetItems();
        Result AddItem(Book item);
        Result UpdateItem(Book item);
    }
    public interface IChecksumCreator
    {
        Task<string> Create(byte[] data);
    }
}

namespace tb_lib.infr
{
    using System.Threading.Tasks;
    using tb_lib.domain;
    public interface IStorage
    {
        Book? Get(string checksum);
        Book[] Get();
        bool Post(Book item);
        bool Put(Book item);
        bool Delete(Book item);
        Book[] Find(string checksumPrefix);
    }
    public interface IFileStorage
    {
        bool Update(CoverImage coverImage);
        bool Add(CoverImage coverImage);
        bool Delete(BookCoverRef reference);
        CoverImage? Get(BookCoverRef reference);

    }
    public interface IBookCreator
    {
        Task<Book> Create(string path, byte[] data);
    }

    public interface ICoverExtractorFabric
    {
        ICoverExtractor Create(string extension);
    }

    public enum SupportedFileFormat
    {
        @default,
        fb2,
        pdf,
        epub
    }

    public interface ICoverExtractor
    {
        SupportedFileFormat FileFormat { get; }

        Task<byte[]> Extract(byte[] data);
    }

    public interface IFileRefIdCreator
    {
        string Create();
    }
}

#endregion

#region Implementation

namespace tb_lib.app
{
    using System;
    using System.Linq;
    using System.Security.Cryptography;
    using tb_lib.domain;
    using tb_lib.infr;
    using Microsoft.Extensions.Logging;
    using System.Threading.Tasks;
    using System.IO;

    public class ChecksumCreator : IChecksumCreator
    {
        public async Task<string> Create(byte[] data)
        {
            using var md5 = MD5.Create();
            using var memoryStream = new MemoryStream(data);
            var hash = await md5.ComputeHashAsync(memoryStream);
            var checksumValue = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return checksumValue;
        }
    }

    public class BCollection : IBCollection
    {
        private readonly ILogger<BCollection> logger;
        private readonly IStorage storage;
        private readonly IFileStorage fileStorage;

        public BCollection(ILoggerFactory loggerFactory, IStorage storage, IFileStorage fileStorage)
        {
            this.logger = loggerFactory.CreateLogger<BCollection>();
            this.storage = storage;
            this.fileStorage = fileStorage;
        }

        public Result AddItem(Book item)
        {
            if (item.Id is null) return new Error("Checksum is null.");
            using (this.logger.BeginScope(nameof(AddItem)))
            {
                var existingItem = this.storage.Get(item.Id);
                if (existingItem is not null)
                {
                    if (existingItem.Deleted)
                    {
                        return UpdateItem(item with {
                            CoverImage = item.CoverImage with { Reference = existingItem.CoverImage.Reference}});
                    }
                    return new AlreadyExists(existingItem);
                }
                if (!this.storage.Put(item))
                {
                    return new Error("Can't add item.");
                }
                if (item.CoverImage is not null)
                {
                    if (!this.fileStorage.Add(item.CoverImage))
                    {
                        return new Error("Can't upload cover image.");
                    }
                }
                return new Added(item);
            }
        }

        public Result UpdateItem(Book item)
        {
            if (item.Id is null) return new Error("Checksum is null.");
            using (this.logger.BeginScope(nameof(UpdateItem)))
            {
                var existingItem = this.storage.Get(item.Id);
                if (existingItem is null)
                {
                    return new NotFound(item);
                }
                if (!this.storage.Post(item))
                {
                    return new Error("Can't update item.");
                }
                if (item.CoverImage is not null)
                {
                    if (!this.fileStorage.Update(item.CoverImage))
                    {
                        return new Error("Can't update cover image.");
                    }
                }
                return new Updated(item);
            }
        }

        // TODO add paging with max = 100 books
        public Book[] GetItems()
        {
            return storage.Get().Select(x =>
            {
                if (x.CoverImage is not null)
                {
                    var coverImage = this.fileStorage.Get(x.CoverImage.Reference);
                    if (coverImage is not null)
                    {
                        return x with { CoverImage = coverImage};
                    }
                }
                return x;
            }).ToArray();
        }
    }
}

namespace tb_lib.infr
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using System.Xml;
    using tb_lib.app;
    using tb_lib.domain;
    using FB2Library;
    using LiteDB;
    using PDFiumCore;
    using SixLabors.ImageSharp;
    using SixLabors.ImageSharp.Formats.Jpeg;
    using SixLabors.ImageSharp.Processing;
    using VersOne.Epub;

    public class Storage : IStorage
    {
        static Storage()
        {
            const string name = "name";
            const string path = "path";
            const string extension = "extension";
            const string id = "_id";
            const string tags = "tags";
            const string read = "read";
            const string deleted = "deleted";
            const string coverImage = "coverImage";
            const string created = "created";
            BsonMapper.Global.RegisterType<Book>
            (
                serialize: (item) => new BsonDocument
                {
                    [id] = item.Id,
                    [name] = item.Name,
                    [path] = item.Path,
                    [extension] = item.Extension,
                    [tags] = new BsonArray(item.Tags.Select(x => new BsonValue(x.Value))),
                    [read] = item.Read,
                    [deleted] = item.Deleted,
                    [coverImage] = item.CoverImage.Reference.Id,
                    [created] = item.Created
                },
                deserialize: (bson) => new Book(
                        bson[id],
                        bson[name].AsString,
                        bson[path].AsString,
                        bson[extension].AsString,
                        new CoverImage(new BookCoverRef(bson[coverImage]), string.Empty, Array.Empty<byte>()),
                        bson[deleted].AsBoolean,
                        bson[read].AsBoolean,
                        bson[tags].AsArray.Select(x => new Tag(x.AsString)).ToHashSet(),
                        bson[created].AsDateTime)
            );
        }

        private T UsingDB<T>(Func<ILiteCollection<Book>, T> lambda)
        {
            using (var db = new LiteDatabase("track_books.db"))
            {
                var col = db.GetCollection<Book>("books");
                return lambda(col);
            }
        }

        public Book[] Get() => UsingDB(col => col.Query().ToArray());

        public bool Post(Book item) => UsingDB(col =>
        {
            return col.Update(item);
        });

        public bool Put(Book item) => UsingDB<bool>(col =>
        {
            var result = col.Insert(item);
            return result is not null;
        });

        public Book? Get(string checksum) => UsingDB<Book?>(col =>
        {
            return col.FindOne(Query.EQ("_id", checksum));
        });

        public bool Delete(Book item) => UsingDB(col =>
        {
            return col.DeleteMany(Query.EQ("_id", item.Id)) > 0;
        });

        public Book[] Find(string checksumPrefix) => UsingDB<Book[]>(col =>
        {
            return col.Find(Query.StartsWith("_id", checksumPrefix)).ToArray();
        });
    }

    public class FileStorage : IFileStorage
    {
        private T UsingDB<T>(Func<ILiteStorage<string>, T> lambda)
        {
            using (var db = new LiteDatabase("track_books.db"))
            {
                var storage = db.FileStorage;
                return lambda(storage);
            }
        }
        public bool Delete(BookCoverRef reference) => UsingDB<bool>(st =>
        {
            return st.Delete(reference.Id);
        });

        public CoverImage? Get(BookCoverRef reference) => UsingDB<CoverImage?>(st =>
        {
            var file = st.FindById(reference.Id);
            if (file is null) return null;
            using var memory = new MemoryStream();
            file.CopyTo(memory);
            return new CoverImage(reference, file.Filename, memory.ToArray());
        });

        public bool Add(CoverImage coverImage) => UsingDB<bool>(st =>
        {
            if (coverImage is null) return false;
            var file = st.FindById(coverImage.Reference.Id);
            if (file is not null) return false;
            using var memoryStream = new MemoryStream(coverImage.Data);
            st.Upload(coverImage.Reference.Id, coverImage.Name, memoryStream);
            return true;
        });

        public bool Update(CoverImage coverImage) => UsingDB<bool>(st =>
        {
            if (coverImage is null) return false;
            var file = st.FindById(coverImage.Reference.Id);
            if (file is null) return false;
            using var memoryStream = new MemoryStream(coverImage.Data);
            using var targetStream = file.OpenWrite();
            memoryStream.CopyTo(targetStream);
            return true;
        });
    }

    public class BookCreator : IBookCreator
    {
        private const string NameKey = "name";
        private const string PathKey = "path";
        private const string CreatedDateKey = "createdDate";
        private const string ExtensionKey = "ext";
        private readonly ICoverExtractorFabric coverExtractorFabric;
        private readonly IChecksumCreator checksumCreator;
        private readonly IFileRefIdCreator fileRefIdCreator;

        public BookCreator(
            IChecksumCreator checksumCreator,
            ICoverExtractorFabric coverExtractorFabric,
            IFileRefIdCreator fileRefIdCreator)
        {
            this.coverExtractorFabric = coverExtractorFabric;
            this.checksumCreator = checksumCreator;
            this.fileRefIdCreator = fileRefIdCreator;
        }

        public async Task<Book> Create(string path, byte[] data)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var checksum = await this.checksumCreator.Create(data);
            var extension = Path.GetExtension(path);

            var coverExtractor = coverExtractorFabric.Create(extension);
            var coverImageData = await coverExtractor.Extract(data); 
            var resizedImageDate = ImageProcessing.Resize(coverImageData);
            return new Book(checksum, name, path, extension, 
                new CoverImage(new BookCoverRef(fileRefIdCreator.Create()), "cover.jpg", resizedImageDate),
                false, false, Enumerable.Empty<Tag>().ToHashSet(), DateTime.UtcNow);
        }
    }

    public class CoverExtractorFabric : ICoverExtractorFabric
    {
        private readonly IEnumerable<ICoverExtractor> coverExtractors;

        public CoverExtractorFabric(IEnumerable<ICoverExtractor> metaExtractors)
        {
            this.coverExtractors = metaExtractors;
        }

        public ICoverExtractor Create(string extension) => this.coverExtractors
            .Single(x => x.FileFormat == extension switch
            {
                ".fb2" => SupportedFileFormat.fb2,
                ".pdf" => SupportedFileFormat.pdf,
                ".epub" => SupportedFileFormat.epub,
                _ => SupportedFileFormat.@default
            });
    }

    public class DefaultCoverExtractor : ICoverExtractor
    {
        public SupportedFileFormat FileFormat => SupportedFileFormat.@default;

        public Task<byte[]> Extract(byte[] data) => Task.FromResult(Array.Empty<byte>());
    }

    public class EpubCoverExtractor : ICoverExtractor
    {
        public SupportedFileFormat FileFormat => SupportedFileFormat.epub;

        public async Task<byte[]> Extract(byte[] data)
        {
            using var bookStream = new MemoryStream(data);
            var epubBook = await EpubReader.OpenBookAsync(bookStream);
            return await epubBook.ReadCoverAsync();
        }
    }

    public class Fb2CoverExtractor : ICoverExtractor
    {
        public SupportedFileFormat FileFormat => SupportedFileFormat.fb2;

        public async Task<byte[]> Extract(byte[] data)
        {
            using var stream = new MemoryStream(data);
            var fb2file = await ReadFB2FileStreamAsync(stream);
            var image = fb2file.TitleInfo?.Cover?.CoverpageImages.FirstOrDefault()?.HRef;
            var titleInfo = fb2file.TitleInfo;
            if (titleInfo != null && fb2file.Images.FirstOrDefault().Key == image?.Substring(1))
            {
                return fb2file.Images.FirstOrDefault().Value.BinaryData;
            }

            return Array.Empty<byte>();
        }

        private async Task<FB2File> ReadFB2FileStreamAsync(Stream stream)
        {
            // setup
            var readerSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore
            };
            var loadSettings = new XmlLoadSettings(readerSettings);

            // reading
            return await new FB2Reader().ReadAsync(stream, loadSettings);
        }
    }

    public class PdfCoverExtractor : ICoverExtractor
    {
        public SupportedFileFormat FileFormat => SupportedFileFormat.pdf;

        public Task<byte[]> Extract(byte[] data)
        {
            IntPtr unmanagedPointer = IntPtr.Zero;
            try
            {
                unmanagedPointer = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, unmanagedPointer, data.Length);
                var pageIndex = 0;
                var scale = 2;

                fpdfview.FPDF_InitLibrary();

                var document = fpdfview.FPDF_LoadMemDocument(unmanagedPointer, data.Length, null);

                var page = fpdfview.FPDF_LoadPage(document, pageIndex);

                var size = new FS_SIZEF_();
                fpdfview.FPDF_GetPageSizeByIndexF(document, 0, size);

                var width = (int)Math.Round(size.Width * scale);
                var height = (int)Math.Round(size.Height * scale);

                var bitmap = fpdfview.FPDFBitmapCreateEx(
                    width,
                    height,
                    4, // BGRA
                    IntPtr.Zero,
                    0);

                fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, (uint)System.Drawing.Color.White.ToArgb());

                // |          | a b 0 |
                // | matrix = | c d 0 |
                // |          | e f 1 |
                using var matrix = new FS_MATRIX_();
                using var clipping = new FS_RECTF_();

                matrix.A = scale;
                matrix.B = 0;
                matrix.C = 0;
                matrix.D = scale;
                matrix.E = 0;
                matrix.F = 0;

                clipping.Left = 0;
                clipping.Right = width;
                clipping.Bottom = 0;
                clipping.Top = height;

                fpdfview.FPDF_RenderPageBitmapWithMatrix(bitmap, page, matrix, clipping, (int)RenderFlags.RenderAnnotations);

                using var btm = new BmpStream(bitmap);

                var img = SixLabors.ImageSharp.Image.Load(btm);

                img.Mutate(x => x.BackgroundColor(Color.White));

                using var output = new MemoryStream();
                img.Save(output, new JpegEncoder());

                return Task.FromResult(output.ToArray());
            }
            finally
            {
                Marshal.FreeHGlobal(unmanagedPointer);
            }
        }

        private class BmpStream : Stream
        {
            const uint BmpHeaderSize = 14;
            const uint DibHeaderSize = 108; // BITMAPV4HEADER
            const uint PixelArrayOffset = BmpHeaderSize + DibHeaderSize;
            const uint CompressionMethod = 3; // BI_BITFIELDS
            const uint MaskR = 0x00_FF_00_00;
            const uint MaskG = 0x00_00_FF_00;
            const uint MaskB = 0x00_00_00_FF;
            const uint MaskA = 0xFF_00_00_00;

            const int BytesPerPixel = 4;

            readonly FpdfBitmapT _bitmap;
            readonly byte[] _header;
            private readonly IntPtr _scan0;
            readonly uint _length;
            readonly uint _stride;
            readonly uint _rowLength;
            private readonly int _widthBitmap;
            private readonly int _heightBitmap;
            uint _pos;
            private int _bitmapStride;
            static bool hasAlpha = true;

            public BmpStream(FpdfBitmapT bitmap, double dpiX = 72, double dpiY = 72)
            {
                _widthBitmap = fpdfview.FPDFBitmapGetWidth(bitmap);
                _heightBitmap = fpdfview.FPDFBitmapGetHeight(bitmap);
                _bitmap = bitmap;
                _rowLength = (uint)BytesPerPixel * (uint)_widthBitmap;
                _stride = (((uint)BytesPerPixel * 8 * (uint)_widthBitmap + 31) / 32) * 4;
                _length = PixelArrayOffset + _stride * (uint)_heightBitmap;
                _header = GetHeader(_length, dpiX, dpiY);
                _scan0 = fpdfview.FPDFBitmapGetBuffer(bitmap);
                _pos = 0;
                _bitmapStride = fpdfview.FPDFBitmapGetStride(bitmap);
            }

            private byte[] GetHeader(uint fileSize, double dpiX, double dpiY)
            {
                const double MetersPerInch = 0.0254;

                byte[] header = new byte[BmpHeaderSize + DibHeaderSize];

                using (var ms = new MemoryStream(header))
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write((byte)'B'); 
                    writer.Write((byte)'M');
                    writer.Write(fileSize);
                    writer.Write(0u);
                    writer.Write(PixelArrayOffset);
                    writer.Write(DibHeaderSize);
                    writer.Write(_widthBitmap);
                    writer.Write(-_heightBitmap); // top-down image
                    writer.Write((ushort)1);
                    writer.Write((ushort)(BytesPerPixel * 8));
                    writer.Write(CompressionMethod);
                    writer.Write(0);
                    writer.Write((int)Math.Round(dpiX / MetersPerInch));
                    writer.Write((int)Math.Round(dpiY / MetersPerInch));
                    writer.Write(0L);
                    writer.Write(MaskR);
                    writer.Write(MaskG);
                    writer.Write(MaskB);
                    if (hasAlpha)
                        writer.Write(MaskA);
                }
                return header;
            }

            public override bool CanRead => true;

            public override bool CanSeek => true;

            public override bool CanWrite => false;

            public override long Length => _length;

            public override long Position
            {
                get => _pos;
                set
                {
                    if (value < 0 || value >= _length)
                        throw new ArgumentOutOfRangeException();
                    _pos = (uint)value;
                }
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int bytesToRead = count;
                int returnValue = 0;
                if (_pos < PixelArrayOffset)
                {
                    returnValue = Math.Min(count, (int)(PixelArrayOffset - _pos));
                    Buffer.BlockCopy(_header, (int)_pos, buffer, offset, returnValue);
                    _pos += (uint)returnValue;
                    offset += returnValue;
                    bytesToRead -= returnValue;
                }

                if (bytesToRead <= 0)
                    return returnValue;

                bytesToRead = Math.Min(bytesToRead, (int)(_length - _pos));
                uint idxBuffer = _pos - PixelArrayOffset;

                if (_stride == _bitmapStride)
                {
                    Marshal.Copy(_scan0 + (int)idxBuffer, buffer, offset, bytesToRead);
                    returnValue += bytesToRead;
                    _pos += (uint)bytesToRead;
                    return returnValue;
                }

                while (bytesToRead > 0)
                {
                    int idxInStride = (int)(idxBuffer / _stride);
                    int leftInRow = Math.Max(0, (int)_rowLength - idxInStride);
                    int paddingBytes = (int)(_stride - _rowLength);
                    int read = Math.Min(bytesToRead, leftInRow);
                    if (read > 0)
                        Marshal.Copy(_scan0 + (int)idxBuffer, buffer, offset, read);
                    offset += read;
                    idxBuffer += (uint)read;
                    bytesToRead -= read;
                    returnValue += read;
                    read = Math.Min(bytesToRead, paddingBytes);
                    for (int i = 0; i < read; i++)
                        buffer[offset + i] = 0;
                    offset += read;
                    idxBuffer += (uint)read;
                    bytesToRead -= read;
                    returnValue += read;
                }
                _pos = PixelArrayOffset + (uint)idxBuffer;
                return returnValue;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                if (origin == SeekOrigin.Begin)
                    Position = offset;
                else if (origin == SeekOrigin.Current)
                    Position += offset;
                else if (origin == SeekOrigin.End)
                    Position = Length + offset;
                return Position;
            }

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override void Close()
            {
                Marshal.FreeHGlobal(_scan0);
                base.Close();
            }
        }
    }

    public class FileRefIdCreator : IFileRefIdCreator
    {
        public string Create() => "$" + Guid.NewGuid().ToString("N");
    }

    public static class ImageProcessing
    {
        public static byte[] Resize(byte[] imageBytes)
        {
            if (imageBytes.Length == 0) return imageBytes;
            const int size = 100;
            using var memoryStream = new MemoryStream(imageBytes);
            using var image = SixLabors.ImageSharp.Image.Load(memoryStream);
            int width, height;
            if (image.Width > image.Height)
            {
                width = size;
                height = Convert.ToInt32(image.Height * size / (double)image.Width);
            }
            else
            {
                width = Convert.ToInt32(image.Width * size / (double)image.Height);
                height = size;
            }
            using var output = new MemoryStream();
            image.Save(output, new JpegEncoder());

            return output.ToArray();
        }
    }
}


#endregion
