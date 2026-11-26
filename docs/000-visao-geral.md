# Visão geral

Resumo de uma página da solução do desafio de fluxo de caixa. Cada seção aponta para o documento com o detalhe.

## 1. Problema
O comerciante precisa registrar créditos e débitos e consultar o saldo consolidado de cada dia. O serviço de **Lançamentos** não pode parar se o **Consolidado** cair, e o Consolidado deve suportar 50 requisições por segundo com no máximo 5% de perda. Detalhes em [001-requisitos.md](001-requisitos.md) e [002-dominios.md](002-dominios.md).

## 2. Solução em uma frase
Dois serviços .NET (DDD em camadas) ligados por eventos: Lançamentos grava o lançamento e o evento na mesma transação (*outbox*), o RabbitMQ entrega, e o Consolidado atualiza um saldo pré-agregado de forma idempotente, servido com cache. Front-end em Angular. Detalhes em [003-desenho-da-solucao.md](003-desenho-da-solucao.md).

## 3. Decisões principais

| Decisão | Motivo | Onde |
|---|---|---|
| Dois serviços com eventos | Isolamento de falha (RNF01) | [004-decisao-escolha.md](004-decisao-escolha.md) |
| Outbox + consumidor idempotente | Sem perda e sem duplicidade (RNF04, RNF05) | [004-decisao-escolha.md](004-decisao-escolha.md) |
| Saldo pré-agregado + cache | 50 req/s com baixa perda (RNF02) | [004-decisao-escolha.md](004-decisao-escolha.md) |
| Sem Event Sourcing (evolução futura) | Simplicidade; o histórico já fica nos lançamentos imutáveis | [004-decisao-escolha.md](004-decisao-escolha.md) |

## 4. Como chegar lá, quanto custa e como operar

| Tema | Resumo | Documento |
|---|---|---|
| Migração do legado | *Strangler Fig* em fases, com execução paralela e conciliação; impacto operacional mapeado | [005-transicao-legado.md](005-transicao-legado.md) |
| Custos | Estimativa mensal em US$ e R$ | [006-estimativa-custos.md](006-estimativa-custos.md) |
| Monitoramento | Health, métricas, logs, correlation-id, SLOs e alertas | [007-monitoramento.md](007-monitoramento.md) |
| Segurança e LGPD | JWT, criptografia, dados pessoais, retenção e direitos do titular | [008-seguranca.md](008-seguranca.md) |
| Riscos | Matriz com probabilidade, impacto e mitigação | [009-riscos.md](009-riscos.md) |

## 5. Mapa dos documentos
Os ADRs em [`adr/`](adr/) registram cada decisão em detalhe, e o draw.io em [diagramas/arquitetura-alvo.drawio](diagramas/arquitetura-alvo.drawio) traz os diagramas editáveis.
