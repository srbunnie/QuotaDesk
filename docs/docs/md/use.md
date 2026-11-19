# Usar o painel

Como ler os cards, o escritório, a barra na borda da tela e os avisos, e o que cada clique faz.

![Painel com a cota de 5 horas do Codex esgotada, Claude a 93% e uma pendência de permissão (dados simulados)](https://srbunnie.github.io/QuotaDesk/assets/screens/panel-waiting.png)

## O escritório

Cada IA tem uma sala e um personagem. Ele mostra o estado da ferramenta sem você precisar ler nada:

| O personagem… | Quer dizer |
|---|---|
| digita | a IA está trabalhando |
| pensa | a IA está esperando por você (permissão ou pergunta) |
| toma café ou cochila | a IA está parada |
| procura sinal no celular | não há leitura da ferramenta |
| deixa uma placa "PAUSA ATÉ 18:35" | a cota de 5 horas acabou |
| deixa a cadeira vazia com "VOLTO SÁB 15:47" | a cota semanal acabou |

Com **Falas reais dos agentes** ligado (em **Configurações › Avisos**), o balão do personagem mostra o título da tarefa e a última ação, como "Executou o comando". Essas falas saem só dos nomes de eventos e ferramentas, nunca do conteúdo dos prompts.

As janelas mostram o céu real da sua cidade, se você ligar o clima: menu da bandeja › **Clima real na janela (Open-Meteo)** e **Cidade do clima**.

## Os cards

Cada card mostra a cota de 5 horas e a semanal (ou o saldo, para Magnific e OpenRouter), o ritmo de gasto e uma previsão.

- **Marca de ritmo:** a barra tem uma marca de ritmo. Se você está gastando mais rápido do que a janela permite, o card avisa, por exemplo, "nesse ritmo a cota acaba ~16:08, antes de renovar".
- **`~`** marca valores estimados, como a cota do Claude lida do histórico do app.
- **Leitura antiga:** quando a fonte não responde, o card mantém a última leitura, marcada como antiga. O QuotaDesk nunca inventa um número.
- **Selo de pendências:** um número no card do Claude Code indica quantas sessões estão esperando por você.

### Trocar "Renova em" por "Esgota em"

**Clique numa linha** do card (5 horas ou semanal) para alternar só aquela linha entre **Renova em X** (quando a cota renova) e **Esgota em X** (quando ela acaba no ritmo atual). Clicar fora das linhas, ou girar a rodinha do mouse sobre o card, alterna todas as linhas de uma vez. Assim dá para ver só o "esgota em" das 5 horas quando for preciso.

### O menu do card

O botão de menu no canto de cada card reúne as ações daquela ferramenta:

- **Atualizar consumo:** lê a ferramenta de novo agora.
- **Abrir Claude Code no terminal** (só no Claude): atualiza a cota exata do Claude.
- **Conectar…** e **Trocar conta…** (Magnific e OpenRouter).
- **Como conectar: Codex…** (com o nome da ferramenta): aparece quando a ferramenta ainda não tem dados e abre a página de conexão dela nesta documentação.
- **Ver pendências:** lista as sessões esperando por você.
- **Próximo grupo / modelo** e **Próxima ferramenta:** trocam o que o card mostra.
- **Nível visual:** só ícone, ícone + estado ou dados completos.
- **Janelas visíveis:** escolha quais janelas o card mostra (5 horas, semanal, créditos ou por modelo). Serve para ferramentas que não têm janela de 5 horas em alguns planos. Só a exibição muda: os avisos continuam vendo todas as janelas, e a última janela visível não pode ser escondida.

## Avisos

- **Aguardando você:** quando uma sessão do Claude Code pede permissão ou faz uma pergunta, o Windows mostra a notificação *Aguardando você*. **Clique nela** para abrir a lista de pendências.
- **Renovação:** um aviso quando a cota renova (**Configurações › Avisos › Avisar renovação de cota**).
- **Som:** um som quando um agente pede atenção (**Som ao pedir atenção**).

## A barra na borda da tela

![Balão da barra na borda da tela com as cotas do Claude Code e a previsão (dados simulados)](https://srbunnie.github.io/QuotaDesk/assets/screens/notch.png)

A barra fica colada em qualquer canto de qualquer monitor e sempre por cima do painel. Escolha o estilo em **Configurações › Aparência › Barra na borda da tela**: **Anéis**, **Minimalista** ou **Desligada**.

- Passe o mouse para abrir um balão com os limites, o ritmo e a previsão.
- **Clique no balão** para alternar entre "Renova em" e "Esgota em".
- **Botão direito na barra › Manter a barra aberta** deixa a barra sempre aberta, sem recolher. A mesma opção fica em **Configurações › Aparência**.

## O ícone da bandeja

O ícone perto do relógio tem um medidor por ferramenta, e passar o mouse mostra o restante de cada uma. O botão direito abre o menu da bandeja, com:

- **Abrir painel**, **Abrir central de controle** e **Atualizar agora**;
- **Conectar Magnific** e **Conectar OpenRouter**;
- **Sempre no topo**, **Barra lateral na borda da tela**, **Barra minimalista**, **Fixar a barra (mantê-la aberta)**, **Mini monitor (tela secundária)** e o clima;
- a primeira linha do menu mostra a **versão e o número da build** (e o ambiente, como `dev` ou `homolog`), para saber qual cópia está aberta;
- **Iniciar com o Windows**;
- **Idioma / Language**;
- **Ajuda e documentação**, que abre esta documentação.

## Mini monitor

Uma tela USB-C de 3,5" (Turing Smart Screen e clones) ou um monitor pequeno vira um mini painel permanente, que pagina as ferramentas sozinho. Ligue em menu da bandeja › **Mini monitor (tela secundária)**. Telas que aparecem como porta serial são detectadas sozinhas. As mesmas opções ficam em **Configurações › Geral**.

- **Feche o programa do fabricante** da tela antes (por exemplo, o do cabo USB): só um programa por vez usa a porta serial. Se ele abre junto com o Windows, tire-o da inicialização.
- **Tela deitada:** se a tela fica apoiada na horizontal, ligue **Tela USB deitada**. O QuotaDesk desenha o layout 480×320 e gira a imagem 90° para ela aparecer em pé.
- **Fluidez:** o cabo leva cerca de 165 KB/s, então um quadro inteiro (320×480) demora ~1,9 s, qualquer que seja a velocidade configurada. Por isso, depois do primeiro quadro só os blocos de 16×16 pixels que mudaram são enviados, e a animação fica bem mais fluida. Em **Configurações › Geral › Estado da tela USB** aparece o ritmo real.
- **Poucas ferramentas:** com 1 ou 2 ferramentas ativas os cards ocupam a largura toda; com 1, a sala fica ao lado do card.

## Atalhos

| Tecla | O que faz |
|---|---|
| `F5` | Atualiza todas as ferramentas agora |
| `F11` | Alterna a resolução do painel (desktop ou mini monitor 480×320) |
