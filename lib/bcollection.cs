
#region Architecture
//

using System;
using System.Collections.Generic;

namespace bcollection.domain
{
    public record ItemFileRef
    {
        public ItemFileRef(string id)
        {
            this.Id = id;
        }
        public string Id { get; set; }
    }
    public record CoverImage
    {
        public CoverImage(ItemFileRef reference, string name, byte[] data)
        {
            this.Reference =reference;
            this.Name = name;
            this.Data = data;
        }
        public ItemFileRef Reference { get; set; }
        public string Name { get; set; }
        public byte[] Data { get; set; }
    }
    public record Item
    {
        public Item(string checksum, string name, string path, string extension, CoverImage coverImage,
            bool deleted, bool read, List<string> tags, DateTime created)
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
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Extension { get; set; }
        public CoverImage CoverImage { get; set; }
        public bool Deleted { get; set; }
        public bool Read { get; set; }
        public List<string> Tags { get; set; }
        public DateTime Created { get; set; }
    }
    public interface Result { };
    public record Updated(Item item) : Result;
    public record Added(Item item) : Result;
    public record NotFound(Item item) : Result;
    public record AlreadyExists(Item item) : Result;
    public record Error(string message) : Result;
}

namespace bcollection.app
{
    using bcollection.domain;

    public interface IBCollection
    {
        Item[] GetItems();
        Result AddItem(Item item);
        Result UpdateItem(Item item);
    }
    public interface IChecksumCreator
    {
        string Create(byte[] data);
    }
}

namespace bcollection.infr
{
    using System.Threading.Tasks;
    using bcollection.domain;
    public interface IStorage
    {
        Item? Get(string checksum);
        Item[] Get();
        bool Post(Item item);
        bool Put(Item item);
        bool Delete(Item item);
        Item[] Find(string checksumPrefix);
    }
    public interface IFileStorage
    {
        bool Update(CoverImage coverImage);
        bool Add(CoverImage coverImage);
        bool Delete(ItemFileRef reference);
        CoverImage? Get(ItemFileRef reference);

    }
    public interface IItemCreator
    {
        Task<Item> Create(string path, byte[] data);
    }

    public interface ICoverExtractorFabric
    {
        ICoverExtractor Create(string extension);
    }

    public enum SupportedFileFormat
    {
        @default,
        fb2,
        pdf
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

namespace bcollection.app
{
    using System;
    using System.Linq;
    using System.Security.Cryptography;
    using bcollection.domain;
    using bcollection.infr;
    using Microsoft.Extensions.Logging;

    public class ChecksumCreator : IChecksumCreator
    {
        public string Create(byte[] data)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(data);
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

        public Result AddItem(Item item)
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

        public Result UpdateItem(Item item)
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

        public Item[] GetItems()
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

namespace bcollection.infr
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using System.Xml;
    using bcollection.app;
    using bcollection.domain;
    using FB2Library;
    using LiteDB;
    using PDFiumCore;
    using SixLabors.ImageSharp;
    using SixLabors.ImageSharp.Formats.Jpeg;
    using SixLabors.ImageSharp.Processing;

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
            BsonMapper.Global.RegisterType<Item>
            (
                serialize: (item) => new BsonDocument
                {
                    [id] = item.Id,
                    [name] = item.Name,
                    [path] = item.Path,
                    [extension] = item.Extension,
                    [tags] = new BsonArray(item.Tags.Select(x => new BsonValue(x))),
                    [read] = item.Read,
                    [deleted] = item.Deleted,
                    [coverImage] = item.CoverImage.Reference.Id,
                    [created] = item.Created
                },
                deserialize: (bson) => new Item(
                        bson[id],
                        bson[name].AsString,
                        bson[path].AsString,
                        bson[extension].AsString,
                        new CoverImage(new ItemFileRef(bson[coverImage]), string.Empty, Array.Empty<byte>()),
                        bson[deleted].AsBoolean,
                        bson[read].AsBoolean,
                        bson[tags].AsArray.Select(x => x.AsString).ToList(),
                        bson[created].AsDateTime)
            );
        }

        private T UsingDB<T>(Func<ILiteCollection<Item>, T> lambda)
        {
            using (var db = new LiteDatabase("bcollection.db"))
            {
                var col = db.GetCollection<Item>("items");
                return lambda(col);
            }
        }

        public Item[] Get() => UsingDB(col => col.Query().ToArray());

        public bool Post(Item item) => UsingDB(col =>
        {
            return col.Update(item);
        });

        public bool Put(Item item) => UsingDB<bool>(col =>
        {
            var result = col.Insert(item);
            return result is not null;
        });

        public Item? Get(string checksum) => UsingDB<Item?>(col =>
        {
            return col.FindOne(Query.EQ("_id", checksum));
        });

        public bool Delete(Item item) => UsingDB(col =>
        {
            return col.DeleteMany(Query.EQ("_id", item.Id)) > 0;
        });

        public Item[] Find(string checksumPrefix) => UsingDB<Item[]>(col =>
        {
            return col.Find(Query.StartsWith("_id", checksumPrefix)).ToArray();
        });
    }

    public class FileStorage : IFileStorage
    {
        private T UsingDB<T>(Func<ILiteStorage<string>, T> lambda)
        {
            using (var db = new LiteDatabase("bcollection.db"))
            {
                var storage = db.FileStorage;
                return lambda(storage);
            }
        }
        public bool Delete(ItemFileRef reference) => UsingDB<bool>(st =>
        {
            return st.Delete(reference.Id);
        });

        public CoverImage? Get(ItemFileRef reference) => UsingDB<CoverImage?>(st =>
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

    public class ItemCreator : IItemCreator
    {
        private const string NameKey = "name";
        private const string PathKey = "path";
        private const string CreatedDateKey = "createdDate";
        private const string ExtensionKey = "ext";
        private readonly ICoverExtractorFabric coverExtractorFabric;
        private readonly IChecksumCreator checksumCreator;
        private readonly IFileRefIdCreator fileRefIdCreator;

        public ItemCreator(
            IChecksumCreator checksumCreator,
            ICoverExtractorFabric coverExtractorFabric,
            IFileRefIdCreator fileRefIdCreator)
        {
            this.coverExtractorFabric = coverExtractorFabric;
            this.checksumCreator = checksumCreator;
            this.fileRefIdCreator = fileRefIdCreator;
        }

        public async Task<Item> Create(string path, byte[] data)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var checksum = this.checksumCreator.Create(data);
            var extension = Path.GetExtension(path);

            var coverExtractor = coverExtractorFabric.Create(extension);
            var coverImageData = await coverExtractor.Extract(data); 
            return new Item(checksum, name, path, extension, 
                new CoverImage(new ItemFileRef(fileRefIdCreator.Create()), "cover.jpg", coverImageData),
                false, false, Enumerable.Empty<string>().ToList(), DateTime.UtcNow);
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
                _ => SupportedFileFormat.@default
            });
    }

    public class DefaultCoverExtractor : ICoverExtractor
    {
        public SupportedFileFormat FileFormat => SupportedFileFormat.@default;

        public Task<byte[]> Extract(byte[] data) => Task.FromResult(Array.Empty<byte>());
    }

    public class Fb2MetaExtractor : ICoverExtractor
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

    public class PdfMetaExtractor : ICoverExtractor
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

                return Task.FromResult(ImageProcessing.Resize(output.ToArray()));
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
