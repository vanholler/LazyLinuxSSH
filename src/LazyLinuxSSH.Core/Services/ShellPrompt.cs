using System.Text.RegularExpressions;

namespace LazyLinuxSSH.Core.Services;

/// <summary>
/// Reads the command readline is actually showing. Tab-completion and history
/// recall rewrite the PTY line, so the raw keystroke buffer (for example
/// <c>nano ./do</c>) must not win over <c>nano ./docker-compose.yml</c>.
/// </summary>
public static class ShellPrompt
{
    private static readonly Regex InputLine = new(
        @"^(?: \([^)\r]{1,80}\)\s* )?
          (?:
            [\w.+-]{1,80}@[\w.:-]{1,128}:(?<cwd>[^\s#$>%]*)
            | [\w.+-]{1,80}@[\w.:-]{1,128}\s+(?<cwd>~/[^\s#$>%]*|/[^\s#$>%]+)
            | (?<cwd>~/[^\s#$>%]*|/[^\s#$>%]+)
          )
          \s*[#$>%]\s+(?<command>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace);

    public static bool TryReadInput(string? line, out string? directory, out string command)
    {
        directory = null;
        command = string.Empty;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var match = InputLine.Match(line.TrimEnd());
        if (!match.Success) return false;

        command = match.Groups["command"].Value.Trim();
        if (command.Length == 0) return false;

        var cwd = match.Groups["cwd"].Value;
        if (cwd is "/" or "~")
            directory = cwd;
        else if (cwd.StartsWith("~/") || (cwd.StartsWith('/') && cwd.Length > 1))
            directory = cwd.TrimEnd('/');
        return true;
    }
}
