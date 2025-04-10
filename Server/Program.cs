using Discord;
using Discord.WebSocket;
using LicenseSystem.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Console;
using Server.Discord;
using Server.Game;
using Server.Server;
using Server.Utils;
using LogLevel = Server.Utils.LogLevel;

namespace Server
{
    public abstract class Program
    {
        private static IServiceProvider _services;
        
        public static T? GetService<T>() => _services.GetService<T>();

        public static async Task Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .Build();


            var logLevelStr = configuration.GetValue<string>("Logging:MinimumLevel", "Info");
            System.Console.WriteLine($"DIAGNOSTIC: Configured log level from appsettings.json: {logLevelStr}");

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection, configuration);
            _services = serviceCollection.BuildServiceProvider();

            var logger = _services.GetRequiredService<Logger>();
            logger.Info("Starting Chunky...");


            var licenseServer = _services.GetRequiredService<LicenseServer>();
            licenseServer.Start();
            logger.Info("LicenseServer started.");


            var commandHandler = _services.GetRequiredService<ConsoleCommandHandler>();
            CommandRegistration.RegisterConsoleCommands(_services, commandHandler);


            var botHandler = _services.GetRequiredService<BotHandler>();
            await botHandler.StartAsync();
            logger.Info("Discord bot started.");
            
            _services.GetRequiredService<MessageHandler>();


            logger.Info("Starting console command loop...");
            await commandHandler.StartAsync();


            logger.Info("Server is shutting down...");

            await botHandler.StopAsync();
            licenseServer.Stop();

            logger.Info("Server shutdown completed. Goodbye!");
        }

        private static void ConfigureServices(ServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton(configuration);


            var logLevelStr = configuration.GetValue<string>("Logging:MinimumLevel", "Info");
            var logLevel = Enum.TryParse<LogLevel>(logLevelStr, true, out var level) ? level : LogLevel.Info;


            var licenseSystemLogLevel = (LicenseSystem.Services.LogLevel)logLevel;

            System.Console.WriteLine(
                $"DIAGNOSTIC: Using LogLevel.{logLevel} for Server and LogLevel.{licenseSystemLogLevel} for LicenseSystem");


            services.AddSingleton<Logger>();


            services.AddMemoryCache();


            services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
                AlwaysDownloadUsers = true,
                MessageCacheSize = 100
            }));
            services.AddSingleton<BotHandler>();
            services.AddSingleton<CommandHandler>();
            services.AddSingleton<PresenceManager>();


            services.AddSingleton<GameInfoService>();
            services.AddSingleton<ChunkManager>();


            services.AddSingleton<LicenseServer>(provider =>
            {
                var logger = provider.GetRequiredService<Logger>();
                var port = configuration.GetValue<int>("Server:Port", 25599);
                var usersFilePath = configuration.GetValue<string>("Server:UsersFilePath", "users.json");

                System.Console.WriteLine($"DIAGNOSTIC: Creating LicenseServer with LogLevel.{licenseSystemLogLevel}");

                var server = new LicenseServer(port, usersFilePath, licenseSystemLogLevel);

                server.ClientConnected += (sender, args) =>
                    logger.Info($"Client connected: {args.Username} (ID: {args.ClientId})");

                server.ClientDisconnected += (sender, args) =>
                    logger.Info($"Client disconnected: {args.Username} (ID: {args.ClientId})");

                return server;
            });

            services.AddSingleton<ServerHandler>();


            services.AddSingleton<ConsoleCommandHandler>();
            
            services.AddSingleton<MessageHandler>();
        }
    }
}