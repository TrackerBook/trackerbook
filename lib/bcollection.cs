using System;

#region Architecture
//

namespace bcollection.domain
{
    public record ItemPath(string value);
    public record Checksum(string value);
    public record ItemFileRef(string id);
    public interface IMetaValue { }
    public record MetaNumber(int value) : IMetaValue;
    public record MetaString(string value) : IMetaValue;
    public record MetaFile(ItemFileRef reference, string fileName, byte[]? value) : IMetaValue;
    public record MetaDateTime(DateTime dateTime) : IMetaValue;
    public record MetaData(string name, IMetaValue value);
    public record Item(Checksum checksum, MetaData[] metadata);
    public interface Result { };
    public record Updated(Item item) : Result;
    public record Deleted(Item item) : Result;
    public record Added(Item item) : Result;
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
        Result AddMetadata(Item item, MetaData tag);
        Result DeleteMetadata(Item item, MetaData tag);
        Result DeleteItem(string checksum);
        Item[] Find(string checksumPrefix);
    }
    public interface IChecksumCreator
    {
        Checksum Create(byte[] data);
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
        bool Post(MetaFile itemFile);
        bool Delete(ItemFileRef reference);
        MetaFile? Get(ItemFileRef reference);

    }
    public interface IItemCreator
    {
        Task<Item> Create(string path, byte[] data);
    }

    public interface IMetaExtractorFabric
    {
        IMetaExtractor[] Create(string extension);
    }

    public enum SupportedFileFormats
    {
        noop,
        fb2,
        pdf
    }

    public interface IMetaExtractor
    {
        SupportedFileFormats FileFormat { get; }

        Task<MetaData[]> Extract(byte[] data);
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
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using bcollection.domain;
    using bcollection.infr;
    using Microsoft.Extensions.Logging;

    public class ChecksumCreator : IChecksumCreator
    {
        public Checksum Create(byte[] data)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(data);
            var checksumValue = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return new Checksum(checksumValue);
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
            using (this.logger.BeginScope(nameof(AddItem)))
            {
                var existingItem = this.storage.Get(item.checksum.value);
                if (existingItem is not null)
                {
                    return new AlreadyExists(existingItem);
                }
                if (!this.storage.Put(item))
                {
                    return new Error("Can't add item.");
                }
                foreach (var fileMeta in item.metadata.Select(x => x.value).OfType<MetaFile>())
                {
                    if (!this.fileStorage.Post(fileMeta))
                    {
                        return new Error("Can't upload file.");
                    }
                }
                return new Added(item);
            }
        }

        public Result AddMetadata(Item item, MetaData tag)
        {
            if (tag.value is MetaFile fileMeta && !this.fileStorage.Post(fileMeta))
            {
                return new Error("Can't upload file.");
            }
            var currentMeta = item.metadata;
            var extendedMeta = new MetaData[currentMeta.Length + 1];
            Array.Copy(currentMeta, extendedMeta, currentMeta.Length);
            extendedMeta[extendedMeta.Length - 1] = tag;

            return storage.Post(item with { metadata = extendedMeta })
                ? new Updated(item)
                : new Error("Can't add metadata.");
        }

        public Result DeleteItem(string checksum)
        {
            var item = this.storage.Get(checksum);
            if (item is null)
            {
                return new Error("Can't find item with checksum.");
            }
            foreach (var fileMeta in item.metadata.Select(x => x.value).OfType<MetaFile>())
            {
                if (!this.fileStorage.Delete(fileMeta.reference))
                {
                    return new Error("Can't delete file.");
                }
            }
            return this.storage.Delete(item)
                ? new Deleted(item)
                : new Error("Can't delete item.");
        }

        public Result DeleteMetadata(Item item, MetaData meta)
        {
            if (meta.value is MetaFile fileMeta && !this.fileStorage.Post(fileMeta))
            {
                return new Error("Can't delete file.");
            }
            var currentMeta = item.metadata;
            var reducedMeta = new MetaData[currentMeta.Length - 1];
            var i = 0;
            foreach (var tagCurrent in item.metadata)
            {
                if (tagCurrent != meta)
                {
                    reducedMeta[i++] = tagCurrent;
                }
            }
            return storage.Post(item with { metadata = reducedMeta })
                ? new Updated(item)
                : new Error("Can't delete metadata.");
        }

        public Item[] Find(string checksumPrefix) => this.storage.Find(checksumPrefix);

        public Item[] GetItems()
        {
            return storage.Get().Select(x =>
            {
                var updatedMetaData = new List<MetaData>();
                foreach (var currentMD in x.metadata)
                {
                    if (currentMD.value is MetaFile mf)
                    {
                        var withData = this.fileStorage.Get(mf.reference);
                        if (withData is not null)
                        {
                            updatedMetaData.Add(new MetaData(currentMD.name, withData));
                        }
                    }
                    else
                    {
                        updatedMetaData.Add(currentMD);
                    }
                }
                return new Item(x.checksum, updatedMetaData.ToArray());
            }).ToArray();
        }
    }
}

namespace bcollection.infr
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using System.Xml;
    using bcollection.app;
    using bcollection.domain;
    using FB2Library;
    using LiteDB;
    //using UglyToad.PdfPig;
    using PDFiumCore;

    public class Storage : IStorage
    {
        static Storage()
        {
            const string name = "name";
            const string value = "value";
            const string checksum = "checksum";
            const string metadata = "metadata";
            BsonMapper.Global.RegisterType<Item>
            (
                serialize: (item) => new BsonDocument
                {
                    [checksum] = item.checksum.value,
                    [metadata] = new BsonArray(item.metadata.Select(x => new BsonDocument
                    {
                        [name] = x.name,
                        [value] = x.value switch
                        {
                            MetaString ms => ms.value,
                            MetaDateTime md => md.dateTime,
                            MetaNumber mn => mn.value,
                            MetaFile mf => mf.reference.id,
                            _ => throw new NotImplementedException()
                        }
                    }))
                },
                deserialize: (bson) => new Item(
                        new Checksum(bson[checksum]),
                        bson[metadata].AsArray.Select(x => new MetaData(x[name], x[value] switch
                        {
                            _ when x[value].IsString && x[value].AsString.StartsWith("$") =>
                                (IMetaValue)new MetaFile(new ItemFileRef(x[value].AsString), string.Empty, null),
                            _ when x[value].IsString => (IMetaValue)new MetaString(x[value].AsString),
                            _ when x[value].IsInt32 => (IMetaValue)new MetaNumber(x[value].AsInt32),
                            _ when x[value].IsDateTime => (IMetaValue)new MetaDateTime(x[value].AsDateTime),
                            _ => throw new NotImplementedException()
                        })).ToArray())
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
            return col.FindOne(Query.EQ("checksum", checksum));
        });

        public bool Delete(Item item) => UsingDB(col =>
        {
            return col.DeleteMany(Query.EQ("checksum", item.checksum.value)) > 0;
        });

        public Item[] Find(string checksumPrefix) => UsingDB<Item[]>(col =>
        {
            return col.Find(Query.StartsWith("checksum", checksumPrefix)).ToArray();
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
            return st.Delete(reference.id);
        });

        public MetaFile? Get(ItemFileRef reference) => UsingDB<MetaFile>(st =>
        {
            var file = st.FindById(reference.id);
            if (file is null)
            {
                return null;
            }
            using var memory = new MemoryStream();
            file.CopyTo(memory);
            return new MetaFile(reference, file.Filename, memory.ToArray());
        });

        public bool Post(MetaFile itemFile) => UsingDB<bool>(st =>
        {
            if (itemFile.value is null)
            {
                return false;
            }
            var file = st.FindById(itemFile.reference.id);
            using var memoryStream = new MemoryStream(itemFile.value);
            if (file is null)
            {
                st.Upload(itemFile.reference.id, itemFile.fileName, memoryStream);
            }
            else
            {
                using var targetStream = file.OpenWrite();
                memoryStream.CopyTo(targetStream);
            }
            return true;
        });
    }

    public class ItemCreator : IItemCreator
    {
        private const string NameKey = "name";
        private const string PathKey = "path";
        private const string CreatedDateKey = "createdDate";
        private const string ExtensionKey = "ext";
        private readonly IMetaExtractorFabric metaExtractorFabric;
        private readonly IChecksumCreator checksumCreator;
        public ItemCreator(IChecksumCreator checksumCreator, IMetaExtractorFabric metaExtractorFabric)
        {
            this.metaExtractorFabric = metaExtractorFabric;
            this.checksumCreator = checksumCreator;
        }

        public async Task<Item> Create(string path, byte[] data)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var checksum = this.checksumCreator.Create(data);
            var extension = Path.GetExtension(path);
            
            var tags = Array.Empty<MetaData>();
            var metadata = new List<MetaData>
            {
                new MetaData(ExtensionKey, new MetaString(extension)),
                new MetaData(NameKey, new MetaString(name)),
                new MetaData(PathKey, new MetaString(path)),
                new MetaData(CreatedDateKey, new MetaDateTime(DateTimeOffset.UtcNow.UtcDateTime)),
            };

            var metaExtractors = metaExtractorFabric.Create(extension);
            foreach (var extractor in metaExtractors)
            {
                metadata.AddRange(await extractor.Extract(data));
            }
            return new Item(checksum, metadata.ToArray());
        }
    }

    public class MetaExtractorFabric : IMetaExtractorFabric
    {
        private readonly IEnumerable<IMetaExtractor> metaExtractors;

        public MetaExtractorFabric(IEnumerable<IMetaExtractor> metaExtractors)
        {
            this.metaExtractors = metaExtractors;
        }

        public IMetaExtractor[] Create(string extension) => this.metaExtractors
            .Where(x => x.FileFormat == extension switch
            {
                ".fb2" => SupportedFileFormats.fb2,
                ".pdf" => SupportedFileFormats.pdf,
                _ => SupportedFileFormats.noop
            }).ToArray();
    }

    public class NoMetaExtractor : IMetaExtractor
    {
        public SupportedFileFormats FileFormat => SupportedFileFormats.noop;

        public Task<MetaData[]> Extract(byte[] data) => Task.FromResult(Array.Empty<MetaData>());
    }

    public class Fb2MetaExtractor : IMetaExtractor
    {
        private readonly IFileRefIdCreator fileRefIdCreator;

        public Fb2MetaExtractor(IFileRefIdCreator fileRefIdCreator)
        {
            this.fileRefIdCreator = fileRefIdCreator;
        }

        public SupportedFileFormats FileFormat => SupportedFileFormats.fb2;

        public async Task<MetaData[]> Extract(byte[] data)
        {
            using var stream = new MemoryStream(data);
            var fb2file = await ReadFB2FileStreamAsync(stream);
            var image = fb2file.TitleInfo?.Cover?.CoverpageImages.FirstOrDefault()?.HRef;
            var titleInfo = fb2file.TitleInfo;
            if (titleInfo != null)
            {
                var result = new List<MetaData>();
                if (fb2file.Images.FirstOrDefault().Key == image?.Substring(1))
                {
                    var imageData = fb2file.Images.FirstOrDefault().Value.BinaryData;
                    var key = fb2file.Images.FirstOrDefault().Key;
                    result.Add(new MetaData(
                        "cover",
                        new MetaFile(
                            new ItemFileRef(fileRefIdCreator.Create()),
                            key,
                            imageData)));
                }
                // check for null/empty values
                var authors = titleInfo.BookAuthors.Select(x => x.ToString()).Aggregate((x, y) => x + ";" + y);
                if (authors is not null)
                {
                    result.Add(new MetaData(
                        "authors",
                        new MetaString(authors)));
                }
                if (titleInfo.BookDate is not null && titleInfo.BookDate.DateValue != default)
                {
                    result.Add(new MetaData(
                        "authors",
                        new MetaDateTime(titleInfo.BookDate.DateValue)));
                }
                if (!string.IsNullOrWhiteSpace(titleInfo.BookTitle?.Text))
                {
                    result.Add(new MetaData(
                        "title",
                        new MetaString(titleInfo.BookTitle.Text)));
                }
                if (titleInfo.Genres is not null)
                {
                    result.Add(new MetaData(
                        "genres",
                        new MetaString(titleInfo.Genres.Select(x => x.Genre).Aggregate((x, y) => x + ";" + y))));
                }
                if (!string.IsNullOrWhiteSpace(titleInfo.Keywords?.Text))
                {
                    result.Add(new MetaData(
                        "keywords",
                        new MetaString(titleInfo.Keywords.Text)));
                }
                if (!string.IsNullOrWhiteSpace(titleInfo.Language))
                {
                    result.Add(new MetaData(
                        "language",
                        new MetaString(titleInfo.Language)));
                }
                return result.ToArray();
            }
            
            return Array.Empty<MetaData>();
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

    public class PdfMetaExtractor : IMetaExtractor
    {
        private readonly IFileRefIdCreator fileRefIdCreator;

        public PdfMetaExtractor(IFileRefIdCreator fileRefIdCreator)
        {
            this.fileRefIdCreator = fileRefIdCreator;
        }

        public SupportedFileFormats FileFormat => SupportedFileFormats.pdf;

        public Task<MetaData[]> Extract(byte[] data)
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

                fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, (uint)Color.White.ToArgb());

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

                var bitmapImage = new Bitmap(
                    width,
                    height,
                    fpdfview.FPDFBitmapGetStride(bitmap),
                    PixelFormat.Format32bppArgb,
                    fpdfview.FPDFBitmapGetBuffer(bitmap));

                using var stream = new MemoryStream();
                bitmapImage.Save(stream, ImageFormat.Jpeg);

                var result = new MetaData(
                    "cover",
                    new MetaFile(
                        new ItemFileRef(fileRefIdCreator.Create()),
                        "cover.png",
                        stream.ToArray()));

                return Task.FromResult(new[] { result });
            }
            finally
            {
                Marshal.FreeHGlobal(unmanagedPointer);
            }
        }

    }

    public class FileRefIdCreator : IFileRefIdCreator
    {
        public string Create() => "$" + Guid.NewGuid().ToString("N");
    }
}


#endregion
