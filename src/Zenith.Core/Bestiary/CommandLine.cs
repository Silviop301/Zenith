namespace Zenith.Core.Bestiary;

/// <summary>
/// What a startup entry runs. <see cref="Payload"/> is the file a host program is told to run
/// (the script for wscript, the DLL for rundll32), when there is one.
/// </summary>
public sealed record ParsedCommand(string Executable, string? Payload)
{
    /// <summary>The file that really decides what happens: the payload for a host, otherwise the executable.</summary>
    public string Subject => Payload ?? Executable;
}

/// <summary>Finds the program in a Run value or shortcut command line, the way Windows does for the usual forms.</summary>
public static class CommandLine
{
    /// <summary>Programs that only run something else; the interesting file is in their arguments.</summary>
    public static readonly IReadOnlySet<string> Hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "rundll32.exe", "wscript.exe", "cscript.exe", "mshta.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "regsvr32.exe", "conhost.exe",
    };

    /// <summary>Hosts that run scripts. A script starting with Windows is unusual for a normal program.</summary>
    public static readonly IReadOnlySet<string> ScriptHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "wscript.exe", "cscript.exe", "mshta.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
    };

    private static readonly string[] PayloadExtensions =
        [".exe", ".dll", ".js", ".jse", ".vbs", ".vbe", ".wsf", ".hta", ".ps1", ".bat", ".cmd", ".scr"];

    public static ParsedCommand? Parse(string? commandLine, Func<string, string>? expand = null)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        expand ??= Environment.ExpandEnvironmentVariables;
        var line = expand(commandLine).Trim();

        var (exe, rest) = SplitExecutable(line);
        if (exe.Length == 0) return null;

        string? payload = null;
        if (Hosts.Contains(FileName(exe)))
            payload = Tokens(rest).FirstOrDefault(t => PayloadExtensions.Any(x => t.EndsWith(x, StringComparison.OrdinalIgnoreCase)));
        return new ParsedCommand(exe, payload);
    }

    /// <summary>Splits off the program: a quoted path, an unquoted path up to ".exe", or the first word.</summary>
    private static (string Exe, string Arguments) SplitExecutable(string line)
    {
        if (line.StartsWith('"'))
        {
            var close = line.IndexOf('"', 1);
            return close < 0 ? (line[1..], "") : (line[1..close], line[(close + 1)..]);
        }

        // Unquoted paths with spaces ("C:\Program Files\App\app.exe -min") are common in Run values.
        var at = 0;
        while ((at = line.IndexOf(".exe", at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = at + 4;
            if (end == line.Length || line[end] is ' ' or '\t' or '"' or ',')
                return (line[..end], line[end..]);
            at = end;
        }

        var space = line.IndexOfAny([' ', '\t']);
        return space < 0 ? (line, "") : (line[..space], line[space..]);
    }

    /// <summary>Arguments split on spaces and commas, honoring quotes (rundll32 uses "file.dll",Entry).</summary>
    private static IEnumerable<string> Tokens(string rest)
    {
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in rest)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (!quoted && c is ' ' or '\t' or ',')
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    // Windows paths; Path.GetFileName on Linux would not split on backslashes.
    public static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
}
