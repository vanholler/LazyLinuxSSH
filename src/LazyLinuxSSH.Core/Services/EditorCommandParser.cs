using System.Text;

namespace LazyLinuxSSH.Core.Services;

public static class EditorCommandParser
{
    private static readonly HashSet<string> EditorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nano", "vim", "vi", "nvim"
    };

    private static readonly HashSet<string> OptionsWithValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "-o", "--operatingdir", "-T", "--tabsize", "-r", "--fill", "-s", "--speller",
        "-c", "--cmd", "-u", "-U", "-S", "-w", "-W", "-i"
    };

    public static bool TryParse(string command, out string? filePath)
    {
        filePath = null;
        var tokens = Tokenize(command);
        if (tokens is null || tokens.Count == 0) return false;

        var executable = tokens[0].Replace('\\', '/').Split('/').LastOrDefault() ?? string.Empty;
        if (!EditorNames.Contains(executable)) return false;

        var files = new List<string>();
        var optionsEnded = false;
        for (var index = 1; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (!optionsEnded && token == "--")
            {
                optionsEnded = true;
                continue;
            }

            if (!optionsEnded && OptionsWithValues.Contains(token))
            {
                index++;
                continue;
            }

            if (!optionsEnded && token.StartsWith('-')) continue;
            files.Add(token);
        }

        if (files.Count == 1 && !string.IsNullOrWhiteSpace(files[0])) filePath = files[0];
        return true;
    }

    private static List<string>? Tokenize(string command)
    {
        var tokens = new List<string>();
        var token = new StringBuilder();
        var quote = '\0';
        var escaped = false;
        var tokenStarted = false;

        foreach (var character in command.Trim())
        {
            if (character is '\r' or '\n' || (quote == '\0' && character is '|' or ';' or '<' or '>' or '&' or '`'))
                return null;

            if (escaped)
            {
                token.Append(character);
                escaped = false;
                tokenStarted = true;
                continue;
            }

            if (character == '\\' && quote != '\'')
            {
                escaped = true;
                tokenStarted = true;
                continue;
            }

            if (character is '\'' or '"')
            {
                if (quote == '\0')
                {
                    quote = character;
                    tokenStarted = true;
                    continue;
                }

                if (quote == character)
                {
                    quote = '\0';
                    continue;
                }
            }

            if (char.IsWhiteSpace(character) && quote == '\0')
            {
                if (tokenStarted) tokens.Add(token.ToString());
                token.Clear();
                tokenStarted = false;
                continue;
            }

            token.Append(character);
            tokenStarted = true;
        }

        if (quote != '\0' || escaped) return null;
        if (tokenStarted) tokens.Add(token.ToString());
        return tokens;
    }
}
