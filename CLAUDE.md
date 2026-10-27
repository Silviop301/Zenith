# Zenith: contexto do projeto

App para Windows que explica o PC de qualquer pessoa em linguagem simples e conserta o que é seguro consertar. Frase-guia: **seu PC, explicado**. Plano completo em `docs/plano-do-produto.md`.

**Estado atual:** Fase 0 (provas técnicas) concluída em 27/10/2026; próximo passo é a Fase 1 (Resgate grátis). Já existem os leitores dos 6 achados, o diário de alterações, o serviço do Windows e os aplicadores dos achados 1 a 4. O `zenith-probe` lê (coletor) e também aplica e desfaz pelo serviço. Leitores, aplicar e desfazer foram validados no desktop do Silvio (achados 1, 2, 3 e 6). Plano da Fase 0: [Claude Doc](https://claude.ai/artifact/CVLR3mJZzGYEfzpjZPJcHX).

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
- Instalador em Inno Setup (`.exe` próprio), não MSIX nem MSI. (27/10/2026)
- Distribuição: sem assinatura de código por enquanto (custo); atualização automática pelas Releases de um repositório público só de versões (`zenith-versoes`), instalação só com o clique do usuário. (27/10/2026)
- Bandeja: o app inicia com o Windows, discreto na bandeja; o instalador traz a opção marcada e dá para desligar no menu do ícone. Fechar a janela volta para a bandeja; avisos só em alerta crítico. (27/10/2026)
- Fase 0 já usa o serviço do Windows (com privilégio e named pipes) para aplicar, desfazer e guardar o diário; não fica tudo no probe. (27/10/2026)
- O serviço é dono do diário e aplica só o que é do sistema (plano de energia). O que é da sessão ou do perfil do usuário (taxa do monitor, HDR, placa por jogo) é aplicado por um agente sem privilégio na sessão do usuário (o probe agora, o app depois), que registra tudo no diário do serviço. Motivo: o serviço roda na sessão 0 e não alcança a tela nem o perfil de quem está logado. (27/10/2026)
- Fase 0 encerrada sem o teste do coletor em PCs de terceiros: o Silvio não tem testadores. Os achados 4 e 5 e a classificação de placa integrada/dedicada nunca rodaram em hardware real com duas placas, então são o maior risco de achado errado. (27/10/2026)
- Coleta de sensores da v1: saúde do disco (SMART), desgaste da bateria, temperatura e uso da placa de vídeo, uso de CPU e memória. Amostra a cada 5 min (disco e bateria uma vez por dia), só no PC, histórico de 1 ano. Temperatura do processador fica de fora sem driver. (27/10/2026)
- Pro cobrado de dois jeitos: assinatura mensal ou licença vitalícia (mais cara). Valores ainda em aberto. (24/10/2026)

## Decisões em aberto

- Valores do Pro (mensal e vitalícia)
- Resultado da pesquisa de marca no INPI

## PC de teste

Desktop do Silvio: Ryzen 5 5600 (sem vídeo integrado, então os achados 4 e 5 não se aplicam), RTX 3060, monitor LG UltraGear 32" a 165 Hz, 2 x DDR4 3600 (JHD3600U1808JG, fabricante "Unknown" no WMI, XMP ligado), placa-mãe Gigabyte B550M AORUS ELITE.

## Código

- `src/Zenith.Core`: leitores dos achados (`Readers/`), retrato do hardware (`Machine/`), chamadas às APIs do Windows (`Interop/`) e base de conhecimento embutida (`KnowledgeBase/`).
- `src/Zenith.Core/Changes`: diário de alterações (SQLite) e `ChangeEngine`, que aplica, confere e desfaz. O desfazer nunca sobrescreve um valor que outra pessoa mudou depois.
- `src/Zenith.Core/Settings` e `Fixes`: cada configuração que o Zenith sabe ler e escrever, e os consertos de cada achado.
- `src/Zenith.Core/Ipc`: protocolo do pipe `Zenith.Core.v1` (uma linha JSON por mensagem), cliente e `CommandHandler` do serviço.
- `src/Zenith.App`: `zenith.exe`, o app WPF (sem privilégio). Telas Achados e Resgatados, teste do HDR e guias. `--demo` abre com um PC de exemplo; `--screenshot <pasta>` salva um PNG de cada tela (o CI publica os prints no artefato `zenith-telas`). Textos da interface em `src/Zenith.Core/Resources/Strings.resx`; regras de cada cartão em `src/Zenith.Core/Presentation`. Fonte Manrope embutida (OFL).
- `src/Zenith.App/Tray`: ícone na bandeja (desenhado na hora, a partir de `design/marca/zenith-bandeja-16*.svg`, claro ou escuro conforme a barra de tarefas), menu, uma instância só e preferências em `HKCU\Software\Zenith`. `zenith.exe --bandeja` inicia só na bandeja, que é o que o Windows roda no login. Fechar a janela deixa o app na bandeja. O ponto do topo fica roxo enquanto aplica, âmbar quando há achado para ativar, serviço desligado ou alerta crítico; regras em `Presentation/TrayPresenter.cs`. Alerta crítico hoje: disco com erros de leitura não corrigidos, avisado uma vez por valor.
- `src/Zenith.Core/Agent/SessionAgent.cs`: aplicar, reverter e desfazer pelo lado do usuário; usado pelo probe e pelo app.
- `src/Zenith.Core/Sensors`: coleta silenciosa (`SensorCollector`, `SensorStore` em `%ProgramData%\Zenith\sensors.db`, 1 ano) e as fontes: CPU e memória, NVIDIA (NVML), disco (`MSFT_StorageReliabilityCounter`) e bateria. Roda no serviço (`SensorWorker`); `zenith-probe sensores` mostra a última leitura de cada sensor.
- `src/Zenith.Service`: `zenith-service.exe`, o serviço do Windows (LocalSystem). Guarda o diário em `%ProgramData%\Zenith\journal.db` e aplica só o plano de energia.
- `src/Zenith.Probe`: `zenith-probe.exe`. Sem comando, só lê e salva um relatório (coletor). Com `aplicar`, `diario`, `desfazer` e `desfazer-tudo`, age como o agente na sessão do usuário.
- `installer/zenith.iss`: instalador Inno Setup (`zenith-setup-<versão>.exe`). Instala app, probe e serviço em `Program Files\Zenith`, com um único pedido de administrador. Ao desinstalar, oferece desfazer tudo: se sim, apaga também o diário; se não, guarda o diário para desfazer depois. O CI gera o instalador e testa instalar, aplicar e desinstalar de ponta a ponta.
- `src/Zenith.App/Updates/Updater.cs` e `src/Zenith.Core/Updates`: atualização automática. Uma vez por dia, a cópia instalada lê `versao.json` da última versão em `Silviop301/zenith-versoes` (repositório público só de versões), baixa o instalador para `%LocalAppData%\Zenith\atualizacao`, confere o SHA-256 e oferece "Instalar agora" na barra lateral e no menu da bandeja. Nunca instala sozinho. Publicar uma versão: Actions → build → Run workflow no `main` (precisa do secret `ZENITH_VERSOES_TOKEN`). A versão é `0.1.<número da execução do CI>`.
- O instalador leva os três programas numa pasta só, com um .NET compartilhado (`-p:SharedInstall=true`). Os .exe de arquivo único continuam no artefato para uso avulso (o probe como coletor).
- `scripts/`: instalar e remover só o serviço (PowerShell como administrador), para testes sem o instalador.
- `tests/Zenith.Core.Tests`: testes xUnit das partes que não dependem do Windows (classificadores, leitura de textos, diário, `ChangeEngine` e `CommandHandler`).
- Compilar e testar: `dotnet build` e `dotnet test`. Compila também em Linux (`EnableWindowsTargeting`), mas só roda no Windows.
- O CI (GitHub Actions, `windows-latest`) compila, testa, roda o probe, instala o serviço e testa aplicar e desfazer o plano de energia de ponta a ponta. Publica os dois .exe e os scripts como artefato.
- Nomes no código e comentários em inglês; textos para o usuário em português.
