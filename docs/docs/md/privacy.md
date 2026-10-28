# Privacidade e segurança

Tudo roda na sua máquina: o QuotaDesk não envia prompts, não chama modelos e não lê credenciais de outras ferramentas.

## O que o QuotaDesk lê

- **Codex:** os limites da conta pelo `codex app-server` e os tipos de evento dos logs de sessão. O conteúdo das mensagens não é lido.
- **Claude Code:** o cache do statusLine, o histórico do app Claude e os hooks. Dos hooks usa só o id da sessão, o nome da ferramenta, o tipo de notificação e o nome da pasta; nada é gravado.
- **Antigravity:** o servidor local, só pelo `127.0.0.1`. O token dessa sessão local fica só em memória.
- **Magnific e OpenRouter:** só ferramentas de leitura de saldo dos MCPs oficiais.
- **Figma:** a página pública de status.

As **falas reais dos agentes** (o balão com a tarefa e a última ação) são montadas só com nomes de eventos e ferramentas, nunca com o conteúdo dos prompts. Desligue em **Configurações › Avisos › Falas reais dos agentes**.

## Rede

A rede é usada apenas para:

- os MCPs da Magnific (`mcp.magnific.com`) e da OpenRouter (`mcp.openrouter.ai`);
- a página de status do Figma;
- o clima opcional do Open-Meteo, que recebe só latitude e longitude.

O receptor de hooks escuta só em `127.0.0.1:32871` e recusa requisições vindas de páginas web.

## Dados guardados

Tudo fica em `%LOCALAPPDATA%\AIUsageMonitor` (ou na pasta do ambiente, veja [Ambientes](develop.md#ambientes)):

| Arquivo | O que é |
|---|---|
| `settings.json` | preferências |
| `*.dat` | logins da Magnific e da OpenRouter, protegidos com DPAPI (só o seu usuário do Windows consegue lê-los) |
| `*-usage.json` | histórico de créditos, para calcular o ritmo de gasto |
| `claude-limits.json` | cache da cota do Claude escrito pelo statusLine |
| `error.log` | log de erros; mascara tokens, códigos OAuth e a pasta do usuário |

## Desconectar tudo

1. Feche o QuotaDesk.
2. Apague os arquivos `*.dat` em `%LOCALAPPDATA%\AIUsageMonitor`.
3. Para revogar o acesso também do lado do serviço, remova a autorização no site da Magnific ou da OpenRouter.

## Ao pedir ajuda

Nunca anexe `settings.json`, arquivos `*.dat`, o `error.log` nem capturas com tokens, e-mails ou caminhos pessoais em issues e pull requests.

## Reportar uma vulnerabilidade

Não abra uma issue pública. Use **Security › Report a vulnerability** no repositório (aviso privado do GitHub), descrevendo o problema, como reproduzir e o impacto. Os detalhes e as regras do repositório estão no [SECURITY.md](https://github.com/srbunnie/QuotaDesk/blob/main/SECURITY.md).
