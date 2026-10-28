# QuotaDesk

Monitor local de cotas e atividade de ferramentas de IA para Windows (C#, .NET 10, WinForms). Fica na bandeja e mostra, num escritório voxel animado, quanto resta de cada assinatura e o que cada ferramenta está fazendo.

Versão atual: **0.4.0**.

## O que mostra

| Ferramenta | Dados | Fonte |
|---|---|---|
| Codex | cotas de 5 horas e semanal, estado | `codex app-server` (`account/rateLimits/read`) e logs locais de sessão |
| Claude Code | cotas de 5 horas e semanal, estado em tempo real | statusLine do Claude Code, histórico do app Claude e hooks locais |
| Antigravity | cotas por grupo de modelos | servidor local do próprio Antigravity |
| Magnific (card Criativos) | saldo de créditos e ritmo de gasto | MCP oficial da Magnific, com login no navegador |
| OpenRouter | saldo de créditos e ritmo de gasto | MCP oficial da OpenRouter, com login no navegador |
| Figma | disponibilidade global do serviço | página pública de status |

Cada card mostra a barra de uso, o ritmo (quanto você está acima ou abaixo de um gasto uniforme até a renovação), a previsão de quando a cota acaba e o horário de renovação. `~` indica valor estimado; leituras antigas são marcadas como desatualizadas, nunca apresentadas como atuais.

## Onde aparece

- **Bandeja:** ícone com um medidor por ferramenta e tooltip com o que resta.
- **Painel:** cards e o **Loft**, um escritório em salas, uma por provedor, com personagens que digitam, esperam, tomam café ou dormem conforme o estado real. As janelas mostram o céu e o clima da sua cidade. Em ⋯ › Aparência › Template das cotas dá para trocar pelo escritório **Clássico**.
- **Widget de borda:** trilho minimalista (notch) ou faixa clássica em qualquer borda de qualquer monitor, com balão de previsão ao passar o mouse.
- **Central de controle:** ocultar e reordenar provedores, densidade de cada card, posição do widget e inicialização com o Windows.
- **Mini monitor:** tela cheia numa tela secundária de 480×320 ou 320×480, ou numa tela USB-C de 3,5" que aparece como porta serial (Turing Smart Screen e clones).
- **Avisos:** notificação do Windows quando uma cota renova e quando uma sessão do Claude Code fica esperando por você.

## Instalar

Requer Windows 10/11 e o [SDK do .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File src/QuotaDesk/build.ps1
```

O script publica a versão self-contained, confere se o Windows permite executar o binário novo (Smart App Control ou antivírus podem bloquear; nesse caso ele para sem mexer em nada), guarda a instalação anterior em `~/QuotaDesk-archive/installations/`, instala em `%LOCALAPPDATA%/Programs/QuotaDesk`, cria atalhos no Menu Iniciar e na Área de Trabalho e abre o painel.

| Parâmetro | Efeito |
|---|---|
| `-NoInstall` | só publica e confere |
| `-Package` | também gera `dist/QuotaDesk-<versão>-win-x64.zip`, que roda em outra máquina sem instalar o .NET |
| `-Dotnet <caminho>` | usa outro `dotnet.exe` (ou defina `QUOTADESK_DOTNET`) |

## Configurar cada ferramenta

- **Codex:** basta ter o Codex CLI ou o app desktop instalado e logado. O executável é procurado em `AI_MONITOR_CODEX_EXE`, no `PATH` e na pasta do app.
- **Claude Code:** a cota exata vem do statusLine, que só roda no Claude Code de terminal. Mescle `src/QuotaDesk/claude-settings-additions.template.json` no seu `%USERPROFILE%\.claude\settings.json` (faça backup e troque `{{PASTA_DO_PROJETO}}` pelo caminho de `src/QuotaDesk`). Isso liga o statusLine e os hooks que dão o estado em tempo real. Para manter a cota atualizada mesmo usando o app Claude, ative ⋯ › Atualização › **Abrir Claude Code no terminal ao iniciar**.
- **Antigravity:** abra o Antigravity; a cota aparece sozinha enquanto ele está aberto.
- **Magnific e OpenRouter:** clique em **Conectar** no menu da bandeja ou na central e faça o login no navegador. **Trocar conta…** no card esquece o login atual.
- **Clima (opcional):** menu da bandeja › Cidade do clima.

## Privacidade e segurança

- Tudo roda localmente. Não envia prompts, não chama modelos e não lê credenciais do Claude nem do Codex.
- O receptor de hooks escuta só em `127.0.0.1:32871` e recusa requisições vindas de páginas web. Do corpo dos hooks usa apenas o id da sessão, o nome da ferramenta, o tipo de notificação e o nome da pasta; nada é gravado.
- O token de sessão do Antigravity fica só em memória. Os logins da Magnific e da OpenRouter são guardados com DPAPI (só o seu usuário do Windows consegue lê-los).
- A rede é usada apenas para os MCPs da Magnific e da OpenRouter, a página de status do Figma e o clima opcional do Open-Meteo (que recebe só latitude e longitude).
- Preferências, caches e o log de erros ficam em `%LOCALAPPDATA%/AIUsageMonitor`, fora do repositório.

## Estrutura do código

Tudo fica em `src/QuotaDesk` (projeto `AIUsageMonitor.csproj`, assembly `QuotaDesk`). A única dependência NuGet além do .NET é `ModelContextProtocol.Core`, mais `System.IO.Ports` para a tela serial.

| Arquivos | Papel |
|---|---|
| `Program.cs`, `MonitorContext.cs` | entrada, instância única, bandeja, timers e ciclo de vida |
| `Models.cs`, `ProviderModule.cs`, `ProviderCategories.cs`, `ProviderPresentation.cs` | contrato `IAgentSource` / `AgentStatus`, catálogo de provedores, ordem e visibilidade |
| `CodexAgent.cs`, `CodexClient.cs`, `ClaudeIntegration.cs`, `ClaudeQuota.cs`, `AntigravityAgent.cs`, `FigmaAgent.cs`, `MagnificAgent.cs`, `OpenRouterAgent.cs`, `OpenRouterMcpClient.cs`, `OpenRouterTokenCache.cs`, `CreditUsage.cs` | leitura de cada ferramenta |
| `RenewalWatcher.cs` | avisos de renovação e `AppSettings` (`settings.json`) |
| `MonitorForm*.cs`, `SettingsForm.cs`, `LauncherForm.cs`, `DockForm.cs`, `NotchForm.cs`, `MiniScreen.cs`, `SerialMiniScreen.cs`, `UsbSerialScreen.cs` | janelas e superfícies |
| `SharedOfficeScene.cs`, `ClassicOffice.cs`, `PixelOffice.cs`, `OfficeViewport.cs`, `SkyService.cs`, `Theme.cs`, `PixelFont.cs`, `TrayIconRenderer.cs`, `LucideIcons.cs` | cena, céu, cores e desenho |
| `*Checks.cs`, `LoftReview.cs`, `MiniOfficePreview.cs` | autotestes e prévias (`--selftest-*`, `--preview`) |
| `build.ps1`, `install-shortcuts.ps1`, `claude-statusline.ps1`, `claude-settings-additions.template.json` | build, instalação e integração com o Claude Code |
| `assets/` | ícone, logos, fundo do Loft e manifesto, poses dos personagens e ícones Lucide (licença em `assets/lucide/LICENSE.txt`) |

Para adicionar uma ferramenta: implemente `IAgentSource` devolvendo um `AgentStatus`, registre-a no `ProviderModuleCatalog` e defina as cores em `Theme`.

## Autotestes

Rodam sem tocar nas suas preferências e usam dados simulados:

```powershell
QuotaDesk.exe --selftest-stability <pasta>
QuotaDesk.exe --selftest-office <pasta>
QuotaDesk.exe --selftest-office-navigation <pasta>
QuotaDesk.exe --selftest-claude <arquivo.txt>
QuotaDesk.exe --selftest-antigravity <arquivo.txt>
QuotaDesk.exe --selftest-renewal <arquivo.txt>
```

`--snapshot <arquivo.png>` renderiza o painel com os dados reais da máquina, e `--test-instance` roda uma segunda cópia ao lado da instalada.

## Contribuir

Issues e pull requests são bem-vindos. Antes de abrir um PR, rode os autotestes acima. Nunca anexe `settings.json`, arquivos `*.dat`, o `error.log` ou capturas com dados da sua conta. Para falhas de segurança, siga o [SECURITY.md](SECURITY.md).

## Licença

Código sob a licença [MIT](LICENSE). Os ícones em `assets/lucide` são do [Lucide](https://lucide.dev) (licença ISC, em `assets/lucide/LICENSE.txt`). Os nomes e logos de Claude, Codex/ChatGPT, Antigravity, Figma, Magnific e OpenRouter são marcas de seus donos e aparecem só para identificar cada serviço; o QuotaDesk não é afiliado a nenhum deles.
