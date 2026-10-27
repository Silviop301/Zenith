using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zenith.Core;
using Zenith.Core.Findings;

namespace Zenith.Probe;

/// <summary>Read-only scan: prints every finding and saves a JSON report. This is what third parties run.</summary>
internal static class ScanCommand
{
    public static int Run(string[] args)
    {
        var outIndex = Array.IndexOf(args, "--out");
        var outPath = outIndex >= 0 && outIndex + 1 < args.Length
            ? args[outIndex + 1]
            : Path.Combine(AppContext.BaseDirectory, $"zenith-relatorio-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        Console.WriteLine("Zenith · verificação do Resgate (somente leitura)");
        Console.WriteLine("Nada no seu PC será alterado.");
        Console.WriteLine();

        var report = Scanner.Run();

        Console.WriteLine($"Processador: {report.Machine.CpuName ?? "?"}");
        foreach (var gpu in report.Machine.GpuAdapters)
            Console.WriteLine($"Placa de vídeo: {gpu.Description} ({(gpu.Kind == GpuKind.Integrated ? "integrada" : "dedicada")})");
        foreach (var display in report.Machine.Displays)
            Console.WriteLine($"Monitor: {display.MonitorName ?? display.GdiDeviceName}, {display.Width}x{display.Height}");
        Console.WriteLine();

        foreach (var finding in report.Findings)
        {
            Console.WriteLine($"[{Label(finding.Status)}] {finding.Title}");
            Console.WriteLine($"    {finding.Summary}");
            if (finding.Error is not null) Console.WriteLine($"    Erro: {finding.Error}");
        }

        foreach (var (section, error) in report.Machine.CaptureErrors)
            Console.WriteLine($"Aviso: não foi possível ler \"{section}\": {error}");

        var json = JsonSerializer.Serialize(new { schemaVersion = 1, probeVersion = typeof(Scanner).Assembly.GetName().Version?.ToString(), report },
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() },
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        File.WriteAllText(outPath, json, Encoding.UTF8);

        Console.WriteLine();
        Console.WriteLine($"Relatório salvo em: {outPath}");
        Console.WriteLine("Você pode abrir o arquivo e conferir o que ele contém antes de enviar.");
        return 0;
    }

    private static string Label(FindingStatus status) => status switch
    {
        FindingStatus.Detected => "ACHADO",
        FindingStatus.NotDetected => "ok",
        FindingStatus.NotApplicable => "não se aplica",
        FindingStatus.Inconclusive => "inconclusivo",
        FindingStatus.Error => "erro",
        _ => status.ToString(),
    };
}
