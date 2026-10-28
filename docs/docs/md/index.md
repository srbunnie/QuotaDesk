# Visão geral

O QuotaDesk é um app de bandeja para Windows que mostra quanto ainda dá para usar de cada IA, sem abrir nenhuma delas.

![Painel do QuotaDesk com Codex, Claude Code e Antigravity trabalhando (dados simulados)](https://srbunnie.github.io/QuotaDesk/assets/screens/panel-working.png)

Um escritório animado fica no canto da tela. Cada IA tem uma sala e um personagem que digita quando ela trabalha, pensa quando espera por você, toma café ou cochila quando está parada e procura sinal no celular quando não há leitura. Os cards mostram a cota de 5 horas e a semanal, o ritmo de gasto, quando a cota acaba nesse ritmo e quando renova.

## O que ele acompanha

| Ferramenta | Dados | De onde vem |
|---|---|---|
| **Codex** | cotas de 5 horas e semanal, estado | `codex app-server` e logs locais de sessão |
| **Claude Code** | cotas de 5 horas e semanal, estado em tempo real, pedidos de permissão | statusLine do Claude Code, histórico do app Claude e hooks locais |
| **Antigravity** | cotas por grupo de modelos | servidor local do próprio Antigravity |
| **Magnific** (card Criativos) | saldo de créditos e ritmo de gasto | MCP oficial da Magnific, com login no navegador |
| **OpenRouter** | saldo de créditos e ritmo de gasto | MCP oficial da OpenRouter, com login no navegador |
| **Figma** | disponibilidade global do serviço | página pública de status |

`~` marca valores estimados. Leituras antigas aparecem como desatualizadas e uma fonte que muda de formato vira "sem sinal": o QuotaDesk nunca inventa um número.

## Para que serve

- **Não estourar a cota no meio de uma tarefa.** A barra de cada card tem uma marca de ritmo. Se você gasta mais rápido do que a janela permite, o card avisa quando a cota acaba nesse ritmo, antes de renovar.
- **Saber quando um agente está esperando por você.** Quando uma sessão do Claude Code pede permissão ou faz uma pergunta, o personagem fica pensando, o card ganha um selo e o Windows mostra uma notificação.
- **Escolher a ferramenta certa para a próxima tarefa.** Todas as cotas ficam lado a lado.
- **Acompanhar créditos pré-pagos** da Magnific e da OpenRouter: saldo, gasto por dia e previsão de quando acaba.
- **Ver de relance, sem abrir nada**, pela barra na borda da tela e pelo ícone da bandeja.

## Comece por aqui

1. [Instale o QuotaDesk](install.md): um comando no PowerShell, sem administrador.
2. Conecte as ferramentas que você usa: [Codex](connect-codex.md), [Claude Code](connect-claude.md), [Antigravity](connect-antigravity.md), [Magnific](connect-magnific.md), [OpenRouter](connect-openrouter.md) e [Figma](connect-figma.md).
3. Aprenda a [usar o painel](use.md) e ajuste as [configurações](settings.md).

| Ferramenta | O que você faz | Tempo |
|---|---|---|
| [Codex](connect-codex.md) | Ter o Codex instalado e logado | — |
| [Claude Code](connect-claude.md) | Colar um trecho no `settings.json` do Claude Code | 5 min |
| [Antigravity](connect-antigravity.md) | Deixar o Antigravity aberto | — |
| [Magnific](connect-magnific.md) | **Conectar Magnific** e fazer login no navegador | 1 min |
| [OpenRouter](connect-openrouter.md) | **Conectar OpenRouter** e fazer login no navegador | 1 min |
| [Figma](connect-figma.md) | Nada | — |

## Onde ficam as ações citadas nesta documentação

- **Menu da bandeja:** clique com o botão direito no ícone do QuotaDesk perto do relógio. Ele tem **Ajuda e documentação**, que abre esta página.
- **Central:** a janela de início, com os botões *Abrir painel* e *Conectar*.
- **Menu do card:** o botão de menu no canto de cada card do painel.
- **Configurações:** o botão de configurações no topo do painel.

## Privacidade em uma frase

Tudo roda na sua máquina: o QuotaDesk não envia prompts, não chama modelos e não lê credenciais do Claude nem do Codex. Os detalhes estão em [Privacidade e segurança](privacy.md).

> Todas as imagens desta documentação usam **dados simulados**.
