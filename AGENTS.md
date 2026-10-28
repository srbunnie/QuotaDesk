# QuotaDesk — guia para agentes

Este arquivo é para agentes de código (Claude Code, Codex, Cursor…) que trabalham neste repositório. Ele explica como o projeto funciona, as regras que não podem ser quebradas e, passo a passo, como adicionar uma ferramenta nova ao QuotaDesk.

## O projeto em uma tela

- App de bandeja para Windows em C# .NET 10 WinForms. Fica em `src/QuotaDesk`: projeto `AIUsageMonitor.csproj`, namespace `AIUsageMonitor`, assembly `QuotaDesk`.
- Cada ferramenta monitorada é um **provedor**, composto por três partes:
  - um adaptador `IAgentSource`, que lê cota e atividade;
  - uma sala no Loft voxel;
  - um personagem com 12 poses.
- Interface e documentação em **português do Brasil**. Comentários de código em inglês, curtos, explicando o porquê.
- O usuário conecta cada ferramenta seguindo [`tutoriais/conectar-ferramentas.md`](tutoriais/conectar-ferramentas.md). Atualize esse tutorial sempre que mudar como uma ferramenta se conecta.

## Comandos

```powershell
scripts\setup.ps1 -NoInstall                          # instala o SDK se faltar e compila em src\QuotaDesk\publish-staging
scripts\env.ps1 dev                                   # abre o build atual isolado, com dados vazios
scripts\env.ps1 dev -Stop
scripts\scan-leaks.ps1 -All                           # mesma verificação do CI
python skills\add-provider\scripts\validate_assets.py
```

Autotestes, no executável de `src\QuotaDesk\publish-staging`. Cada um grava um relatório; procure `FAIL`:

```powershell
QuotaDesk.exe --selftest-stability <pasta>
QuotaDesk.exe --selftest-office <pasta>
QuotaDesk.exe --selftest-office-navigation <pasta>
QuotaDesk.exe --selftest-providers <pasta>\providers.txt
QuotaDesk.exe --selftest-claude <arquivo.txt>
QuotaDesk.exe --selftest-renewal <arquivo.txt>
QuotaDesk.exe --selftest-antigravity <arquivo.txt>
QuotaDesk.exe --preview <pasta>                       # todas as telas com dados simulados
```

`--selftest-ui` já falha no código atual (`UiChecks.cs`). Não é regressão sua.

## Regras

**Segurança e privacidade**

- Nunca versione chaves, tokens, `.env`, `settings.json`, `*.dat`, logs ou capturas com dados de conta. Os hooks em `.githooks` e o CI `leak-scan` bloqueiam isso. Nunca contorne com `--no-verify` nem marque uma chave real com `leak-scan:allow`.
- Nada de dados pessoais em arquivos versionados: nomes, e-mails, `C:\Users\<nome>`. Em exemplos, use `<seu-usuario>` ou `%LOCALAPPDATA%`.
- Commits com o e-mail *noreply* do GitHub.

**Adaptadores**

- Só leem: não enviam prompts, não chamam modelos e não leem credenciais de outras ferramentas.
- Gravam só em `AppPaths.DataDirectory`. Nunca monte o caminho `%LOCALAPPDATA%\AIUsageMonitor` à mão: em desenvolvimento e homologação a pasta é outra.
- Segredos ficam na memória ou com DPAPI (veja `OpenRouterTokenCache`). O login OAuth só abre o navegador por ação do usuário.
- Toda chamada de rede tem timeout e cancelamento, e atualizações não se sobrepõem (`SemaphoreSlim`).

**Honestidade dos números**

- Nunca invente um número. Leitura que falhou vira a última leitura com `Stale = true`, ou `ProviderSnapshot.Unavailable` com um motivo curto em português.
- Valores estimados levam `ResetEstimated = true`. A interface os mostra com `~`.

**Arquivos**

- `*.ps1` lidos pelo Windows PowerShell 5.1 precisam de UTF-8 **com BOM**.
- `install.ps1` fica **sem** BOM: ele roda por `irm | iex`.
- `*.cmd` em ASCII com CRLF.
- O `.gitattributes` cuida das quebras de linha no Git.

**Fluxo de trabalho**

- Trabalhe numa branch `feature/<id>` e verifique no ambiente `dev`.
- Branches de trabalho não são enviadas ao GitHub: só a `main`, por PR ou pelo mantenedor.
- A arte é gerada por IA, nunca desenhada por código (veja a skill).

---

## Tutorial: adicionar uma ferramenta

A referência completa é a skill [`skills/add-provider/SKILL.md`](skills/add-provider/SKILL.md). Ela traz os prompts de arte, as regras de chroma-key e os scripts. Leia a skill antes de começar. Este tutorial é o roteiro, com exemplos de código.

Mostre ao usuário este plano e marque cada etapa quando o arquivo existir e a verificação passar:

1. Descobrir de onde vem a cota
2. Escrever o adaptador
3. Registrar o provedor
4. Criar a sala
5. Criar o personagem
6. Validar e documentar

Sem um gerador de imagens disponível, faça só as etapas 1, 2, 3 e 6. Isso é o modo **só adaptador**: o card, a bandeja, o widget e o mini monitor funcionam, e o Loft mostra só os provedores que têm sala. Diga isso no PR.

### 1. Descobrir de onde vem a cota

Antes de escrever código, descubra como a ferramenta expõe uso e limites, nesta ordem de preferência. Copie o padrão do arquivo indicado:

| Fonte | Exemplo no código |
|---|---|
| CLI local com servidor JSON-RPC | `CodexClient.cs`: inicia o processo, aplica timeout, encerra a árvore de processos e drena o stderr |
| Arquivo local escrito pela ferramenta | `ClaudeQuota.cs`: leitura com `FileShare.ReadWrite \| FileShare.Delete` e detecção de dado vencido |
| Servidor local da ferramenta | `AntigravityAgent.cs`: só loopback, token em memória, backoff na descoberta |
| MCP remoto com OAuth | `MagnificAgent.cs`, `OpenRouterAgent.cs`, `OpenRouterMcpClient.cs`, `OpenRouterTokenCache.cs` (DPAPI) |
| Página pública de status | `FigmaAgent.cs`: sem login, no máximo de hora em hora |
| Saldo de créditos com ritmo de gasto | `CreditUsage.cs` |

Se nada público documenta a cota, pare e pergunte ao usuário. Não faça engenharia reversa de credenciais nem leia o banco de dados de outra ferramenta.

### 2. Escrever o adaptador

Crie `src/QuotaDesk/<Nome>Agent.cs`. O contrato, em `Models.cs`:

```csharp
internal interface IAgentSource : IDisposable
{
    string Id { get; }
    event Action? Changed;
    AgentStatus GetStatus(DateTimeOffset now);
    Task RefreshQuotaAsync(bool force);
    void SetForeground(bool visible) { }   // opcional: atualizar mais rápido com o painel aberto
}
```

Esqueleto de um adaptador que lê um arquivo local. Troque `exemplo` e `Exemplo` pelo id e pelo nome reais:

```csharp
using System.Text.Json;

namespace AIUsageMonitor;

/// <summary>Reads Exemplo's quota from the usage file the tool writes locally. Read-only; nothing leaves the PC.</summary>
internal sealed class ExemploAgent : IAgentSource
{
    private const string Name = "Exemplo";
    private const string SourceName = "arquivo local do Exemplo";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ProviderSnapshot _quota = ProviderSnapshot.Unavailable(Name, "Abra o Exemplo para consultar a cota.", SourceName);

    public string Id => "exemplo";
    public event Action? Changed;

    public AgentStatus GetStatus(DateTimeOffset now) =>
        new(Id, Name, ActivityState.Unknown, true, "sem sinal de atividade", SourceName, _quota);

    public async Task RefreshQuotaAsync(bool force)
    {
        if (!await _gate.WaitAsync(0)) return; // a refresh is already running
        try { _quota = await ReadAsync(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Keep the last real reading, marked old, instead of inventing or dropping it.
            _quota = _quota.Windows.Count > 0 ? _quota with { Stale = true }
                : ProviderSnapshot.Unavailable(Name, "Não foi possível ler a cota do Exemplo.", SourceName);
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    private static async Task<ProviderSnapshot> ReadAsync()
    {
        // Parse the tool's data into LimitWindow(label, usedPercent, resetsAt, kind, resetEstimated, resetHint).
        // WindowKind.FiveHour / Weekly only when the tool really has those windows; everything else is Other.
        await Task.CompletedTask;
        throw new NotImplementedException();
    }

    public void Dispose() => _gate.Dispose();
}
```

Para uma ferramenta de créditos pré-pagos, preencha `LimitWindow.Amount` e `Credits` e use `CreditUsage` para o ritmo de gasto, como em `OpenRouterAgent.cs`.

### 3. Registrar o provedor

Siga [`skills/add-provider/references/code-touchpoints.md`](skills/add-provider/references/code-touchpoints.md). O mínimo:

1. Uma entrada `Module("exemplo", "Exemplo", new(cores…), anchorX, headX, headY, () => new ExemploAgent(), quotaScreen: …, logoPlaque: …)` em `ProviderModuleCatalog.All` (`ProviderModule.cs`). Copie os valores de um vizinho; `anchorX` vem do passo 5.
2. O logo `assets/exemplo-logo.png`, quadrado e transparente, com `<EmbeddedResource Include="assets\exemplo-logo.png" />` no `.csproj`, mais o caso em `LogoFor` (`MonitorForm.cs`).
3. O novo id nas listas esperadas de `SharedOfficeChecks.cs`, `ProviderAvailabilityChecks.cs` e `OfficeNavigationChecks.cs`.
4. Se o login é pelo navegador: os itens **Conectar** e **Trocar conta…** em `MonitorContext.cs` e `MonitorForm.Panel.cs`, iguais aos da Magnific.

Compile e abra no ambiente isolado. O card novo deve aparecer com dados reais ou com um motivo honesto:

```powershell
scripts\env.ps1 dev
```

### 4. Criar a sala

Siga a seção *Room* da skill:

1. Gere a sala em 1452×896 com [`references/room-prompt.md`](skills/add-provider/references/room-prompt.md), passando como referência uma sala existente recortada do `voxel-loft.png`. A janela é `#00FF00` chapado e a tela do monitor é `#FF00FF` chapado. Sem pessoas, cadeiras, texto ou logos. Gere dois candidatos e escolha um.
2. Corrija detalhes com retoque, não gerando tudo de novo.
3. Encaixe a sala no Loft:

```bash
python skills/add-provider/scripts/add_room.py --id exemplo --room exemplo-room.png --seat 182 --preview loft-preview.png
```

4. Copie a ordem que o script imprime para `SharedOfficeScene.Order` e `OfficeViewport.PhysicalOrder`.
5. Opcional: adicione um `case "exemplo":` na animação de tela em `SharedOfficeScene.cs`.

### 5. Criar o personagem

1. Gere duas folhas 3×2 do mesmo personagem na mesma cadeira com [`references/character-prompt.md`](skills/add-provider/references/character-prompt.md), usando `docs/assets/characters.png` como referência de estilo.
2. Recorte e alinhe:

```bash
python skills/add-provider/scripts/cut_poses.py --id exemplo --sheet exemplo-sheet.png --sheet2 exemplo-sheet2.png
```

3. Use o `wheelAnchorX` impresso como `anchorX` no `Module(...)`.

### 6. Validar e documentar

```powershell
python skills\add-provider\scripts\validate_assets.py
scripts\setup.ps1 -NoInstall
src\QuotaDesk\publish-staging\QuotaDesk.exe --selftest-office out
src\QuotaDesk\publish-staging\QuotaDesk.exe --selftest-office-navigation out
src\QuotaDesk\publish-staging\QuotaDesk.exe --selftest-providers out\providers.txt
scripts\scan-leaks.ps1 -All
```

Abra as imagens de `--selftest-office` da sala nova, não só os relatórios. Depois:

- uma seção nova em [`tutoriais/conectar-ferramentas.md`](tutoriais/conectar-ferramentas.md): o que mostra, passo a passo, de onde vem e mensagens de erro;
- uma linha nova na tabela *O que acompanha* do `README.md`;
- no PR: a fonte da cota, o gerador e o modelo usados na arte, e se ficou só adaptador.

**Pronto quando:**

- `validate_assets.py` imprime OK;
- o build passa e os autotestes não têm `FAIL`;
- o card mostra dado real ou um estado honesto;
- `scan-leaks.ps1 -All` passa;
- o tutorial de conexão tem a ferramenta nova.
