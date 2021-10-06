using System;
using bc_ui.ViewModels;
using bcollection.app;
using bcollection.infr;
using Microsoft.Extensions.Logging;
using Splat;

namespace bc_ui
{
    public static class Bootstrapper
    {
        public static void Register(IMutableDependencyResolver services, IReadonlyDependencyResolver resolver)
        {
            services.RegisterLazySingleton<IStorage>(() => new Storage());
            services.RegisterLazySingleton<IFileStorage>(() => new FileStorage());
            services.RegisterLazySingleton<IChecksumCreator>(() => new ChecksumCreator());
            services.RegisterLazySingleton<IMetaExtractorFabric>(() => new MetaExtractorFabric(
                resolver.GetServices<IMetaExtractor>()
            ));
            services.RegisterLazySingleton<IItemCreator>(() => new ItemCreator(
                resolver.GetRequiredService<IChecksumCreator>(),
                resolver.GetRequiredService<IMetaExtractorFabric>()
            ));
            services.RegisterLazySingleton<IFileRefIdCreator>(() => new FileRefIdCreator());
            services.RegisterLazySingleton<IMetaExtractor>(() => new Fb2MetaExtractor(
                resolver.GetRequiredService<IFileRefIdCreator>()
            ));
            services.RegisterLazySingleton<IMetaExtractor>(() => new PdfMetaExtractor(
                resolver.GetRequiredService<IFileRefIdCreator>()
            ));
            services.RegisterLazySingleton<IBCollection>(() => new BCollection(
                new LoggerFactory(),
                resolver.GetRequiredService<IStorage>(),
                resolver.GetRequiredService<IFileStorage>()
            ));
            services.Register<MainWindowViewModel>(() => new MainWindowViewModel(
                resolver.GetRequiredService<IBCollection>(),
                resolver.GetRequiredService<IItemCreator>()
            ));
        }

        public static TService GetRequiredService<TService>(this IReadonlyDependencyResolver resolver)
        {
            var service = resolver.GetService<TService>();
            if (service is null) // Splat is not able to resolve type for us
            {
                throw new InvalidOperationException($"Failed to resolve object of type {typeof(TService)}"); // throw error with detailed description
            }

            return service; // return instance if not null
        }
    }
}
