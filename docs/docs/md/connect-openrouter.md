# Conectar a OpenRouter

Faça login uma vez no navegador e o card mostra seu saldo, o gasto por dia e quando ele acaba.

**Mostra:** saldo em US$, gasto por dia e previsão de quando o saldo acaba.

## Passo a passo

1. Clique em **Conectar OpenRouter**: no menu da bandeja, na central ou no menu do card.
2. Entre na OpenRouter no navegador e autorize.
3. Volte ao QuotaDesk.

## De onde vêm os dados

O MCP oficial da OpenRouter (`mcp.openrouter.ai`). O login fica em `openrouter-mcp-token.dat`, protegido com DPAPI: só o seu usuário do Windows nesta máquina consegue abri-lo.

## Trocar de conta

Menu do card › **Trocar conta…**. Ele esquece o login atual e abre o login no navegador.

## Se não aparecer

| Mensagem | O que fazer |
|---|---|
| *Conecte ou reautorize OpenRouter no menu da bandeja.* | O login expirou ou foi revogado. Conecte de novo. |
| *Não foi possível consultar o saldo do OpenRouter* | Falha de rede ou do serviço. O QuotaDesk tenta de novo após 1 hora e mantém a última leitura. |

## O login vale 7 dias

A OpenRouter não renova esse login sozinha: depois de uma semana, o card pede para conectar de novo (*Autorização expirada*). Escolha **Conectar OpenRouter** outra vez. A chave criada tem limite de US$ 0 de propósito: ela só serve para ler o saldo, nunca para gastar.

## Conta de organização

Se a chave for criada num workspace de organização em que você não é admin, a OpenRouter recusa o saldo (*só chaves de gerenciamento ou criadas por admins da organização veem o saldo da organização*). O card avisa isso. Ao conectar, escolha o seu workspace pessoal (Default), ou peça a um admin da organização para autorizar.

## Desconectar

Feche o QuotaDesk e apague `openrouter-mcp-token.dat` em `%LOCALAPPDATA%\AIUsageMonitor`. Para revogar o acesso também do lado do serviço, remova a autorização no site da OpenRouter.
