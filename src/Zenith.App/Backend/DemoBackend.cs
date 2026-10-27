using Zenith.Core.Bestiary;
using Zenith.Core.Changes;
using Zenith.Core.Findings;
using Zenith.Core.Sensors;
using Zenith.Core.Settings;

namespace Zenith.App.Backend;

/// <summary>
/// A made-up PC with five findings, kept in memory, for screenshots in CI and design reviews (zenith --demo).
/// Nothing on the real PC is read or changed.
/// </summary>
public sealed class DemoBackend : IBackend
{
    private const string Sid = "S-1-5-21-demo";
    private const string Monitor = @"\\?\DISPLAY#DEMO";
    private readonly List<JournalEntry> _journal = [];
    private readonly HashSet<string> _fixed = [];
    private readonly HashSet<string> _startupOff = [];
    private readonly Lock _lock = new();

    public DemoBackend()
    {
        // One change from an earlier session, so the Resgatados screen is not empty.
        Record(FindingIds.GameOnIntegratedGpu, GpuPreferenceSetting.SettingKey, ChangeScope.User,
            @"C:\Jogos\Valorant\VALORANT.exe", GpuPreferenceSetting.Unset, GpuPreferenceSetting.HighPerformance, DateTimeOffset.Now.AddDays(-1));
    }

    public string UserSid => Sid;

    public ServiceState CheckService() => ServiceState.Ready;

    public ScanOutcome Scan()
    {
        lock (_lock)
        {
            FindingResult R(string id, string title, string summary, string source, FindingChange change, Dictionary<string, object?>? evidence = null) =>
                _fixed.Contains(id)
                    ? new(id, title, FindingStatus.NotDetected, "Já está certo.", source, evidence ?? [])
                    : new(id, title, FindingStatus.Detected, summary, source, evidence ?? [], Change: change);

            return new ScanOutcome(DateTimeOffset.Now,
            [
                R(FindingIds.RefreshRate, "Monitor abaixo da taxa máxima",
                    "LG ULTRAGEAR está em 60 Hz, mas aceita até 144 Hz nesta resolução.",
                    "API de configuração de tela do Windows (QueryDisplayConfig, EnumDisplaySettings)", new("60 Hz", "144 Hz")),
                R(FindingIds.PowerPlan, "Plano de energia limitando o processador",
                    "O processador não está rendendo tudo: o plano ativo é \"Economia de energia\".",
                    "API de energia do Windows (powrprof)", new("Economia de energia", "Equilibrado"),
                    new() { ["schemeKind"] = "power-saver" }),
                R(FindingIds.Hdr, "HDR disponível e desligado",
                    "LG ULTRAGEAR tem HDR, mas ele está desligado.",
                    "Informações de cor avançada da API de tela do Windows (DisplayConfigGetDeviceInfo)", new("Desligado", "Ligado")),
                new(FindingIds.GameOnIntegratedGpu, "Jogo rodando na placa de vídeo integrada", FindingStatus.NotDetected,
                    "Nenhum programa aberto agora está usando a placa integrada para 3D.",
                    "Contadores \"GPU Engine\" do Windows e preferências gráficas por app (UserGpuPreferences)", new Dictionary<string, object?>()),
                R(FindingIds.MonitorOnMotherboard, "Monitor ligado no vídeo da placa-mãe",
                    "LG ULTRAGEAR está ligado na saída de vídeo da placa-mãe, e não na placa de vídeo dedicada.",
                    "Lista de adaptadores de vídeo e saídas (DXGI) e tipo de gabinete (WMI)", new("Placa-mãe", "Placa de vídeo")),
                R(FindingIds.MemorySpeed, "Memória abaixo da velocidade de fábrica",
                    "A memória está rodando a 2133 MHz, mas foi feita para 3200 MHz. Dá para ativar o perfil XMP/EXPO na BIOS.",
                    "WMI (Win32_PhysicalMemory) e base de modelos de memória do Zenith", new("2133 MHz", "3200 MHz")),
            ]);
        }
    }

    public IReadOnlyList<JournalEntry> Apply(string findingId)
    {
        Thread.Sleep(400);
        lock (_lock)
        {
            if (!_fixed.Add(findingId)) return [];
            return findingId switch
            {
                FindingIds.RefreshRate => [Record(findingId, RefreshRateSetting.SettingKey, ChangeScope.User, Monitor, "60", "144")],
                FindingIds.Hdr => [Record(findingId, HdrSetting.SettingKey, ChangeScope.User, Monitor, HdrSetting.Off, HdrSetting.On)],
                FindingIds.PowerPlan => [Record(findingId, ActivePowerSchemeSetting.SettingKey, ChangeScope.System, ActivePowerSchemeSetting.MachineTarget,
                    "a1841308-3541-4fab-bc81-f71556f20b4a", "381b4222-f694-41f0-9685-ff5bb260df2e")],
                _ => [],
            };
        }
    }

    public IReadOnlyList<UndoResult> Revert(IReadOnlyList<JournalEntry> applied) =>
        applied.OrderByDescending(e => e.Id).Select(e => Undo(e.Id)).ToList();

    public IReadOnlyList<JournalEntry> Journal()
    {
        lock (_lock) return _journal.ToList();
    }

    public UndoResult Undo(long id)
    {
        lock (_lock)
        {
            var i = _journal.FindIndex(e => e.Id == id);
            var entry = _journal[i];
            if (entry.Status != JournalStatus.Applied) return new UndoResult(id, entry.Status, "Esta alteração já tinha sido desfeita.");
            _journal[i] = entry with { Status = JournalStatus.Undone, ClosedAt = DateTimeOffset.Now };
            _fixed.Remove(entry.FindingId);
            _startupOff.Remove(entry.Target);
            return new UndoResult(id, JournalStatus.Undone, "Desfeito.");
        }
    }

    public IReadOnlyList<UndoResult> UndoAll()
    {
        List<long> active;
        lock (_lock) active = _journal.Where(e => e.Status == JournalStatus.Applied).OrderByDescending(e => e.Id).Select(e => e.Id).ToList();
        return active.Select(Undo).ToList();
    }

    public IReadOnlyList<SensorSample> LatestSensors() =>
    [
        new(SensorKeys.GpuTemperature, "NVIDIA GeForce RTX 3060", 54, DateTimeOffset.Now),
        new(SensorKeys.DiskReadErrors, "Samsung SSD 980 1TB (disco 0)", 0, DateTimeOffset.Now),
    ];

    private const long MB = 1024 * 1024;

    /// <summary>A dozen programs, classified by the real classifier and catalog, including the mockup's impostor.</summary>
    public BestiaryScan Bestiary()
    {
        Thread.Sleep(300);
        lock (_lock)
        {
            var microsoft = new SignatureInfo(SignatureStatus.Valid, "Microsoft Windows");
            SignatureInfo By(string signer) => new(SignatureStatus.Valid, signer);
            StartupEntry Run(StartupLocation location, string name, string command) =>
                new(location, name, command, CommandLine.Parse(command, s => s),
                    !_startupOff.Contains(StartupTarget.Format(location, name)));
            Inhabitant I(string path, SignatureInfo signature, int processes, long? memory, double? cpu, int? hosts, params StartupEntry[] startup) =>
                new(path, CommandLine.FileName(path), null, null, signature, Enumerable.Range(100, processes).ToList(),
                    processes > 0 ? memory : null, processes > 0 ? cpu : null, processes > 0 ? hosts : null, startup);

            const string discord = @"C:\Users\ana\AppData\Local\Discord";
            var inhabitants = new[]
            {
                I(@"C:\Users\ana\AppData\Roaming\Microsoft\svch0st.exe", new SignatureInfo(SignatureStatus.Unsigned), 1, 46 * MB, 12, 3,
                    Run(StartupLocation.UserRun, "Windows Host", @"C:\Users\ana\AppData\Roaming\Microsoft\svch0st.exe")),
                I(@"C:\Program Files (x86)\AnyDesk\AnyDesk.exe", By("AnyDesk Software GmbH"), 2, 38 * MB, 0.2, 1,
                    Run(StartupLocation.MachineRun32, "AnyDesk", @"""C:\Program Files (x86)\AnyDesk\AnyDesk.exe"" --tray")),
                I(@"C:\Program Files\Google\Chrome\Application\chrome.exe", By("Google LLC"), 18, 1460 * MB, 6.4, 9),
                I(@"C:\Windows\System32\svchost.exe", microsoft, 74, 690 * MB, 1.8, 6),
                I(@"C:\Program Files (x86)\Microsoft\EdgeWebView\Application\130.0.2849.56\msedgewebview2.exe", By("Microsoft Corporation"), 7, 410 * MB, 0.6, 2),
                I($@"{discord}\app-1.0.9170\Discord.exe", By("Discord Inc."), 6, 380 * MB, 1.1, 4),
                I($@"{discord}\Update.exe", By("Discord Inc."), 0, null, null, null,
                    Run(StartupLocation.UserRun, "Discord", $@"{discord}\Update.exe --processStart Discord.exe")),
                I(@"C:\Program Files (x86)\Steam\steam.exe", By("Valve Corp."), 3, 230 * MB, 0.4, 2,
                    Run(StartupLocation.UserRun, "Steam", @"""C:\Program Files (x86)\Steam\steam.exe"" -silent")),
                I(@"C:\ProgramData\Microsoft\Windows Defender\Platform\4.18.24090.11-0\MsMpEng.exe", By("Microsoft Corporation"), 1, 210 * MB, 0.3, 1),
                I(@"C:\Users\ana\AppData\Roaming\Spotify\Spotify.exe", By("Spotify AB"), 5, 260 * MB, 0.8, 3,
                    Run(StartupLocation.UserRun, "Spotify", @"""C:\Users\ana\AppData\Roaming\Spotify\Spotify.exe"" --autostart --minimized")),
                I(@"C:\Program Files\LGHUB\system_tray\lghub_system_tray.exe", By("Logitech Inc"), 1, 64 * MB, 0.1, 0,
                    Run(StartupLocation.MachineRun, "LGHUB", @"""C:\Program Files\LGHUB\system_tray\lghub_system_tray.exe"" --minimized")),
                I(@"C:\Program Files\HP\HP Smart Update\hpsmartupdate.exe", By("HP Inc."), 0, null, null, null,
                    Run(StartupLocation.MachineRun, "HP Smart Update", @"""C:\Program Files\HP\HP Smart Update\hpsmartupdate.exe"" /background")),
                I(@"C:\Windows\explorer.exe", microsoft, 1, 150 * MB, 0.5, 0),
                I(@"C:\Program Files\NVIDIA Corporation\NVIDIA app\CEF\NVIDIA Overlay.exe", By("NVIDIA Corporation"), 2, 120 * MB, 0.2, 0),
            };

            var classifier = new Classifier(Catalog.Default, new ClassifierContext(@"C:\Windows\", @"C:\Users\"));
            var described = inhabitants.Select(i => i.FileName == "hpsmartupdate.exe"
                ? i with { Description = "HP Smart Update", Company = "HP Inc." } : i);
            return new BestiaryScan(described.Select(i => new Sighting(i, classifier.Classify(i))).ToList(), 23, DateTimeOffset.Now);
        }
    }

    public JournalEntry? DisableStartup(string target)
    {
        Thread.Sleep(300);
        lock (_lock)
        {
            if (!_startupOff.Add(target)) return null;
            var location = StartupTarget.Parse(target).Location;
            return Record("bestiary-startup", StartupSetting.KeyFor(location),
                StartupTarget.IsMachineWide(location) ? ChangeScope.System : ChangeScope.User, target, StartupSetting.Enabled, StartupSetting.Disabled);
        }
    }

    private JournalEntry Record(string findingId, string key, ChangeScope scope, string target, string previous, string next, DateTimeOffset? at = null)
    {
        var entry = new JournalEntry(_journal.Count + 1, findingId, key, scope, scope == ChangeScope.User ? Sid : null, target,
            previous, next, at ?? DateTimeOffset.Now, JournalStatus.Applied, null, null);
        _journal.Add(entry);
        return entry;
    }
}
