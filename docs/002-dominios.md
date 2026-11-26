# Domínios

## 1. Contexto de negócio
O comerciante controla o fluxo de caixa diário (créditos e débitos) e precisa do saldo consolidado por dia. Os requisitos estão em [001-requisitos.md](001-requisitos.md).

## 2. Mapa de domínios e capacidades

| Domínio | Tipo | Capacidades de negócio | Serviço |
|---|---|---|---|
| **Lançamentos (Fluxo de Caixa)** | Core | Registrar crédito e débito; consultar lançamentos por dia; garantir a integridade do registro | `Verx.Lancamentos.Api` |
| **Consolidado** | Core / Suporte | Apurar o saldo diário (créditos − débitos); consultar o saldo por dia e por período; tolerar o reprocessamento | `Verx.Consolidado.Api` |
| **Login e Acesso** | Genérico | Cadastro e login do comerciante; gerar o token JWT que identifica o usuário; autorizar o consumo das APIs | IdP externo (OIDC), simulado por login com JWT local (`/auth`, no `Verx.Lancamentos.Api`) |
| **Plataforma / Observabilidade** | Genérico | Health, métricas, logs e alertas | `Verx.Observability` (health e logs) e Prometheus (métricas) |

**Tipos de domínio**
- **Core:** diferencia o negócio e recebe o maior investimento.
- **Suporte:** necessário ao negócio, mas sem diferencial competitivo.
- **Genérico:** problema comum, resolvido com solução padrão ou de mercado.

## 3. Glossário

| Termo | Significado |
|---|---|
| Lançamento | Movimentação de caixa, crédito ou débito, com valor, data e descrição |
| Crédito / Débito | Entrada e saída de dinheiro, respectivamente |
| Consolidado | Saldo apurado de um dia: total de créditos, total de débitos, saldo e quantidade de lançamentos |
| Saldo | Total de créditos menos total de débitos |
| Data contábil | Dia ao qual o lançamento pertence, informado pelo comerciante |
