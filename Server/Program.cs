using Discord;
using Discord.WebSocket;
using LicenseSystem.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Discord;
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
            
            var licenseServer = _services.GetRequiredService<LicenseServer>();
            licenseServer.Start();
            logger.Info("LicenseServer gestartet.");
            
            var botHandler = _services.GetRequiredService<BotHandler>();
            await botHandler.StartAsync();
            logger.Info("Discord-Bot gestartet.");
            
            await Task.Delay(-1);
        }

        private static void ConfigureServices(ServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton(configuration);
            
            services.AddSingleton<Logger>();
            
            services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
                AlwaysDownloadUsers = true,
                MessageCacheSize = 100
            }));
            services.AddSingleton<BotHandler>();
            services.AddSingleton<CommandHandler>();
            services.AddSingleton<PresenceManager>();
            
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
            
        }
    }
}