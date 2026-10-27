using System.Text;

namespace Zenith.Core.Bestiary;

/// <summary>
/// Spots names made to look like a Windows process: "svch0st.exe", "lsas.exe", "scvhost.exe" or
/// Cyrillic letters in place of Latin ones. A match is only a hint; the classifier weighs it with the
/// file's folder and signature.
/// </summary>
public static class ImpostorDetector
{
    /// <summary>Windows processes that malware likes to imitate. All live in the Windows folder.</summary>
    public static readonly IReadOnlyList<string> SystemNames =
    [
        "svchost.exe", "lsass.exe", "csrss.exe", "winlogon.exe", "wininit.exe", "services.exe", "smss.exe",
        "explorer.exe", "spoolsv.exe", "taskhostw.exe", "runtimebroker.exe", "dllhost.exe", "conhost.exe",
        "sihost.exe", "ctfmon.exe", "dwm.exe", "fontdrvhost.exe", "searchindexer.exe", "taskmgr.exe",
    ];

    private static readonly IReadOnlySet<string> SystemNameSet = new HashSet<string>(SystemNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>Short names are not compared by distance: "dwm" is one letter away from too many real programs.</summary>
    private const int MinLengthForDistance = 6;

    public static bool IsSystemName(string fileName) => SystemNameSet.Contains(fileName);

    /// <summary>The Windows process this name imitates, or null. An exact system name is not an imitation.</summary>
    public static string? Imitates(string fileName)
    {
        if (IsSystemName(fileName)) return null;
        var stem = Normalize(Stem(fileName));
        foreach (var system in SystemNames)
        {
            var target = Normalize(Stem(system));
            if (stem == target) return system;
            if (target.Length >= MinLengthForDistance && Distance(stem, target) == 1) return system;
        }
        return null;
    }

    private static string Stem(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        return name.EndsWith(".exe") ? name[..^4] : name;
    }

    /// <summary>Folds look-alike characters together, so "svch0st" and "svсhost" (Cyrillic с) both become "svchost".</summary>
    internal static string Normalize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            sb.Append(c switch
            {
                '0' or 'о' or 'ο' => 'o',
                '1' or 'i' or '|' or '!' or 'і' or 'ӏ' or 'ι' => 'l',
                '3' or 'е' or 'ε' => 'e',
                '5' or 'ѕ' or '$' => 's',
                'а' or 'α' or '@' => 'a',
                'с' or 'ϲ' => 'c',
                'р' or 'ρ' => 'p',
                'х' or 'χ' => 'x',
                'у' => 'y',
                'ԁ' => 'd',
                'һ' => 'h',
                'ո' => 'n',
                'ѡ' or 'ш' => 'w',
                _ => c,
            });
        }
        return sb.ToString().Replace("rn", "m").Replace("vv", "w");
    }

    /// <summary>Damerau-Levenshtein distance (optimal string alignment): a swap of two neighbors counts as one edit.</summary>
    internal static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
        }
        return d[a.Length, b.Length];
    }
}
