# Desenho da solução

Este documento descreve a arquitetura alvo para atender aos requisitos de [001-requisitos.md](001-requisitos.md), nos domínios definidos em [002-dominios.md](002-dominios.md).

## 1. Diagramas

Os diagramas das seções 2, 3 e 4 estão em Mermaid neste documento e também em uma versão editável no draw.io:

[diagramas/arquitetura-alvo.drawio](diagramas/arquitetura-alvo.drawio)

| Página no draw.io | Diagrama | Seção |
|---|---|---|
| 1 | Contexto (C4 nível 1) | 2 |
| 2 | Containers (C4 nível 2) | 3 |
| 3 | Fluxo de escrita (sequência) | 4 |

Para abrir o arquivo, use app.diagrams.net → Arquivo → Abrir, ou a extensão draw.io do VS Code. Ao alterar um diagrama, mantenha as duas versões iguais.

## 2. Visão de contexto (C4 – nível 1)

```mermaid
flowchart LR
    C[Comerciante] -->|usa| W[Web App Angular]
    W -->|HTTPS + JWT| L[Serviço de Lançamentos]
    W -->|HTTPS + JWT| K[Serviço de Consolidado]
    L -.->|evento LancamentoRegistrado| K
    IdP[(Identity Provider OIDC)] -. emite tokens .-> W
```

## 3. Containers (C4 – nível 2)

```mermaid
flowchart LR
    subgraph Cliente
      W[Angular SPA]
    end
    subgraph Lancamentos[Domínio Lançamentos]
      LA[Lancamentos.Api<br/>ASP.NET Core]
      LDB[(SQL Server<br/>Lançamentos + Outbox)]
      OP[OutboxPublisher<br/>BackgroundService]
      LA --> LDB
      OP --> LDB
    end
    subgraph Broker
      EX{{Exchange verx.lancamentos}}
      Q[[Fila durável consolidado.lancamento-registrado]]
      DLQ[[DLQ]]
      EX --> Q
      Q -. falhas .-> DLQ
    end
    subgraph Consolidacao[Domínio Consolidado]
      CO[LancamentoConsumer<br/>BackgroundService]
      CA[Consolidado.Api<br/>ASP.NET Core]
      CDB[(SQL Server<br/>Saldo diário + Eventos processados)]
      CACHE[(Cache em memória)]
      CO --> CDB
      CA --> CACHE --> CDB
    end
    W --> LA
    W --> CA
    OP -- publisher confirms --> EX
    Q --> CO
    PROM[Prometheus] -. scrape /metrics .-> LA
    PROM -. scrape /metrics .-> CA
```

Os dois domínios têm bancos de dados distintos e não se acessam diretamente. A única integração é o evento `LancamentoRegistrado`, definido em `Verx.Contracts`, sem chamada síncrona entre os serviços.

## 4. Fluxo de escrita (comando)

```mermaid
sequenceDiagram
    participant U as Comerciante
    participant L as Lançamentos API
    participant DB as DB Lançamentos
    participant O as Outbox Publisher
    participant B as RabbitMQ
    participant C as Consumer Consolidado
    participant CDB as DB Consolidado
    U->>L: POST /lancamentos
    L->>DB: BEGIN, INSERT lancamento + INSERT outbox, COMMIT
    L-->>U: 201 Created
    loop a cada 1s
      O->>DB: SELECT outbox pendente
      O->>B: publish (persistente, confirm)
      O->>DB: marca publicado
    end
    B->>C: entrega (confirmação manual)
    C->>CDB: evento já processado? se não, atualiza saldo + registra EventId (1 transação)
    C-->>B: confirma processamento
```

O `201` não depende do broker nem do Consolidado: se qualquer um estiver fora, o evento permanece no outbox ou na fila e é aplicado quando o componente voltar.

## 5. Fluxo de leitura (consulta)
`GET /consolidado/{data}` → cache em memória (TTL configurável, padrão 10 s, invalidado quando o evento do dia é processado) → em *miss*, leitura por chave primária em `SaldoDiario`. Custo por requisição: uma busca por chave primária ou um acerto de cache.
