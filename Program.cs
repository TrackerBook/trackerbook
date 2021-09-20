using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Text;
using bcollection.app;
using bcollection.domain;
using bcollection.infr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace bcollection
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
                .AddSingleton<IMetaExtractorFabric, MetaExtractorFabric>()
                .AddSingleton<IItemCreator, ItemCreator>()
                .BuildServiceProvider();
            Console.OutputEncoding = Encoding.UTF8;

            return new RootCommand("Book Collection CLI")
            {
                CreateAddCommand(serviceProvider),
                CreateListCommand(serviceProvider),
                CreateDeleteCommand(serviceProvider),
                CreateFindByChecksumCommand(serviceProvider),
                CreateDeleteAllCommand(serviceProvider)
            }.InvokeAsync(args).Result;
        }

        private static Command CreateFindByChecksumCommand(ServiceProvider provider)
        {
            var findCommand = new Command(
                                "find",
                                "Find item by checksum prefix");
            findCommand.AddArgument(new Argument<string>("prefix"));
            findCommand.Handler = CommandHandler.Create<string>((prefix) =>
            {
                var bCollection = provider.GetService<IBCollection>()!;
                var result = bCollection.Find(prefix);
                var logger = provider.GetService<ILoggerFactory>()!.CreateLogger<Program>();
                foreach (var item in result)
                {
                    logger.LogInformation(item.checksum.ToString());
                    foreach (var meta in item.metadata)
                    {
                        logger.LogInformation(meta.ToString());
                    }
                }
            });
            return findCommand;
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
                var result = bCollection.DeleteItem(checksum);
                var logger = provider.GetService<ILoggerFactory>()!.CreateLogger<Program>();
                if (result is Deleted)
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
                var logger = provider.GetService<ILoggerFactory>()!.CreateLogger<Program>();
                foreach (var item in bCollection.GetItems())
                {
                    var result = bCollection.DeleteItem(item.checksum.value);
                    if (result is Deleted)
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
                var logger = provider.GetService<ILoggerFactory>()!.CreateLogger<Program>();
                foreach (var item in bCollection.GetItems())
                {
                    logger.LogInformation(item.checksum.ToString());
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
                var bCollection = provider.GetService<IBCollection>()!;
                var itemCreator = provider.GetService<IItemCreator>()!;
                var logger = provider.GetService<ILoggerFactory>()!.CreateLogger<Program>();
                var item = await itemCreator.Create(fileInfo.FullName, File.ReadAllBytes(fileInfo.FullName));
                var result = bCollection.AddItem(item);
                switch (result)
                {
                    case Added addedResult:
                        logger.LogInformation($"Added: {addedResult.checksum ?? "null"}");
                        break;
                    case AlreadyExists alreadyExistsResult:
                        logger.LogInformation($"Already exists: {alreadyExistsResult.checksum ?? "null"}");
                        break;
                }
            });
            return addCommand;
        }
    }
}
