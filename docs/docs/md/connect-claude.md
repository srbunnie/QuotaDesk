# Conectar o Claude Code

Uma configuração única no `settings.json` do Claude Code liga a cota exata e o estado em tempo real de cada sessão.

**Mostra:** cotas de 5 horas e semanal (exatas), o estado em tempo real de cada sessão e os pedidos de permissão ou perguntas esperando por você.

## As três fontes

O Claude Code tem três fontes, da mais para a menos precisa:

1. **statusLine do Claude Code no terminal:** porcentagens e horários de renovação exatos.
2. **Histórico do app Claude (desktop):** amostras a cada ~15 min. O QuotaDesk usa sozinho quando não há statusLine e marca os valores com `~` (estimado).
3. **Hooks:** dizem em tempo real se a sessão está trabalhando, esperando você ou parada.

O passo a passo abaixo liga a 1 e a 3.

## 1. Ache o script do statusLine

- **Instalado pela Release:** `%LOCALAPPDATA%\Programs\QuotaDesk\claude-statusline.ps1`. Ele vem no pacote a partir da versão seguinte à 0.4.0. Na 0.4.0, use a cópia do repositório.
- **A partir do repositório:** `src\QuotaDesk\claude-statusline.ps1`.

Anote a pasta, por exemplo `C:\Users\<seu-usuario>\AppData\Local\Programs\QuotaDesk`.

## 2. Edite o settings.json do Claude Code

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

## 3. Confira

1. Com o QuotaDesk aberto, abra o Claude Code **no terminal** e mande qualquer mensagem.
2. O card do Claude passa a mostrar as cotas sem `~`, e aparece o arquivo `%LOCALAPPDATA%\AIUsageMonitor\claude-limits.json`.
3. Enquanto o Claude trabalha, o personagem digita. Quando ele pede permissão, o card ganha um selo e o Windows mostra a notificação *Aguardando você*; clique nela para ver a lista de pendências.

## Atualizar a cota sem abrir o terminal

A aba *Code* do app Claude e o chat não rodam o statusLine. Quando quiser a cota exata, use o menu do card do Claude › **Abrir Claude Code no terminal**: uma mensagem qualquer já atualiza a cota.

## Se não aparecer

| Sintoma | O que fazer |
|---|---|
| *Sem dados de cota.* | Use o Claude Code no terminal uma vez (statusLine) ou abra o app Claude (histórico estimado). |
| Cotas com `~` e *Dados do statusLine vencidos.* | O statusLine não roda há mais de 30 min. Abra o Claude Code no terminal (menu do card › **Abrir Claude Code no terminal**). |
| *Porta local 32871 ocupada* | Outro programa usa a porta dos hooks, ou há duas cópias do QuotaDesk instaladas. Feche a outra e reabra. |
| Estado nunca muda (*nenhum evento recebido ainda*) | Os hooks não estão no `settings.json` certo ou o Claude Code foi aberto antes da edição. Reabra o Claude Code. |

## Privacidade

Dos hooks o QuotaDesk usa só o id da sessão, o nome da ferramenta, o tipo de notificação e o nome da pasta. Prompts e respostas não são lidos nem gravados. As falas reais do personagem ("Editando um arquivo") saem só do nome da ferramenta; desligue em **Configurações › Avisos › Falas reais dos agentes**.
