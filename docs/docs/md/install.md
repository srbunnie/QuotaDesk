# Instalar

Instale com um comando no PowerShell, sem compilar, sem administrador e sem precisar do .NET.

## Instalação em um clique

Abra o PowerShell e cole:

```powershell
irm https://raw.githubusercontent.com/srbunnie/QuotaDesk/main/install.ps1 | iex
```

O instalador:

1. baixa a última versão publicada em [Releases](https://github.com/srbunnie/QuotaDesk/releases/latest);
2. instala em `%LOCALAPPDATA%\Programs\QuotaDesk`;
3. cria atalhos no Menu Iniciar e na Área de Trabalho;
4. abre o painel.

Se preferir fazer à mão, baixe [`QuotaDesk-win-x64.zip`](https://github.com/srbunnie/QuotaDesk/releases/latest/download/QuotaDesk-win-x64.zip), extraia e abra `QuotaDesk.exe`.

## Com o repositório baixado

Baixe o repositório ([ZIP](https://github.com/srbunnie/QuotaDesk/archive/refs/heads/main.zip) ou `git clone`) e dê dois cliques em **`QuotaDesk.cmd`**. O menu abre e, se ninguém escolher nada em 8 segundos, abre o seu QuotaDesk.

| Opção | Linha de comando | O que faz |
|---|---|---|
| 1 | `QuotaDesk.cmd abrir` | Abre o app instalado. Se ainda não houver um, instala a última versão publicada, sem compilar. |
| 2 | `QuotaDesk.cmd atualizar` | Compara com a última Release e instala se houver versão nova (`reinstalar` instala mesmo assim). |
| 3 | `QuotaDesk.cmd dev` | Compila o código atual e abre o [ambiente de desenvolvimento](develop.md#ambientes). |
| 4 | `QuotaDesk.cmd homolog` | Compila o código atual e abre a [homologação](develop.md#ambientes). |
| 5 | `QuotaDesk.cmd codigo` | Compila este código e instala na versão pessoal. |
| 6 | `QuotaDesk.cmd parar` | Fecha o desenvolvimento e a homologação. |

A opção 5 (também `QuotaDesk.cmd -NoInstall` ou `-Package`):

1. procura o SDK do .NET 10 e, se faltar, baixa o instalador oficial da Microsoft, confere a assinatura digital e instala só para o seu usuário;
2. compila, confere se o Windows permite rodar o binário novo, guarda a instalação anterior e instala;
3. cria os atalhos e abre o painel.

## Atualizar

Rode o mesmo comando de instalação de novo, ou `QuotaDesk.cmd atualizar`. Atualizar ou reinstalar troca só a pasta do programa: preferências, logins e histórico ficam em `%LOCALAPPDATA%\AIUsageMonitor`.

## "O Windows protegeu o computador"

O executável não é assinado digitalmente. Na primeira execução o Windows pode mostrar *O Windows protegeu o computador*: clique em **Mais informações › Executar assim mesmo**.

Antivírus e Smart App Control podem bloquear compilações novas. Nesse caso o instalador para sem mexer na versão instalada.

## Iniciar com o Windows

Ligue em **Configurações › Geral › Iniciar com o Windows** ou no menu da bandeja. Os ambientes de desenvolvimento e homologação nunca mudam essa opção.

## Desinstalar

Não há desinstalador: a instalação é só uma pasta e alguns atalhos.

1. Desligue **Configurações › Geral › Iniciar com o Windows** e feche o QuotaDesk (**Configurações › Geral › Desligar o QuotaDesk**).
2. Apague a pasta `%LOCALAPPDATA%\Programs\QuotaDesk` e os atalhos.
3. Para apagar também preferências, logins e histórico, apague `%LOCALAPPDATA%\AIUsageMonitor`. Para revogar o acesso da Magnific e da OpenRouter do lado do serviço, remova a autorização no site de cada uma.

## Próximo passo

[Conecte a primeira ferramenta](connect-codex.md). Codex, Antigravity e Figma funcionam sozinhos; o Claude Code precisa de uma configuração única.
