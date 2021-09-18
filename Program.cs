﻿using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Text;
using bcollection.app;
using bcollection.domain;
using bcollection.infr;

namespace bcollection
{
    class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            return new RootCommand("Book Collection CLI")
            {
                CreateAddCommand(),
                CreateListCommand(),
                CreateDeleteCommand(),
                CreateFindByChecksumCommand(),
                CreateDeleteAllCommand()
            }.InvokeAsync(args).Result;
        }

        private static Command CreateFindByChecksumCommand()
        {
            var findCommand = new Command(
                                "find",
                                "Find item by checksum prefix");
            findCommand.AddArgument(new Argument<string>("prefix"));
            findCommand.Handler = CommandHandler.Create<string>((prefix) =>
            {
                var bCollection = CreateBCollection();
                var result = bCollection.Find(prefix);
                foreach (var item in result)
                {
                    Console.WriteLine(item.checksum);
                    foreach (var meta in item.metadata)
                    {
                        Console.WriteLine(meta);
                    }
                }
            });
            return findCommand;
        }

        private static BCollection CreateBCollection()
        {
            var storage = new Storage();
            var fileStorage = new FileStorage();
            var bCollection = new BCollection(storage, fileStorage);
            return bCollection;
        }

        private static Command CreateDeleteCommand()
        {
            var deleteCommand = new Command(
                                "delete",
                                "Delete item by checksum");
            deleteCommand.AddArgument(new Argument<string>("checksum"));
            deleteCommand.Handler = CommandHandler.Create<string>((checksum) =>
            {
                var bCollection = CreateBCollection();
                var result = bCollection.DeleteItem(checksum);
                if (result is Deleted)
                {
                    Console.WriteLine("Deleted item.");
                }
                else
                {
                    Console.WriteLine("Can't delete item.");
                    Console.WriteLine(result);
                }
            });
            return deleteCommand;
        }

        private static Command CreateDeleteAllCommand()
        {
            var deleteCommand = new Command(
                                "delete-all",
                                "Delete all items");
            deleteCommand.Handler = CommandHandler.Create<string>((checksum) =>
            {
                var bCollection = CreateBCollection();
                foreach (var item in bCollection.GetItems())
                {
                    var result = bCollection.DeleteItem(item.checksum.value);
                    if (result is Deleted)
                    {
                        Console.WriteLine("Deleted item.");
                    }
                    else
                    {
                        Console.WriteLine("Can't delete item.");
                        Console.WriteLine(result);
                    }
                }
            });
            return deleteCommand;
        }

        private static Command CreateListCommand()
        {
            var listCommand = new Command(
                                "list",
                                "List files in the collection.");
            listCommand.Handler = CommandHandler.Create(() =>
            {
                var bCollection = CreateBCollection();
                foreach (var item in bCollection.GetItems())
                {
                    Console.WriteLine(item.checksum);
                }
            });
            return listCommand;
        }

        private static Command CreateAddCommand()
        {
            var addCommand = new Command("add", "Add a file to the collection.");
            addCommand.AddArgument(new Argument<FileInfo>("fileInfo"));
            addCommand.Handler = CommandHandler.Create<FileInfo>(async (fileInfo) =>
            {
                var bCollection = CreateBCollection();
                var checksumCreator = new ChecksumCreator();
                var metaExtractorFabric = new MetaExtractorFabric();
                var itemCreator = new ItemCreator(checksumCreator, metaExtractorFabric);
                var item = await itemCreator.Create(fileInfo.FullName, File.ReadAllBytes(fileInfo.FullName));
                var result = bCollection.AddItem(item);
                switch (result)
                {
                    case Added addedResult:
                        Console.WriteLine($"Added: {addedResult.checksum ?? "null"}");
                        break;
                    case AlreadyExists alreadyExistsResult:
                        Console.WriteLine($"Already exists: {alreadyExistsResult.checksum ?? "null"}");
                        break;
                }
            });
            return addCommand;
        }
    }
}
