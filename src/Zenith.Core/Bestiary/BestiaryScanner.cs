using System.Collections.Concurrent;
using System.Diagnostics;
using Zenith.Core.Interop;

namespace Zenith.Core.Bestiary;

/// <summary>One program as the Bestiário shows it: what was read and what the classifier made of it.</summary>
public sealed record Sighting(Inhabitant Inhabitant, Verdict Verdict);

/// <param name="HiddenProcesses">Processes whose file Windows does not let a normal user read (mostly Windows' own).</param>
public sealed record BestiaryScan(IReadOnlyList<Sighting> Sightings, int HiddenProcesses, DateTimeOffset At);

/// <summary>
/// Reads who lives in the PC: running processes grouped by file, plus programs that start with Windows but are
/// not running now. Runs without administrator rights, so a few protected processes stay out (counted).
/// </summary>
public static class BestiaryScanner
{
    private static readonly ConcurrentDictionary<(string Path, DateTime Written, long Size), SignatureInfo> SignatureCache = new();

    public static BestiaryScan Scan(TimeSpan? cpuWindow = null, Catalog? catalog = null)
    {
        var window = cpuWindow ?? TimeSpan.FromSeconds(1);
        var processes = Process.GetProcesses();
        var paths = new Dictionary<int, string>();
        var memory = new Dictionary<int, long>();
        var cpuStart = new Dictionary<int, long>();
        var hidden = 0;

        foreach (var p in processes)
        {
            using (p)
            {
                if (p.Id is 0 or 4 || ProcessInfo.ImagePath(p.Id) is not { } path || !Path.IsPathRooted(path))
                {
                    hidden++;
                    continue;
                }
                paths[p.Id] = path;
                try { memory[p.Id] = p.WorkingSet64; } catch (InvalidOperationException) { }
                if (ProcessInfo.CpuTime(p.Id) is { } t) cpuStart[p.Id] = t;
            }
        }

        var clock = Stopwatch.StartNew();
        Thread.Sleep(window);
        var cpu = new Dictionary<int, double>();
        var elapsed = clock.Elapsed.Ticks * (double)Environment.ProcessorCount;
        foreach (var (pid, start) in cpuStart)
            if (ProcessInfo.CpuTime(pid) is { } end && end >= start)
                cpu[pid] = (end - start) / elapsed * 100;

        var network = SafeNetwork();

        var groups = paths.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList(), StringComparer.OrdinalIgnoreCase);

        var startup = StartupReader.Read()
            .Where(s => s.Parsed is not null)
            .GroupBy(s => Resolve(s.Parsed!.Subject), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var classifier = new Classifier(catalog ?? Catalog.Default, ClassifierContext.Current);
        var sightings = groups.Keys.Union(startup.Keys, StringComparer.OrdinalIgnoreCase)
            .AsParallel()
            .Select(path =>
            {
                var pids = groups.GetValueOrDefault(path) ?? [];
                var inhabitant = Describe(path, pids,
                    Sum(pids, memory), Sum(pids, cpu), RemoteHosts(pids, network),
                    startup.GetValueOrDefault(path) ?? []);
                return new Sighting(inhabitant, classifier.Classify(inhabitant));
            })
            .ToList();

        return new BestiaryScan(sightings, hidden, DateTimeOffset.Now);
    }

    public static SignatureInfo Signature(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return SignatureInfo.NotChecked;
            return SignatureCache.GetOrAdd((path.ToLowerInvariant(), info.LastWriteTimeUtc, info.Length), _ => WinTrust.Check(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SignatureInfo.NotChecked;
        }
    }

    private static Inhabitant Describe(string path, IReadOnlyList<int> pids, long? memory, double? cpu, int? remoteHosts,
        IReadOnlyList<StartupEntry> startup)
    {
        string? description = null, company = null;
        try
        {
            if (File.Exists(path))
            {
                var version = FileVersionInfo.GetVersionInfo(path);
                description = Blank(version.FileDescription);
                company = Blank(version.CompanyName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return new Inhabitant(path, CommandLine.FileName(path), description, company, Signature(path), pids, memory, cpu, remoteHosts, startup);
    }

    /// <summary>A bare "program.exe" in a Run value is found in System32, like Windows does.</summary>
    private static string Resolve(string subject) =>
        Path.IsPathRooted(subject) ? subject : Path.Combine(Environment.SystemDirectory, subject);

    private static long? Sum(IReadOnlyList<int> pids, Dictionary<int, long> values) =>
        pids.Any(values.ContainsKey) ? pids.Sum(p => values.GetValueOrDefault(p)) : null;

    private static double? Sum(IReadOnlyList<int> pids, Dictionary<int, double> values) =>
        pids.Any(values.ContainsKey) ? pids.Sum(p => values.GetValueOrDefault(p)) : null;

    private static int? RemoteHosts(IReadOnlyList<int> pids, Dictionary<int, HashSet<string>>? network) =>
        network is null || pids.Count == 0 ? null
            : pids.SelectMany(p => network.GetValueOrDefault(p) ?? []).Distinct().Count();

    private static Dictionary<int, HashSet<string>>? SafeNetwork()
    {
        try
        {
            return ProcessInfo.RemoteHostsByProcess();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
