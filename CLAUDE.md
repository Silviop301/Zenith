# Zenith: contexto do projeto

App para Windows que explica o PC de qualquer pessoa em linguagem simples e conserta o que é seguro consertar. Frase-guia: **seu PC, explicado**. Plano completo em `docs/plano-do-produto.md`.

**Estado atual:** Fase 0 (provas técnicas) em andamento. Já existem os leitores dos 6 achados do Resgate e o `zenith-probe`, que testa no PC do Silvio e serve de coletor em PCs de terceiros. Ainda faltam o diário de alterações e os aplicadores. Plano da Fase 0: [Claude Doc](https://claude.ai/artifact/CVLR3mJZzGYEfzpjZPJcHX).

## Como trabalhar neste projeto

- Faça perguntas de direcionamento antes de implementar qualquer coisa nova. Não comece a codar com decisões em aberto.
- Comunicação em português do Brasil, direta, sem enrolação, com estrutura prática.
- Visual: premium e calmo, paletas limpas, animações suaves. Nada de neon ou estética cyberpunk.

## Regras inegociáveis

1. **Ler tudo, consertar o que é seguro, explicar o resto.**
2. **Toda alteração é reversível:** passa pelo diário de alterações com valor anterior e novo, e tem desfazer.
3. **Nunca afirmar sem evidência:** cada achado mostra de onde veio; ganho de desempenho só com medição antes e depois.
4. **Sistema e antivírus são intocáveis:** nada pausa, fecha ou altera processos do Windows ou de proteção.
5. **Segurança nunca é paga:** detecção de suspeitos e avisos críticos ficam no plano grátis.
6. **Sem promessa vaga:** nenhum texto do tipo "deixe seu PC mais rápido".

## Escopo do MVP (só o Resgate)

Varredura em 1 clique, achados explicados, aplicar, desfazer e guias. Achados da v1:

1. Monitor abaixo da taxa máxima (aplica)
2. HDR disponível e desligado (liga com teste de 15 s)
3. Plano de energia limitando o processador (aplica)
4. Jogo rodando na placa de vídeo integrada (define a dedicada por jogo)
5. Monitor ligado no vídeo da placa-mãe com placa dedicada instalada (guia)
6. Memória abaixo da velocidade de fábrica (guia na BIOS; detecção limitada)

Também na v1: diário de alterações com "desfazer tudo" e coleta silenciosa de sensores (só o que dá sem driver próprio).

Fora da v1: Bestiário, Revisão, modo gamer, detecção de cabo e de G-Sync/FreeSync.

## Tecnologia (aprovada em 24/10/2026)

- **Interface:** C# + .NET 10 + WPF, sem privilégio de administrador.
- **Núcleo:** serviço do Windows em C#, com privilégio; leitores, aplicadores e diário de alterações.
- **Comunicação:** canal local por named pipes.
- **Dados locais:** SQLite (histórico de sensores e diário).
- **Base de conhecimento:** arquivos atualizáveis (fichas de programas, modelos de memória, jogos reconhecidos).
- **Sem driver de kernel na v1.** O WinRing0 é sinalizado pelo Defender; se um dia precisar de sensores de baixo nível, avaliar o PawnIO.

Diagrama: `docs/diagramas/arquitetura.svg`.

## Marca e design

- Nome provisório **Zenith** (pesquisa no INPI pendente). Símbolo **Ascensão**: pontos que sobem e crescem; o ponto do topo indica status (azul-noite em repouso, roxo #6B5B8E ativo, âmbar #B07F2E em alerta).
- Cores, raios, tipografia (Manrope) e movimento: `design/tokens/` (JSON, CSS e `Cores.xaml` para WPF).
- Arquivos do símbolo, ícone e bandeja: `design/marca/`.
- Telas de referência: `design/mockups/index.html`. Elas ainda usam um ícone provisório na barra lateral; a marca final está em `design/marca/`.

## Decisões tomadas

- Nomes no código em inglês (classes, métodos, variáveis); textos de interface em português. (24/10/2026)
- Lançamento só em português; código preparado para tradução desde o início (arquivos de recursos), inglês depois. (24/10/2026)
- Código fechado por enquanto; abrir partes (ex.: base de conhecimento) pode ser reavaliado depois. (24/10/2026)
- Critério da Fase 0 adaptado a um único desktop de teste: os achados testáveis no desktop detectam, aplicam e desfazem sem sobras; os que não se aplicam não aparecem lá; e o coletor (só leitura) roda sem erro em pelo menos 5 PCs de terceiros, entre eles 1 notebook com duas placas e 1 PC com vídeo integrado. (25/10/2026)
- Pro cobrado de dois jeitos: assinatura mensal ou licença vitalícia (mais cara). Valores ainda em aberto. (24/10/2026)

## Decisões em aberto

- Valores do Pro (mensal e vitalícia)
- Resultado da pesquisa de marca no INPI

## PC de teste

Desktop do Silvio: Ryzen 5 5600 (sem vídeo integrado, então os achados 4 e 5 não se aplicam), RTX 3060, monitor LG UltraGear 32" a 165 Hz, 2 x DDR4 3600 (JHD3600U1808JG, fabricante "Unknown" no WMI, XMP ligado), placa-mãe Gigabyte B550M AORUS ELITE.

## Código

- `src/Zenith.Core`: leitores dos achados (`Readers/`), retrato do hardware (`Machine/`), chamadas às APIs do Windows (`Interop/`) e base de conhecimento embutida (`KnowledgeBase/`).
- `src/Zenith.Probe`: `zenith-probe.exe`, ferramenta de teste e coletor. Só lê, imprime o resultado e salva um relatório .json.
- `tests/Zenith.Core.Tests`: testes xUnit das partes que não dependem do Windows (classificadores e leitura de textos).
- Compilar e testar: `dotnet build` e `dotnet test`. Compila também em Linux (`EnableWindowsTargeting`), mas só roda no Windows.
- O CI (GitHub Actions, `windows-latest`) compila, testa, roda o probe e publica o `zenith-probe.exe` como artefato.
- Nomes no código e comentários em inglês; textos para o usuário em português.
