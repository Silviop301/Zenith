# Zenith

**Seu PC, explicado.** App para Windows que explica o PC de qualquer pessoa em linguagem simples e conserta o que é seguro consertar.

O projeto está na **Fase 0**: provas técnicas dos 6 achados do Resgate antes de construir o app. O plano do produto está em [`docs/plano-do-produto.md`](docs/plano-do-produto.md), e o contexto para quem trabalha no código está em [`CLAUDE.md`](CLAUDE.md).

## O que tem aqui

| Pasta | Conteúdo |
| --- | --- |
| `src/Zenith.Core` | Leitores dos achados, retrato do hardware e chamadas às APIs do Windows |
| `src/Zenith.Service` | `zenith-service.exe`: o serviço do Windows que guarda o diário de alterações |
| `src/Zenith.Probe` | `zenith-probe.exe`: verifica (só lendo) e, com o serviço instalado, aplica e desfaz |
| `scripts/` | Instalar e remover o serviço |
| `tests/Zenith.Core.Tests` | Testes automáticos |
| `docs/` | Plano do produto e diagramas |
| `design/` | Mockups, marca e tokens de design |

## Rodar o zenith-probe

**Sem instalar nada:** abra a aba **Actions** do repositório, entre na execução mais recente do workflow `build` e baixe o artefato `zenith-phase0`. Descompacte e dê dois cliques em `zenith-probe.exe`. O Windows pode avisar que o programa não é reconhecido, porque ele ainda não tem assinatura digital. Nesse caso, clique em "Mais informações" e depois em "Executar assim mesmo".

**Pelo código:** com o [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) instalado, rode `dotnet run --project src/Zenith.Probe`.

O probe não altera nada no PC. Ele mostra cada achado na tela e salva um arquivo `zenith-relatorio-*.json` ao lado do .exe. O relatório não tem nome de usuário, números de série nem caminhos de pastas.

## Aplicar e desfazer

Aplicar e desfazer precisam do serviço. Instale uma vez, num PowerShell aberto como administrador, dentro da pasta do artefato:

```
powershell -ExecutionPolicy Bypass -File .\instalar-servico.ps1 -ExePath .\zenith-service.exe
```

Depois, num terminal comum (sem administrador):

| Comando | O que faz |
| --- | --- |
| `zenith-probe servico` | Confere se o serviço está rodando |
| `zenith-probe aplicar taxa` | Coloca o monitor na taxa máxima |
| `zenith-probe aplicar hdr` | Liga o HDR. Se você não confirmar com S em 15 s, ele volta a ficar desligado |
| `zenith-probe aplicar energia` | Tira o processador do limite do plano de energia |
| `zenith-probe aplicar placa` | Define a placa dedicada para apps rodando na integrada (PCs com duas placas) |
| `zenith-probe diario` | Lista tudo o que o Zenith mudou |
| `zenith-probe desfazer <número>` | Desfaz uma alteração |
| `zenith-probe desfazer-tudo` | Desfaz tudo o que ainda está ativo |

Para tirar o serviço: `.\remover-servico.ps1`, também como administrador.

## Compilar e testar

```
dotnet build
dotnet test
```
