# Conectar a Magnific

Faça login uma vez no navegador e o card Criativos passa a mostrar seu saldo de créditos.

**Mostra:** saldo de créditos, quanto você gasta por dia e quando o saldo acaba nesse ritmo. Aparece no card **Criativos**.

## Passo a passo

1. Clique em **Conectar Magnific**: no menu da bandeja, na central ou no menu do card (**Conectar Magnific…**).
2. O navegador abre o login oficial da Magnific. Entre e autorize o acesso.
3. Volte ao QuotaDesk. O saldo aparece em alguns segundos.

**Pelo terminal:** `QuotaDesk.exe --connect-magnific` faz o login sem abrir o painel.

## De onde vêm os dados

O MCP oficial da Magnific (`mcp.magnific.com`), só com ferramentas de leitura de saldo. O login fica em `magnific-mcp-token.dat`, protegido com DPAPI: só o seu usuário do Windows nesta máquina consegue abri-lo.

## Trocar de conta

Menu do card › **Trocar conta…**. Ele esquece o login atual e abre o login no navegador. Se o navegador entrar direto na mesma conta, saia dela no site da Magnific antes.

## Se não aparecer

| Mensagem | O que fazer |
|---|---|
| *Conecte ou reautorize Magnific no menu da bandeja.* | O login expirou ou foi revogado. Conecte de novo. |
| *Não foi possível consultar os créditos do Magnific; nova tentativa em 15 minutos.* | Falha de rede ou do serviço. O card mantém a última leitura marcada como antiga. |

## Desconectar

Feche o QuotaDesk e apague `magnific-mcp-token.dat` em `%LOCALAPPDATA%\AIUsageMonitor`. Para revogar o acesso também do lado do serviço, remova a autorização no site da Magnific.
