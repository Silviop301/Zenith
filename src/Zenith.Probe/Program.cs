using System.Text;
using Zenith.Core.Ipc;
using Zenith.Probe;

// Zenith Probe: Phase 0 test tool and read-only collector.
// With no command it only reads (the collector). The other commands talk to the Zenith service and
// change settings, always through the change journal.

Console.OutputEncoding = Encoding.UTF8;

// The command, when given, comes first; options like "--out <file>" belong to it.
var hasCommand = args.Length > 0 && !args[0].StartsWith("--");
var command = hasCommand ? args[0].ToLowerInvariant() : "verificar";
var rest = hasCommand ? args[1..] : args;

int exitCode;
try
{
    exitCode = command switch
    {
        "verificar" => ScanCommand.Run(rest),
        "servico" => ChangeCommands.Ping(),
        "aplicar" => ChangeCommands.Apply(rest),
        "diario" => ChangeCommands.Journal(),
        "desfazer" => ChangeCommands.Undo(rest),
        "desfazer-tudo" => ChangeCommands.UndoAll(),
        "sensores" => ChangeCommands.Sensors(),
        _ => Usage(),
    };
}
catch (ServiceUnavailableException ex)
{
    Console.WriteLine(ex.Message);
    exitCode = 2;
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Erro: {ex.Message}");
    exitCode = 1;
}

if (command == "verificar" && !rest.Contains("--no-wait") && !Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.WriteLine("Pressione Enter para sair.");
    Console.ReadLine();
}
return exitCode;

static int Usage()
{
    Console.WriteLine("""
        Uso: zenith-probe [comando]

          verificar            Lê os 6 achados e salva um relatório (padrão; não altera nada)
          servico              Confere se o serviço do Zenith está rodando
          aplicar <achado>     Conserta um achado: taxa, hdr, energia ou placa
          diario               Lista as alterações feitas pelo Zenith
          desfazer <número>    Desfaz uma alteração do diário
          desfazer-tudo        Desfaz todas as alterações ainda ativas
          sensores             Mostra a leitura mais recente de cada sensor coletado pelo serviço

        Opções de verificar: --out <arquivo>, --no-wait
        """);
    return 1;
}
