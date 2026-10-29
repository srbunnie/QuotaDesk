# Adicionar provedor

Como pôr uma ferramenta de IA nova no QuotaDesk: o que é preciso, o passo a passo e o que aparece enquanto ela ainda não tem arte.

## Não programa?

Abra uma [issue no GitHub](https://github.com/srbunnie/QuotaDesk/issues) com o nome da ferramenta e um link de onde ela mostra o uso ou os limites (uma página da conta, um comando, uma API). Isso basta para alguém do projeto avaliar.

## Passo a passo

O roteiro completo, com as regras do projeto e o esqueleto do adaptador, está no **[AGENTS.md](https://github.com/srbunnie/QuotaDesk/blob/main/AGENTS.md#tutorial-adicionar-uma-ferramenta)**. Codex, Cursor e outros agentes de código leem esse arquivo sozinhos; o Claude Code o recebe pelo `CLAUDE.md`.

Cada ferramenta é um **provedor** com três partes: um adaptador `IAgentSource`, que lê cota e atividade; uma sala no escritório; e um personagem com 12 poses. As etapas:

1. Descobrir de onde vem a cota.
2. Escrever o adaptador.
3. Registrar o provedor.
4. Criar a sala.
5. Criar o personagem.
6. Validar e documentar.

Sem um gerador de imagens, faça só as etapas 1, 2, 3 e 6 (modo **só adaptador**). O card, a bandeja e o mini monitor funcionam normalmente, e o escritório põe a ferramenta numa **sala provisória** com um personagem emprestado (hoje, os do OpenRouter). A arte própria pode vir depois, em outro PR.

A skill [`skills/add-provider`](https://github.com/srbunnie/QuotaDesk/blob/main/skills/add-provider/SKILL.md) ensina um agente a fazer tudo de ponta a ponta, com os prompts de arte, as regras de chroma-key e os scripts que recortam, alinham e validam a arte:

```bash
python skills/add-provider/scripts/add_room.py --id cursor --room cursor-room.png --seat 182
python skills/add-provider/scripts/cut_poses.py --id cursor --sheet cursor-sheet.png --sheet2 cursor-sheet2.png
python skills/add-provider/scripts/validate_assets.py
```

Para usar no Claude Code, copie a pasta para `.claude/skills/` (ou `~/.claude/skills/`) e peça "adicione o Cursor ao QuotaDesk".

Ao adicionar uma ferramenta, crie também a página `connect-<id>` desta documentação nos dois idiomas: o menu do card abre `docs/connect-<id>.html` quando a ferramenta não tem dados.

## Sala provisória

Uma ferramenta sem sala e personagem próprios não fica de fora do escritório. Ela ganha uma sala própria, depois das salas fixas, com a arte do OpenRouter. O card, a barra na borda e os avisos funcionam igual às outras.

Quando a sala e o personagem da ferramenta ficarem prontos, basta adicioná-los: o QuotaDesk passa a usar a arte nova sozinho.
