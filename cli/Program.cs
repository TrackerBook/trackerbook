using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Text;
using tb_lib.app;
using tb_lib.domain;
using tb_lib.infr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace tb_cli
{
    class Program
    {
        static int Main(string[] args)
        {
            using var serviceProvider = new ServiceCollection()
                .AddLogging(x =>x.AddSimpleConsole())
                .AddSingleton<IBCollection, BCollection>()
                .AddSingleton<IStorage, Storage>()
                .AddSingleton<IFileStorage, FileStorage>()
                .AddSingleton<IChecksumCreator, ChecksumCreator>()
                .AddSingleton<ICoverExtractorFabric, CoverExtractorFabric>()
                .AddSingleton<IBookCreator, BookCreator>()
                .AddSingleton<IFileRefIdCreator, FileRefIdCreator>()
                .AddSingleton<ICoverExtractor, Fb2CoverExtractor>()
                .AddSingleton<ICoverExtractor, EpubCoverExtractor>()
                .AddSingleton<ICoverExtractor, DefaultCoverExtractor>()
                .AddSingleton<ICoverExtractor, PdfCoverExtractor>()
                .BuildServiceProvider();
            Console.OutputEncoding = Encoding.UTF8;

            return new RootCommand("Book Collection CLI")
            {
                CreateAddCommand(serviceProvider),
                CreateListCommand(serviceProvider),
                CreateDeleteCommand(serviceProvider),
                CreateDeleteAllCommand(serviceProvider)
            }.InvokeAsync(args).Result;
        }

        private static Command CreateDeleteCommand(ServiceProvider provider)
        {
            var deleteCommand = new Command(
                                "delete",
                                "Delete item by checksum");
            deleteCommand.AddArgument(new Argument<string>("checksum"));
            deleteCommand.Handler = CommandHandler.Create<string>((checksum) =>
            {
                var bCollection = provider.GetService<IBCollection>()!;
                var item = bCollection.GetItems().SingleOrDefault(x => x.Id == checksum);
                if (item is null) return;
                var result = bCollection.UpdateItem(item with { Deleted = true });
                var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
                if (result is Updated)
                {
                    logger.LogInformation("Deleted item.");
                }
                else
                {
                    logger.LogInformation("Can't delete item.");
                    logger.LogInformation(result.ToString());
                }
            });
            return deleteCommand;
        }

        private static Command CreateDeleteAllCommand(ServiceProvider provider)
        {
            var deleteCommand = new Command(
                                "delete-all",
                                "Delete all items");
            deleteCommand.Handler = CommandHandler.Create<string>((checksum) =>
            {
                var bCollection = provider.GetService<IBCollection>()!;
                var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
                foreach (var item in bCollection.GetItems())
                {
                    var result = bCollection.UpdateItem(item with { Deleted = true });
                    if (result is Updated)
                    {
                        logger.LogInformation("Deleted item.");
                    }
                    else
                    {
                        logger.LogInformation("Can't delete item.");
                        logger.LogInformation(result.ToString());
                    }
                }
            });
            return deleteCommand;
        }

        private static Command CreateListCommand(ServiceProvider provider)
        {
            var listCommand = new Command(
                                "list",
                                "List files in the collection.");
            listCommand.Handler = CommandHandler.Create(() =>
            {
                var bCollection = provider.GetService<IBCollection>()!;
                var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
                foreach (var item in bCollection.GetItems())
                {
                    logger.LogInformation(item.Id.ToString());
                }
            });
            return listCommand;
        }

        private static Command CreateAddCommand(ServiceProvider provider)
        {
            var addCommand = new Command("add", "Add a file to the collection.");
            addCommand.AddArgument(new Argument<FileInfo>("fileInfo"));
            addCommand.Handler = CommandHandler.Create<FileInfo>(async (fileInfo) =>
            {
                var bCollection = provider.GetRequiredService<IBCollection>();
                var itemCreator = provider.GetRequiredService<IBookCreator>();
                var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
                var item = await itemCreator.Create(fileInfo.FullName, File.ReadAllBytes(fileInfo.FullName));
                var result = bCollection.AddItem(item);
                switch (result)
                {
                    case Added addedResult:
                        logger.LogInformation($"Added: {addedResult.item.Id ?? "null"}");
                        break;
                    case AlreadyExists alreadyExistsResult:
                        logger.LogInformation($"Already exists: {alreadyExistsResult.item.Id ?? "null"}");
                        break;
                }
            });
            return addCommand;
        }
    }
}
