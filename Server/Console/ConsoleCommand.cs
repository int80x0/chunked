using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Server.Console
{
    public abstract class ConsoleCommand
    {
        public string Name { get; }
        public string Description { get; }
        public string Usage { get; }
        public string Example { get; }
        
        public delegate Task<List<string>> AutocompleteDelegate(string[] args);
        public AutocompleteDelegate GetAutocompleteSuggestions { get; }

        protected ConsoleCommand(string name, string description, string usage = "", string example = "", AutocompleteDelegate autocompleteSuggestions = null)
        {
            Name = name.ToLower();
            Description = description;
            Usage = usage;
            Example = example;
            GetAutocompleteSuggestions = autocompleteSuggestions;
        }

        public abstract Task ExecuteAsync(string[] args);

        protected void WriteError(string message)
        {
            var originalColor = System.Console.ForegroundColor;
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"Fehler: {message}");
            System.Console.ForegroundColor = originalColor;
        }

        protected void WriteSuccess(string message)
        {
            var originalColor = System.Console.ForegroundColor;
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine(message);
            System.Console.ForegroundColor = originalColor;
        }

        protected void WriteInfo(string message)
        {
            var originalColor = System.Console.ForegroundColor;
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine(message);
            System.Console.ForegroundColor = originalColor;
        }

        protected void WriteWarning(string message)
        {
            var originalColor = System.Console.ForegroundColor;
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"Warnung: {message}");
            System.Console.ForegroundColor = originalColor;
        }

        protected async Task<bool> ConfirmActionAsync(string message = "Bist du sicher? (j/n)")
        {
            System.Console.Write(message + " ");
            string input = await Task.Run(() => System.Console.ReadLine());
            return input?.ToLower() == "j" || input?.ToLower() == "ja" || input?.ToLower() == "y" || input?.ToLower() == "yes";
        }
    }
}