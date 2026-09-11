#nullable enable
using System.Text;

namespace Eniris.Examples.Models;

public sealed class ConsoleMenu
{
    private readonly IReadOnlyList<(string Key, string Description)> _items;

    public ConsoleMenu(string title, IReadOnlyList<(string Key, string Description)> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(items);

        Title = title;
        _items = items;
    }

    public string Title { get; }

    public string PromptSelection()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine(Title);
            foreach (var (key, description) in _items)
            {
                Console.WriteLine($"  {key}. {description}");
            }

            Console.Write("Select an option: ");
            var selection = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(selection) && _items.Any(item => string.Equals(item.Key, selection, StringComparison.OrdinalIgnoreCase)))
            {
                return selection;
            }

            Console.WriteLine("Please enter one of the listed menu options.");
        }
    }

    public static string PromptRequired(string label, string? defaultValue = null, bool secret = false)
    {
        while (true)
        {
            var value = secret
                ? PromptSecret(label, defaultValue)
                : Prompt(label, defaultValue);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            Console.WriteLine($"{label} is required.");
        }
    }

    public static string Prompt(string label, string? defaultValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var suffix = string.IsNullOrWhiteSpace(defaultValue) ? string.Empty : $" [{defaultValue}]";
        Console.Write($"{label}{suffix}: ");
        var value = Console.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? defaultValue ?? string.Empty : value.Trim();
    }

    public static string PromptSecret(string label, string? defaultValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var suffix = string.IsNullOrWhiteSpace(defaultValue) ? string.Empty : " [press Enter to reuse configured value]";
        Console.Write($"{label}{suffix}: ");

        if (Console.IsInputRedirected)
        {
            var redirected = Console.ReadLine();
            return string.IsNullOrWhiteSpace(redirected) ? defaultValue ?? string.Empty : redirected.Trim();
        }

        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.Length == 0 ? defaultValue ?? string.Empty : buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }
}
