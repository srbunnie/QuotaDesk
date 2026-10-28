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
