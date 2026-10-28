# Segurança

## Como reportar uma vulnerabilidade

Não abra uma issue pública. Use **Security › Report a vulnerability** neste repositório (aviso privado do GitHub), descrevendo o problema, como reproduzir e o impacto. A resposta inicial costuma sair em poucos dias.

## Escopo

O QuotaDesk roda só na máquina do usuário. Os pontos mais sensíveis são:

- o receptor local de hooks em `127.0.0.1:32871`;
- o retorno do login OAuth (porta local temporária) e os tokens da Magnific e da OpenRouter, guardados com DPAPI em `%LOCALAPPDATA%\AIUsageMonitor`;
- a leitura do servidor local do Antigravity.

## Nunca publique credenciais

Ao abrir issues ou PRs, não anexe `settings.json`, arquivos `*.dat`, o `error.log` nem capturas que mostrem tokens, e-mails ou caminhos pessoais. Esses arquivos ficam fora do repositório por padrão (`.gitignore`).

## Regras do repositório

Nada que dê acesso a uma conta, nem dado que identifique quem contribui, entra no Git. Em vários níveis:

| Camada | O que barra |
|---|---|
| `.gitignore` | Configuração local, `.env`, chaves (`*.pem`, `*.pfx`, `id_rsa*`…), tokens, `*.dat` do DPAPI, logs, despejos de memória (`*.dmp`) e capturas de rede (`*.har`), pacotes de build. |
| `.githooks/pre-commit` | Roda `scripts/scan-leaks.ps1` nas linhas que vão entrar no commit: chaves de API (Anthropic, OpenAI, OpenRouter, GitHub, AWS, Google…), chaves privadas, JWT, `Bearer` literal, senha atribuída no código, caminhos `C:\Users\<nome>`, e-mails, tipos de arquivo proibidos, arquivos acima de 10 MB e autor sem o e-mail *noreply* do GitHub. |
| `.githooks/pre-push` | Repete a verificação nos arquivos versionados e em cada commit que ainda não está no GitHub, inclusive tags e branches antigas enviadas por engano. |
| CI (`leak-scan`) | A mesma verificação em todo push e PR, no histórico inteiro. |
| GitHub | Secret scanning e push protection ligados no repositório. |

Ative os hooks uma vez por clone:

```powershell
git config core.hooksPath .githooks
```

Para verificar à mão: `scripts\scan-leaks.ps1` (stage), `-All` (arquivos) ou `-History` (todos os commits locais).

**Termos pessoais.** Seu nome, seu usuário do Windows ou sua empresa não podem estar numa lista pública. Coloque-os, um por linha, em `.git/info/leak-scan-denylist.txt`, que nunca é enviado. No CI, o mantenedor guarda a mesma lista no segredo `LEAK_SCAN_DENYLIST`. O relatório só mostra o arquivo e a linha, nunca o termo.

**E-mail.** Use o endereço *noreply* do GitHub (`ID+usuario@users.noreply.github.com`) no `git config user.email` deste repositório e ative *Block command line pushes that expose my email* em GitHub › Settings › Emails.

**Falsos positivos.** Um exemplo propositalmente falso (em teste ou documentação) pode levar o marcador `leak-scan:allow` na mesma linha. Nunca use o marcador para uma chave de verdade.

**Se algo vazar.** Revogue a chave na hora, no painel do serviço. Apagar o commit não basta: o que chegou ao GitHub pode ter sido copiado. Depois limpe o histórico e avise pelo canal privado acima.

---

## English

### Reporting a vulnerability

Don't open a public issue. Use **Security › Report a vulnerability** in this repository (GitHub private advisory). Describe the problem, how to reproduce it and its impact. You'll usually get a first reply within a few days.

### Scope

QuotaDesk runs only on the user's machine. The most sensitive parts are:

- the local hook receiver on `127.0.0.1:32871`;
- the OAuth login callback (temporary local port) and the Magnific and OpenRouter tokens, stored with DPAPI in `%LOCALAPPDATA%\AIUsageMonitor`;
- reading Antigravity's local server.

### Never publish credentials

In issues and PRs, don't attach `settings.json`, `*.dat` files, `error.log` or screenshots showing tokens, e-mails or personal paths. These files are kept out of the repository by default (`.gitignore`).

### Repository rules

Nothing that grants access to an account, and no data that identifies a contributor, goes into Git. Several layers enforce this:

| Layer | What it blocks |
|---|---|
| `.gitignore` | Local config, `.env`, keys (`*.pem`, `*.pfx`, `id_rsa*`…), tokens, DPAPI `*.dat` files, logs, memory dumps (`*.dmp`), network captures (`*.har`), build packages. |
| `.githooks/pre-commit` | Runs `scripts/scan-leaks.ps1` on the lines being committed: API keys (Anthropic, OpenAI, OpenRouter, GitHub, AWS, Google…), private keys, JWTs, literal `Bearer` tokens, passwords assigned in code, `C:\Users\<name>` paths, e-mails, forbidden file types, files over 10 MB and an author without the GitHub *noreply* e-mail. |
| `.githooks/pre-push` | Repeats the check on tracked files and on every commit not yet on GitHub, including tags and old branches pushed by mistake. |
| CI (`leak-scan`) | The same check on every push and PR, across the whole history. |
| GitHub | Secret scanning and push protection turned on for the repository. |

Turn on the hooks once per clone:

```powershell
git config core.hooksPath .githooks
```

To check by hand: `scripts\scan-leaks.ps1` (staged changes), `-All` (tracked files) or `-History` (all local commits).

**Personal terms.** Your name, Windows user name or company can't be in a public list. Put them, one per line, in `.git/info/leak-scan-denylist.txt`, which is never pushed. In CI, the maintainer keeps the same list in the `LEAK_SCAN_DENYLIST` secret. The report only shows the file and line, never the term.

**E-mail.** Use your GitHub *noreply* address (`ID+username@users.noreply.github.com`) in this repository's `git config user.email`, and turn on *Block command line pushes that expose my email* in GitHub › Settings › Emails.

**False positives.** A deliberately fake example (in a test or in docs) can carry the `leak-scan:allow` marker on the same line. Never use the marker for a real key.

**If something leaks.** Revoke the key right away in the service's dashboard. Deleting the commit isn't enough: whatever reached GitHub may have been copied. Then clean the history and report it through the private channel above.
