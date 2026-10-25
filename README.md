# Zenith

**Seu PC, explicado.** App para Windows que explica o PC de qualquer pessoa em linguagem simples e conserta o que é seguro consertar.

O projeto está na **Fase 0**: provas técnicas dos 6 achados do Resgate antes de construir o app. O plano do produto está em [`docs/plano-do-produto.md`](docs/plano-do-produto.md), e o contexto para quem trabalha no código está em [`CLAUDE.md`](CLAUDE.md).

## O que tem aqui

| Pasta | Conteúdo |
| --- | --- |
| `src/Zenith.Core` | Leitores dos achados, retrato do hardware e chamadas às APIs do Windows |
| `src/Zenith.Probe` | `zenith-probe.exe`: roda todas as verificações, só lendo, e salva um relatório |
| `tests/Zenith.Core.Tests` | Testes automáticos |
| `docs/` | Plano do produto e diagramas |
| `design/` | Mockups, marca e tokens de design |

## Rodar o zenith-probe

**Sem instalar nada:** abra a aba **Actions** do repositório, entre na execução mais recente do workflow `build` e baixe o artefato `zenith-probe`. Descompacte e dê dois cliques em `zenith-probe.exe`. O Windows pode avisar que o programa não é reconhecido, porque ele ainda não tem assinatura digital. Nesse caso, clique em "Mais informações" e depois em "Executar assim mesmo".

**Pelo código:** com o [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) instalado, rode `dotnet run --project src/Zenith.Probe`.

O probe não altera nada no PC. Ele mostra cada achado na tela e salva um arquivo `zenith-relatorio-*.json` ao lado do .exe. O relatório não tem nome de usuário, números de série nem caminhos de pastas.

## Compilar e testar

```
dotnet build
dotnet test
```
