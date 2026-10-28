# Conectar cada ferramenta

**Português** · [English](../tutorials/connect-tools.md)

O QuotaDesk lê o que cada ferramenta já guarda ou expõe na sua máquina. Ele não pede chave de API e não lê credenciais de outras ferramentas. Três delas funcionam sozinhas: Codex, Antigravity e Figma. O Claude Code precisa de uma configuração única. Magnific e OpenRouter precisam de um login no navegador.

| Ferramenta | O que você faz | Tempo |
|---|---|---|
| [Codex](#codex) | Ter o Codex instalado e logado | — |
| [Claude Code](#claude-code) | Colar um trecho no `settings.json` do Claude Code | 5 min |
| [Antigravity](#antigravity) | Deixar o Antigravity aberto | — |
| [Magnific](#magnific) | **Conectar Magnific** e fazer login no navegador | 1 min |
| [OpenRouter](#openrouter) | **Conectar OpenRouter** e fazer login no navegador | 1 min |
| [Figma](#figma) | Nada | — |

Para esconder uma ferramenta que você não usa: **Configurações › Ferramentas**, desmarque o nome.

Onde ficam as ações citadas abaixo:

- **Menu da bandeja:** clique com o botão direito no ícone do QuotaDesk perto do relógio.
- **Central:** a janela de início, com os botões *Abrir painel* e *Conectar*.
- **Menu do card:** o botão de menu no canto de cada card do painel.
- **Configurações:** o botão de configurações no topo do painel.

---

## Codex

**Mostra:** cotas de 5 horas e semanal do seu plano do ChatGPT, e se o Codex está trabalhando.

1. Instale o Codex: o CLI (`npm i -g @openai/codex`) ou o app desktop.
2. Faça login com a sua conta do ChatGPT (`codex login` no terminal, ou pelo app).
3. Abra o QuotaDesk. O card do Codex aparece sozinho.

**De onde vem:** o QuotaDesk inicia `codex app-server` em segundo plano e pede só os limites da conta. A atividade vem dos tipos de evento nos logs de sessão em `%USERPROFILE%\.codex\sessions` (comando, edição, raciocínio), e o título que aparece no balão do personagem é o nome da conversa no índice do próprio Codex. O conteúdo das mensagens não é lido, e nada é enviado ao modelo.

**Se não aparecer:**

| Mensagem no card | O que fazer |
|---|---|
| *Codex CLI não encontrado.* | O QuotaDesk procura `codex.exe` no `PATH` e em `%LOCALAPPDATA%\OpenAI\Codex\bin`. Se estiver em outro lugar, crie a variável de ambiente `AI_MONITOR_CODEX_EXE` com o caminho completo do `codex.exe` e reabra o QuotaDesk. |
| *Nenhuma janela de cota disponível.* | Com login por chave de API não há cota de 5 horas ou semanal para mostrar. Entre com a conta do ChatGPT. |
| *Não foi possível ler os limites do Codex.* | Rode `codex` uma vez no terminal para conferir se o login está válido, depois **Atualizar consumo** no menu do card. |

---

## Claude Code

**Mostra:** cotas de 5 horas e semanal (exatas), o estado em tempo real de cada sessão e os pedidos de permissão ou perguntas esperando por você.

O Claude Code tem três fontes, da mais para a menos precisa:

1. **statusLine do Claude Code no terminal:** porcentagens e horários de renovação exatos.
2. **Histórico do app Claude (desktop):** amostras a cada ~15 min. O QuotaDesk usa sozinho quando não há statusLine e marca os valores com `~` (estimado).
3. **Hooks:** dizem em tempo real se a sessão está trabalhando, esperando você ou parada.

O passo a passo abaixo liga a 1 e a 3.

### 1. Ache o script do statusLine

- **Instalado pela Release:** `%LOCALAPPDATA%\Programs\QuotaDesk\claude-statusline.ps1`. Ele vem no pacote a partir da versão seguinte à 0.4.0. Na 0.4.0, use a cópia do repositório.
- **A partir do repositório:** `src\QuotaDesk\claude-statusline.ps1`.

Anote a pasta, por exemplo `C:\Users\<seu-usuario>\AppData\Local\Programs\QuotaDesk`.

### 2. Edite o `settings.json` do Claude Code

1. Feche o Claude Code.
2. Faça uma cópia de `%USERPROFILE%\.claude\settings.json`. Se ele não existir, crie um arquivo com `{}`.
3. Abra `claude-settings-additions.template.json`, que fica ao lado do script, e copie as chaves `statusLine` e `hooks` para dentro do seu `settings.json`. Se você já tem `hooks`, junte os eventos em vez de substituir.
4. Troque `{{PASTA_DO_PROJETO}}` pela pasta do passo 1, com as barras dobradas:

```json
"statusLine": {
  "type": "command",
  "command": "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"C:\\Users\\<seu-usuario>\\AppData\\Local\\Programs\\QuotaDesk\\claude-statusline.ps1\""
}
```

O Claude Code aceita um único `statusLine`. Se você já usa outro, escolha um dos dois.

### 3. Confira

1. Com o QuotaDesk aberto, abra o Claude Code **no terminal** e mande qualquer mensagem.
2. O card do Claude passa a mostrar as cotas sem `~`, e aparece o arquivo `%LOCALAPPDATA%\AIUsageMonitor\claude-limits.json`.
3. Enquanto o Claude trabalha, o personagem digita. Quando ele pede permissão, o card ganha um selo e o Windows mostra uma notificação.

### Manter a cota atualizada sem abrir o terminal

A aba *Code* do app Claude e o chat não rodam o statusLine. Quando quiser a cota exata, use o menu do card do Claude › **Abrir Claude Code no terminal**: uma mensagem qualquer já atualiza a cota.

**Se não aparecer:**

| Sintoma | O que fazer |
|---|---|
| *Sem dados de cota.* | Use o Claude Code no terminal uma vez (statusLine) ou abra o app Claude (histórico estimado). |
| Cotas com `~` e *Dados do statusLine vencidos.* | O statusLine não roda há mais de 30 min. Abra o Claude Code no terminal (menu do card › *Abrir Claude Code no terminal*). |
| *Porta local 32871 ocupada* | Outro programa usa a porta dos hooks, ou há duas cópias do QuotaDesk instaladas. Feche a outra e reabra. |
| Estado nunca muda (*nenhum evento recebido ainda*) | Os hooks não estão no `settings.json` certo ou o Claude Code foi aberto antes da edição. Reabra o Claude Code. |

**Privacidade:** dos hooks o QuotaDesk usa só o id da sessão, o nome da ferramenta, o tipo de notificação e o nome da pasta. Prompts e respostas não são lidos nem gravados. As falas reais do personagem ("Editando um arquivo") saem só do nome da ferramenta; desligue em **Configurações › Avisos › Falas reais dos agentes**.

---

## Antigravity

**Mostra:** cota por grupo de modelos (por exemplo Gemini, e Claude e GPT).

1. Abra o Google Antigravity e entre na sua conta.
2. Pronto. O QuotaDesk encontra o servidor local do Antigravity e lê a cota a cada 30 segundos.

**De onde vem:** o QuotaDesk só aceita o processo `language_server` da pasta de instalação do Antigravity, e só conversa com ele pelo `127.0.0.1`. O token dessa sessão local fica na memória e nunca é gravado.

**Se não aparecer:** *Abra o Antigravity para consultar a cota.* significa que ele está fechado ou ainda iniciando. Com o Antigravity fechado, o card mostra a última leitura marcada como antiga.

---

## Magnific

**Mostra:** saldo de créditos, quanto você gasta por dia e quando o saldo acaba nesse ritmo. Aparece no card **Criativos**.

1. Clique em **Conectar Magnific**: no menu da bandeja, na central ou no menu do card (`Conectar Magnific…`).
2. O navegador abre o login oficial da Magnific. Entre e autorize o acesso.
3. Volte ao QuotaDesk. O saldo aparece em alguns segundos.

**De onde vem:** o MCP oficial da Magnific (`mcp.magnific.com`), só com ferramentas de leitura de saldo. O login fica em `magnific-mcp-token.dat`, protegido com DPAPI: só o seu usuário do Windows nesta máquina consegue abri-lo.

**Trocar de conta:** menu do card › **Trocar conta…**. Se o navegador entrar direto na mesma conta, saia dela no site da Magnific antes.

**Pelo terminal:** `QuotaDesk.exe --connect-magnific` faz o login sem abrir o painel.

**Se não aparecer:**

| Mensagem | O que fazer |
|---|---|
| *Conecte ou reautorize Magnific no menu da bandeja.* | O login expirou ou foi revogado. Conecte de novo. |
| *Não foi possível consultar os créditos do Magnific; nova tentativa em 15 minutos.* | Falha de rede ou do serviço. O card mantém a última leitura marcada como antiga. |

---

## OpenRouter

**Mostra:** saldo em US$, gasto por dia e previsão de quando o saldo acaba.

1. Clique em **Conectar OpenRouter**: no menu da bandeja, na central ou no menu do card.
2. Entre na OpenRouter no navegador e autorize.
3. Volte ao QuotaDesk.

**De onde vem:** o MCP oficial da OpenRouter (`mcp.openrouter.ai`). O login fica em `openrouter-mcp-token.dat`, com DPAPI.

**Trocar de conta:** menu do card › **Trocar conta…**.

**Se não aparecer:** *Conecte ou reautorize OpenRouter no menu da bandeja.* pede um novo login. *Não foi possível consultar o saldo do OpenRouter* tenta de novo após 1 hora, mantendo a última leitura.

---

## Figma

**Mostra:** se o serviço **Figma Agent** está operacional, degradado ou fora do ar.

Não há nada a configurar. O QuotaDesk lê a página pública `status.figma.com` uma vez por hora. É o status global do serviço: não verifica os créditos nem as tarefas da sua conta.

---

## Extras

- **Clima real na janela:** menu da bandeja › **Clima real na janela (Open-Meteo)** e **Cidade do clima**. O Open-Meteo recebe só latitude e longitude.
- **Mini monitor:** menu da bandeja › **Mini monitor (tela secundária)**. Telas USB-C de 3,5" que aparecem como porta serial são detectadas sozinhas.

## Onde ficam os dados e como desconectar tudo

Tudo fica em `%LOCALAPPDATA%\AIUsageMonitor`: preferências (`settings.json`), logins (`*.dat`), histórico de créditos (`*-usage.json`) e o log de erros. Para desconectar Magnific e OpenRouter, feche o QuotaDesk e apague os arquivos `*.dat`. Para revogar o acesso também do lado do serviço, remova a autorização no site da Magnific ou da OpenRouter.

Nunca anexe esses arquivos em issues: veja o [SECURITY.md](../SECURITY.md).

Falta uma ferramenta? Veja [como adicionar uma nova](../AGENTS.md#tutorial-adicionar-uma-ferramenta).
