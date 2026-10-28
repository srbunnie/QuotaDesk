# Conectar o Antigravity

Abra o Antigravity e pronto: a cota aparece sozinha enquanto ele está aberto.

**Mostra:** cota por grupo de modelos (por exemplo Gemini, e Claude e GPT).

## Passo a passo

1. Abra o Google Antigravity e entre na sua conta.
2. Pronto. O QuotaDesk encontra o servidor local do Antigravity e lê a cota a cada 30 segundos.

Para ver outro grupo de modelos no card, use o menu do card › **Próximo grupo / modelo**.

## De onde vêm os dados

O QuotaDesk só aceita o processo `language_server` da pasta de instalação do Antigravity, e só conversa com ele pelo `127.0.0.1`. O token dessa sessão local fica na memória e nunca é gravado.

## Se não aparecer

| Mensagem | O que fazer |
|---|---|
| *Abra o Antigravity para consultar a cota.* | Ele está fechado ou ainda iniciando. Abra o Antigravity e espere alguns segundos. |

Com o Antigravity fechado, o card mostra a última leitura marcada como antiga.
