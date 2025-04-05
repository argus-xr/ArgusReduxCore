// ArgusCoreService.cs
using ArgusReduxCore.NetworkUDP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArgusReduxCore
{
    public static class ArgusCoreService
    {
        public static IServiceProvider CreateDefaultSetup(Action<IServiceCollection>? configureServices = null)
        {
            // Create a service collection
            var services = new ServiceCollection();

            // Configure services
            ConfigureCoreServices(services);

            // Allow the caller to configure additional services
            configureServices?.Invoke(services);

            // Check if logging is already configured
            if (!services.Any(descriptor => descriptor.ServiceType == typeof(ILoggerFactory)))
            {
                // Add default console logging
                services.AddLogging(loggingBuilder =>
                {
                    loggingBuilder.AddConsole();
                    loggingBuilder.SetMinimumLevel(LogLevel.Debug);
                });
            }

            // Build the service provider
            return services.BuildServiceProvider();
        }

        private static void ConfigureCoreServices(IServiceCollection services)
        {
            // Add services to the collection
            services.AddSingleton<IUDPNetworkService>(provider =>
            {
                // Resolve the logger from the provider, if available.
                var logger = provider.GetService<ILogger<UDPNetworkService>>();
                // Create the NetworkMessageReceiver with the resolved dependency
                return new UDPNetworkService(logger);
            });
            services.AddSingleton<TrackerManager>();
        }
    }
}
