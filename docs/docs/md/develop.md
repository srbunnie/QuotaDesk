# Desenvolver

Ambientes isolados, autotestes, como contribuir e como adicionar uma ferramenta nova ao QuotaDesk.

## Estrutura do código

Tudo fica em `src/QuotaDesk` (projeto `AIUsageMonitor.csproj`, assembly `QuotaDesk`), em C# .NET 10 WinForms. Dependências NuGet: `ModelContextProtocol.Core` e `System.IO.Ports`.

| Arquivos | Papel |
|---|---|
| `Program.cs`, `MonitorContext.cs`, `AppPaths.cs` | entrada, instância única, pasta de dados de cada ambiente, bandeja, timers e ciclo de vida |
| `Models.cs`, `ProviderModule.cs`, `ProviderCategories.cs`, `ProviderPresentation.cs` | contrato `IAgentSource` / `AgentStatus`, catálogo de provedores, ordem e visibilidade |
| `*Agent.cs`, `CodexClient.cs`, `ClaudeIntegration.cs`, `ClaudeQuota.cs`, `OpenRouterMcpClient.cs`, `OpenRouterTokenCache.cs`, `CreditUsage.cs` | leitura de cada ferramenta |
| `RenewalWatcher.cs` | avisos de renovação e `AppSettings` (`settings.json`) |
| `MonitorForm*.cs`, `SettingsForm.cs`, `LauncherForm.cs`, `DockForm.cs`, `NotchForm.cs`, `MiniScreen.cs`, `SerialMiniScreen.cs`, `UsbSerialScreen.cs` | janelas e superfícies |
| `SharedOfficeScene.cs`, `ClassicOffice.cs`, `PixelOffice.cs`, `OfficeViewport.cs`, `SkyService.cs`, `Theme.cs`, `PixelFont.cs`, `TrayIconRenderer.cs`, `LucideIcons.cs` | cena, céu, cores e desenho |
| `Lang.cs` | idioma da interface (português ou inglês) e links para esta documentação |
| `*Checks.cs`, `LoftReview.cs`, `MiniOfficePreview.cs` | autotestes e prévias |
| `build.ps1`, `install-shortcuts.ps1`, `claude-statusline.ps1` | build, atalhos e integração com o Claude Code |

## Ambientes

O app instalado é o seu QuotaDesk de uso diário: `%LOCALAPPDATA%\Programs\QuotaDesk`, com dados em `%LOCALAPPDATA%\AIUsageMonitor`. Para desenvolver sem arriscar esses dados, abra o código atual num ambiente isolado, que roda ao lado dele:

```powershell
scripts\env.ps1 dev                  # dados vazios, nada real
scripts\env.ps1 homolog              # cópia dos seus dados reais (sem os logins), para testar a versão candidata
scripts\env.ps1 homolog -FreshData   # recopia os dados pessoais
scripts\env.ps1 dev -Stop            # fecha o ambiente
```

Também pelo menu do `QuotaDesk.cmd`: `dev`, `homolog` e `parar`.

- Cada ambiente tem programa, pasta de dados (`AIUsageMonitor-dev`, `AIUsageMonitor-homolog`) e trava de instância única próprios, e aparece na bandeja como `QuotaDesk [dev]` ou `QuotaDesk [homolog]`.
- Eles nunca alteram a inicialização com o Windows.
- A cota do Claude vem do mesmo cache do statusLine, só para leitura. O estado ao vivo dos hooks continua com a versão instalada.
- Para qualquer build, `QUOTADESK_DATA_DIR=<pasta>` escolhe a pasta de dados, e `--test-instance` sozinho usa `AIUsageMonitor-Test`.

### Fluxo sugerido

`feature/*` → `scripts\env.ps1 dev` → branch `release/x.y` → alguns dias em `homolog` → merge na `main`, tag e Release → a versão pessoal se atualiza pelo `install.ps1`.

Branches de trabalho ficam locais. O que for particular (notas, rascunhos de arte) vai para um repositório privado, nunca para uma branch deste: uma branch enviada aqui fica pública.

## Compilar e testar

```powershell
scripts\setup.ps1 -NoInstall         # instala o SDK se faltar e compila em src\QuotaDesk\publish-staging
scripts\scan-leaks.ps1 -All          # mesma verificação de vazamentos do CI
```

Os autotestes usam dados simulados e preferências isoladas. Cada um grava um relatório; procure `FAIL`:

```powershell
QuotaDesk.exe --selftest-stability <pasta>
QuotaDesk.exe --selftest-office <pasta>
QuotaDesk.exe --selftest-office-navigation <pasta>
QuotaDesk.exe --selftest-claude <arquivo.txt>
QuotaDesk.exe --preview <pasta>      # painel, céus, barra na borda e bandeja com dados simulados
```

## Contribuir

Issues e pull requests são bem-vindos. Antes de abrir um PR, rode os autotestes e ative os hooks que barram chaves e dados pessoais:

```powershell
git config core.hooksPath .githooks
```

- Nunca anexe `settings.json`, arquivos `*.dat`, o `error.log` ou capturas com dados da sua conta.
- Nada de dados pessoais em arquivos versionados: em exemplos, use `C:\Users\<seu-usuario>` ou `%LOCALAPPDATA%`.
- Use o e-mail *noreply* do GitHub nos commits.

As regras completas estão no [SECURITY.md](https://github.com/srbunnie/QuotaDesk/blob/main/SECURITY.md#regras-do-reposit%C3%B3rio).

## Adicionar uma ferramenta

O passo a passo está na página **[Adicionar provedor](add-provider.md)**. O roteiro completo para agentes de código, com as regras do projeto e o esqueleto do adaptador, está no [AGENTS.md](https://github.com/srbunnie/QuotaDesk/blob/main/AGENTS.md#tutorial-adicionar-uma-ferramenta).

## Esta documentação

As páginas são escritas em Markdown em `docs/docs/md/` (português) e `docs/en/docs/md/` (inglês). Para gerar o HTML:

```powershell
python scripts/build_docs.py
```

O script usa só a biblioteca padrão do Python e gera páginas estáticas em `docs/docs/` e `docs/en/docs/`.

## Roteiro

Hoje o QuotaDesk roda só no Windows. As próximas versões levam o mesmo escritório para o Linux e o macOS: primeiro separando o núcleo sem WinForms, depois trocando o desenho para Skia. A etapa 1 não depende de Mac nem de Linux. Veja o [roteiro completo](https://github.com/srbunnie/QuotaDesk/blob/main/ROADMAP.md).
