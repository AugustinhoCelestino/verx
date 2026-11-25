# 1. Domínios, capacidades e requisitos

## 1.1 Contexto de negócio
O comerciante controla o fluxo de caixa diário (créditos e débitos) e precisa do saldo consolidado por dia.

## 1.2 Mapa de domínios e capacidades

| Domínio | Tipo | Capacidades de negócio | Serviço |
|---|---|---|---|
| **Lançamentos (Fluxo de Caixa)** | Core | Registrar crédito/débito; consultar lançamentos por dia; garantir integridade do registro | `Verx.Lancamentos.Api` |
| **Consolidação** | Core / Suporte | Apurar saldo diário (créditos − débitos); disponibilizar consulta de saldo por dia/período; tolerar reprocessamento | `Verx.Consolidado.Api` |
| **Identidade e Acesso** | Genérico | Autenticar comerciante; autorizar consumo das APIs | IdP externo (OIDC) – simulado por JWT local |
| **Plataforma / Observabilidade** | Genérico | Health, métricas, logs, alertas | Prometheus (+ Grafana/OTel em produção) |

**Contextos delimitados (bounded contexts):** Lançamentos e Consolidação são contextos distintos, com bancos distintos. A única integração é o evento `LancamentoRegistrado` (contrato em `Verx.Contracts`), sem acoplamento de banco ou chamada síncrona.

## 1.3 Requisitos funcionais

| ID | Requisito |
|---|---|
| RF01 | Registrar lançamento com tipo (crédito/débito), valor > 0 (2 casas), data (padrão: hoje) e descrição opcional |
| RF02 | Listar lançamentos de uma data |
| RF03 | Consultar saldo consolidado de uma data (créditos, débitos, saldo, quantidade) |
| RF04 | Consultar saldos de um período |
| RF05 | Dia sem movimento retorna saldo zero |
| RF06 | Interface web para registrar lançamentos e visualizar o consolidado |

## 1.4 Requisitos não funcionais

| ID | Requisito | Meta | Como é atendido |
|---|---|---|---|
| RNF01 | **Isolamento de falha**: Lançamentos não pode ficar indisponível se o Consolidado cair | Disponibilidade de escrita independente do Consolidado | Comunicação assíncrona via RabbitMQ + *transactional outbox*; bancos separados; nenhuma chamada síncrona entre serviços (ADR-0002, 0003) |
| RNF02 | **Pico no Consolidado**: 50 req/s com ≤ 5% de perda | ≥ 95% de sucesso a 50 req/s; p95 < 500 ms | Leitura de linha única pré-agregada (PK = data) + cache em memória (TTL) + escala horizontal stateless; teste k6 em `tests/load` |
| RNF03 | Consistência | Eventual; saldo reflete lançamentos em segundos | Consumo assíncrono; cada evento aplicado exatamente uma vez (efetivamente) via idempotência por `EventId` |
| RNF04 | Durabilidade | Nenhum lançamento perdido | Outbox na mesma transação; fila durável com mensagens persistentes e *publisher confirms*; DLQ |
| RNF05 | Segurança | Acesso autenticado | JWT Bearer em todas as rotas de negócio (ver `04-seguranca-e-observabilidade.md`) |
| RNF06 | Observabilidade | Saúde e métricas | `/health/live`, `/health/ready`, `/metrics` (Prometheus) |
| RNF07 | Escalabilidade | Horizontal | Serviços stateless; concorrência otimista no saldo permite múltiplos consumidores |

## 1.5 Premissas
- Um comerciante / uma conta de caixa nesta versão (multi-tenant está nas evoluções).
- A "data" do lançamento é a data contábil informada; o fuso/dia-fechamento usa UTC por padrão.
- Lançamentos são imutáveis; estornos serão novos lançamentos (evolução).
- Consistência eventual no consolidado é aceitável para o negócio (relatório diário).
