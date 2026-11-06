namespace LazyLinuxSSH.Core.Services;

/// <summary>
/// Tracks the interactive shell working directory from simple <c>cd</c> commands so
/// relative editor paths like <c>nano .env</c> resolve against the current folder,
/// not only the login home directory.
/// </summary>
public static class ShellWorkingDirectory
{
    public static string? Apply(string? current, string command)
    {
        if (!TryGetCdTarget(command, out var target)) return current;

        if (string.IsNullOrEmpty(target) || target == "~")
            return "~";

        if (target.StartsWith("~/") || target.StartsWith("~\\"))
            return "~/" + target[2..].Replace('\\', '/').TrimStart('/');

        if (target.StartsWith('/') || IsWindowsRooted(target))
            return Normalize(target);

        return ExpandAgainst(current ?? "~", target);
    }

    public static string ResolveAgainstHome(string? workingDirectory, string homeDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Укажите путь к файлу.", nameof(path));
        homeDirectory = Normalize(homeDirectory);
        if (path == "~") return homeDirectory;
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
            return CombineUnix(homeDirectory, path[2..]);
        if (path.StartsWith('/')) return Normalize(path);

        var baseDirectory = ExpandHome(workingDirectory, homeDirectory);
        return CombineUnix(baseDirectory, path);
    }

    public static string CombineUnix(string basePath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath is "." or "./")
            return Normalize(basePath);
        if (relativePath.StartsWith('/')) return Normalize(relativePath);
        return Normalize($"{basePath.TrimEnd('/')}/{relativePath.TrimStart('/', '\\')}");
    }

    private static string ExpandAgainst(string current, string relativeTarget)
    {
        if (current is "~" or "")
            return "~/" + relativeTarget.TrimStart('/', '\\');

        if (current.StartsWith("~/") || current.StartsWith("~\\"))
        {
            var combined = CombineUnix("/" + current[2..], relativeTarget).TrimStart('/');
            return string.IsNullOrEmpty(combined) ? "~" : "~/" + combined;
        }

        return CombineUnix(current, relativeTarget);
    }

    private static string ExpandHome(string? workingDirectory, string homeDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || workingDirectory == "~")
            return homeDirectory;
        if (workingDirectory.StartsWith("~/") || workingDirectory.StartsWith("~\\"))
            return CombineUnix(homeDirectory, workingDirectory[2..]);
        if (!workingDirectory.StartsWith('/') && !IsWindowsRooted(workingDirectory))
            return CombineUnix(homeDirectory, workingDirectory);
        return Normalize(workingDirectory);
    }

    private static bool TryGetCdTarget(string command, out string? target)
    {
        target = null;
        var trimmed = command.Trim();
        if (trimmed.Length < 2 || trimmed[0] is not ('c' or 'C') || trimmed[1] is not ('d' or 'D'))
            return false;

        if (trimmed.Length == 2)
        {
            target = string.Empty;
            return true;
        }

        if (!char.IsWhiteSpace(trimmed[2])) return false;

        var argument = trimmed[2..].Trim();
        if (argument.Length == 0)
        {
            target = string.Empty;
            return true;
        }

        if (argument.IndexOfAny(['|', ';', '&', '>', '<', '`', '\n', '\r']) >= 0)
            return false;

        target = Unquote(FirstToken(argument));
        return target != "-";
    }

    private static string FirstToken(string argument)
    {
        if (argument[0] is '\'' or '"')
        {
            var quote = argument[0];
            var end = argument.IndexOf(quote, 1);
            return end > 0 ? argument[1..end] : argument.Trim(quote);
        }

        var index = 0;
        while (index < argument.Length && !char.IsWhiteSpace(argument[index])) index++;
        return argument[..index];
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }

    private static string Normalize(string path)
    {
        // Preserve a leading ~/ until ExpandHome replaces it with the real home path.
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var rest = Normalize("/" + path[2..]).TrimStart('/');
            return string.IsNullOrEmpty(rest) ? "~" : "~/" + rest;
        }

        if (path == "~") return "~";

        var isAbsolute = path.StartsWith('/');
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        if (isAbsolute) return "/" + string.Join('/', parts);
        return parts.Count == 0 ? "." : string.Join('/', parts);
    }

    private static bool IsWindowsRooted(string path) =>
        path.Length >= 3 &&
        char.IsLetter(path[0]) &&
        path[1] == ':' &&
        (path[2] == '\\' || path[2] == '/');
}
