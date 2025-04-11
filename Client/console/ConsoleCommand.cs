using Spectre.Console;

namespace Client.Console
{
    public abstract class ConsoleCommand(
        string name,
        string description,
        string usage = "",
        string example = "",
        ConsoleCommand.AutocompleteDelegate autocompleteSuggestions = null!)
    {
        public string Name { get; } = name.ToLower();
        public string Description { get; } = description;
        public string Usage { get; } = usage;
        public string Example { get; } = example;

        public delegate Task<List<string>> AutocompleteDelegate(string[] args);

        public AutocompleteDelegate GetAutocompleteSuggestions { get; } = autocompleteSuggestions;

        public abstract Task ExecuteAsync(string[] args);

        protected static void WriteError(string message)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(message)}[/]");
        }

        protected static void WriteSuccess(string message)
        {
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");
        }

        protected static void WriteInfo(string message)
        {
            AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(message)}[/]");
        }

        protected static void WriteWarning(string message)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: {Markup.Escape(message)}[/]");
        }

        protected static async Task<bool> ConfirmActionAsync(string message = "Are you sure? (y/n)")
        {
            return await AnsiConsole.ConfirmAsync(message);
        }
    }
}