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

    private JournalEntry Record(string findingId, string key, ChangeScope scope, string target, string previous, string next, DateTimeOffset? at = null)
    {
        var entry = new JournalEntry(_journal.Count + 1, findingId, key, scope, scope == ChangeScope.User ? Sid : null, target,
            previous, next, at ?? DateTimeOffset.Now, JournalStatus.Applied, null, null);
        _journal.Add(entry);
        return entry;
    }
}
