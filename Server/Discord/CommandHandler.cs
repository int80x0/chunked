using System;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Server.Commands;
using Server.Utils;

namespace Server.Discord
{
    public class CommandHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly InteractionService _interactionService;
        private readonly IServiceProvider _services;
        private readonly IConfiguration _config;
        private readonly Logger _logger;
        
        private readonly bool _registerGlobally;
        private readonly ulong _testGuildId;
        
        public CommandHandler(
            DiscordSocketClient client,
            IServiceProvider services,
            IConfiguration config,
            Logger logger)
        {
            _client = client;
            _services = services;
            _config = config;
            _logger = logger;
            
            _interactionService = new InteractionService(client);
            
            _registerGlobally = _config.GetValue("Discord:RegisterCommandsGlobally", false);
            _testGuildId = _config.GetValue<ulong>("Discord:TestGuildId", 0);
            
            _client.InteractionCreated += HandleInteractionAsync;
            _client.Ready += OnReadyAsync;
        }
        
        public async Task InitializeAsync()
        {
            _logger.Info("CommandHandler wird initialisiert...");
            
            try
            {
                await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
                
                _logger.Info($"{_interactionService.Modules.Count} Command-Module geladen.");
                
                foreach (var module in _interactionService.Modules)
                {
                    _logger.Debug($"Modul geladen: {module.Name} mit {module.SlashCommands.Count} Slash-Commands");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Initialisieren des CommandHandlers: {ex.Message}");
            }
        }
        
        private async Task OnReadyAsync()
        {
            try
            {
                if (_registerGlobally)
                {
                    await _interactionService.RegisterCommandsGloballyAsync();
                    _logger.Info("Slash-Commands global registriert.");
                }
                else if (_testGuildId != 0)
                {
                    await _interactionService.RegisterCommandsToGuildAsync(_testGuildId);
                    _logger.Info($"Slash-Commands für Test-Server {_testGuildId} registriert.");
                }
                else
                {
                    _logger.Warning("Weder global noch für einen Test-Server wurden Commands registriert.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler bei der Registrierung von Slash-Commands: {ex.Message}");
            }
        }
        
        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            try
            {
                var context = new SocketInteractionContext(_client, interaction);
                
                var result = await _interactionService.ExecuteCommandAsync(context, _services);
                
                if (!result.IsSuccess)
                {
                    switch (result.Error)
                    {
                        case InteractionCommandError.UnknownCommand:
                            _logger.Warning($"Unbekannter Command: {interaction.Data}");
                            break;
                        case InteractionCommandError.BadArgs:
                            await interaction.RespondAsync("Falsche Argumente für den Command.", ephemeral: true);
                            break;
                        case InteractionCommandError.Exception:
                            _logger.Error($"Command-Ausnahme: {result.ErrorReason}");
                            await interaction.RespondAsync("Bei der Ausführung des Commands ist ein Fehler aufgetreten.", ephemeral: true);
                            break;
                        case InteractionCommandError.Unsuccessful:
                            await interaction.RespondAsync("Der Command konnte nicht erfolgreich ausgeführt werden.", ephemeral: true);
                            break;
                        case InteractionCommandError.ConvertFailed:
                        case InteractionCommandError.UnmetPrecondition:
                        case InteractionCommandError.ParseFailed:
                        case null:
                            break;
                        default:
                            _logger.Error($"Unbehandelter Fehler: {result.Error}: {result.ErrorReason}");
                            await interaction.RespondAsync("Ein unbekannter Fehler ist aufgetreten.", ephemeral: true);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler bei der Verarbeitung einer Interaktion: {ex.Message}");
                
                if (!interaction.HasResponded)
                {
                    await interaction.RespondAsync("Bei der Verarbeitung ist ein unerwarteter Fehler aufgetreten.", ephemeral: true);
                }
            }
        }
    }
}