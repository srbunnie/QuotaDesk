# Conectar o Codex

Basta ter o Codex instalado e logado com a conta do ChatGPT: o card aparece sozinho.

**Mostra:** cotas de 5 horas e semanal do seu plano do ChatGPT, e se o Codex está trabalhando.

## Passo a passo

1. Instale o Codex: o CLI (`npm i -g @openai/codex`) ou o app desktop.
2. Faça login com a sua conta do ChatGPT (`codex login` no terminal, ou pelo app).
3. Abra o QuotaDesk. O card do Codex aparece sozinho.

## De onde vêm os dados

O QuotaDesk inicia `codex app-server` em segundo plano e pede só os limites da conta (`account/rateLimits/read`). A atividade vem dos tipos de evento nos logs de sessão em `%USERPROFILE%\.codex\sessions` (comando, edição, raciocínio), e o título que aparece no balão do personagem é o nome da conversa no índice do próprio Codex.

O conteúdo das mensagens não é lido, e nada é enviado ao modelo.

## Se não aparecer

| Mensagem no card | O que fazer |
|---|---|
| *Codex CLI não encontrado.* | O QuotaDesk procura `codex.exe` no `PATH` e em `%LOCALAPPDATA%\OpenAI\Codex\bin`. Se estiver em outro lugar, crie a variável de ambiente `AI_MONITOR_CODEX_EXE` com o caminho completo do `codex.exe` e reabra o QuotaDesk. |
| *Nenhuma janela de cota disponível.* | Com login por chave de API não há cota de 5 horas ou semanal para mostrar. Entre com a conta do ChatGPT. |
| *Não foi possível ler os limites do Codex.* | Rode `codex` uma vez no terminal para conferir se o login está válido, depois **Atualizar consumo** no menu do card. |

Para esconder o Codex se você não usa: **Configurações › Ferramentas**, desmarque o nome.
