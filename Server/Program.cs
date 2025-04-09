using Discord;
using Discord.WebSocket;
using LicenseSystem.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Console;
using Server.Discord;
using Server.Game;
using Server.Server;
using Server.Utils;

namespace Server
{
    public abstract class Program
    {
        private static IServiceProvider _services;

        public static async Task Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .Build();
            
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection, configuration);
            _services = serviceCollection.BuildServiceProvider();
            
            var logger = _services.GetRequiredService<Logger>();
            logger.Info("ChunkyBotServer wird gestartet...");
            
            // Initialisiere und starte den LicenseServer
            var licenseServer = _services.GetRequiredService<LicenseServer>();
            licenseServer.Start();
            logger.Info("LicenseServer gestartet.");
            
            // Registriere und starte den ConsoleCommandHandler
            var commandHandler = _services.GetRequiredService<ConsoleCommandHandler>();
            CommandRegistration.RegisterConsoleCommands(_services, commandHandler);
            
            // Starte den Discord-Bot
            var botHandler = _services.GetRequiredService<BotHandler>();
            await botHandler.StartAsync();
            logger.Info("Discord-Bot gestartet.");
            
            // Starte die Konsolen-Befehlsschleife
            logger.Info("Starte Console-Befehlsschleife...");
            await commandHandler.StartAsync();
            
            // Wenn der Befehlshandler beendet wird, fahre alles herunter
            logger.Info("Server wird beendet...");
            
            await botHandler.StopAsync();
            licenseServer.Stop();
            
            logger.Info("Server-Shutdown abgeschlossen. Auf Wiedersehen!");
        }

        private static void ConfigureServices(ServiceCollection services, IConfiguration configuration)
        {
            // Konfiguration
            services.AddSingleton(configuration);
            
            // Logging
            services.AddSingleton<Logger>();
            
            // Memory Cache für die GameInfoService
            services.AddMemoryCache();
            
            // Discord Services
            services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
                AlwaysDownloadUsers = true,
                MessageCacheSize = 100
            }));
            services.AddSingleton<BotHandler>();
            services.AddSingleton<CommandHandler>();
            services.AddSingleton<PresenceManager>();
            
            // Game Services
            services.AddSingleton<GameInfoService>();
            services.AddSingleton<ChunkManager>();
            
            // License Services
            services.AddSingleton<LicenseServer>(provider =>
            {
                var logger = provider.GetRequiredService<Logger>();
                var port = configuration.GetValue<int>("Server:Port", 25599);
                var usersFilePath = configuration.GetValue<string>("Server:UsersFilePath", "users.json");

                var server = new LicenseServer(port, usersFilePath);
                
                server.ClientConnected += (sender, args) =>
                    logger.Info($"Client verbunden: {args.Username} (ID: {args.ClientId})");

                server.ClientDisconnected += (sender, args) =>
                    logger.Info($"Client getrennt: {args.Username} (ID: {args.ClientId})");

                return server;
            });
            
            services.AddSingleton<ServerHandler>();
            
            // Console Command Handler
            services.AddSingleton<ConsoleCommandHandler>();
        }
    }
}