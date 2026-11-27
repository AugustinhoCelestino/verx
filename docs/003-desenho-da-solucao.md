# Desenho da solução

Este documento descreve a arquitetura alvo para atender aos requisitos de [001-requisitos.md](001-requisitos.md), nos domínios definidos em [002-dominios.md](002-dominios.md).

## 1. Diagramas

Os diagramas das seções 2, 3, 4 e 6 estão em Mermaid neste documento e também em uma versão editável no draw.io:

[diagramas/arquitetura-alvo.drawio](diagramas/arquitetura-alvo.drawio)

| Página no draw.io | Diagrama | Seção |
|---|---|---|
| 1 | Contexto (C4 nível 1) | 2 |
| 2 | Containers (C4 nível 2) | 3 |
| 3 | Fluxo de escrita (sequência) | 4 |
| 4 | Implantação (Docker Compose) | 6 |

Para abrir o arquivo, use app.diagrams.net → Arquivo → Abrir, ou a extensão draw.io do VS Code. Ao alterar um diagrama, mantenha as duas versões iguais.

**Padrão visual** (o mesmo no Mermaid e no draw.io):

| Cor | Significado |
|---|---|
| Azul | Serviço ou aplicação |
| Verde | Banco de dados e cache |
| Laranja | Mensageria (exchange e filas) |
| Cinza | Sistema externo ou de apoio (IdP, Prometheus) |
| Vermelho | Falha (DLQ) |

Linha contínua é chamada síncrona; linha tracejada é evento, apoio ou falha.

## 2. Visão de contexto (C4 – nível 1)

```mermaid
flowchart LR
    C[Comerciante] -->|usa| W[Web App Angular]
    W -->|HTTPS + JWT| L[Serviço de Lançamentos]
    W -->|HTTPS + JWT| K[Serviço de Consolidado]
    L -.->|evento LancamentoRegistrado| K
    IdP[(Identity Provider OIDC)] -. emite tokens .-> W

    classDef svc fill:#DAE8FC,stroke:#254F9B,color:#1A1A1A
    classDef ext fill:#F5F5F5,stroke:#666666,color:#1A1A1A
    class C,W,L,K svc
    class IdP ext
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
      RQ[[Filas de espera retry 5s, 30s, 300s]]
      DLQ[[DLQ]]
      EX --> Q
      Q <-. falha transitória / volta após o TTL .-> RQ
      Q -. falhas definitivas .-> DLQ
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

    classDef svc fill:#DAE8FC,stroke:#254F9B,color:#1A1A1A
    classDef data fill:#D5E8D4,stroke:#2E7D32,color:#1A1A1A
    classDef msg fill:#FFE6CC,stroke:#B36B00,color:#1A1A1A
    classDef ext fill:#F5F5F5,stroke:#666666,color:#1A1A1A
    classDef fail fill:#F8CECC,stroke:#C20016,color:#1A1A1A
    class W,LA,OP,CO,CA svc
    class LDB,CDB,CACHE data
    class EX,Q,RQ msg
    class PROM ext
    class DLQ fail
```

Os dois domínios têm bancos de dados distintos e não se acessam diretamente. Com o broker (arquitetura alvo), a única integração é o evento `LancamentoRegistrado`, definido em `Verx.Contracts`, sem chamada síncrona entre os serviços. No modo local sem Docker (HTTP) o evento vai por `POST /internal/eventos` e o Consolidado confere o evento no Lançamentos (`GET /internal/eventos/{id}`) antes de aplicá-lo; essa conferência não existe com o broker. Mensagens que falham de forma transitória passam pelas filas de espera; as definitivas vão à DLQ, de onde um administrador as reprocessa (`POST /admin/dlq/reprocessar`).

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

## 6. Implantação (Docker Compose)

O `docker-compose.yml` entregue implanta isto (as seções 2 a 4 mostram a lógica; esta mostra onde cada peça roda):

```mermaid
flowchart LR
    B[Navegador]
    subgraph Host[Host Docker - rede interna do Compose]
      WEB[web<br/>nginx + Angular :4200]
      GW[gateway nginx<br/>:5101 Lançamentos / :5102 Consolidado]
      subgraph LAN[lancamentos-api x2]
        L1[réplica 1]
        L2[réplica 2]
      end
      subgraph CON[consolidado-api x2]
        C1[réplica 1]
        C2[réplica 2]
      end
      MQ{{rabbitmq<br/>instância única}}
      SQL[(sqlserver<br/>instância única<br/>VerxLancamentos e VerxConsolidado)]
      PROM[prometheus :9090]
      V1[(volume rabbitmq-data)]
      V2[(volume sqlserver-data)]
      V3[(volume prometheus-data)]
    end
    B --> WEB
    B -->|HTTP + JWT| GW
    GW -->|round-robin| L1 & L2 & C1 & C2
    L1 & L2 --> SQL
    C1 & C2 --> SQL
    L1 & L2 -- publica via outbox --> MQ
    MQ -- consumidores concorrentes --> C1 & C2
    PROM -. scrape /metrics via DNS .-> L1 & L2 & C1 & C2
    MQ --- V1
    SQL --- V2
    PROM --- V3

    classDef svc fill:#DAE8FC,stroke:#254F9B,color:#1A1A1A
    classDef data fill:#D5E8D4,stroke:#2E7D32,color:#1A1A1A
    classDef msg fill:#FFE6CC,stroke:#B36B00,color:#1A1A1A
    classDef ext fill:#F5F5F5,stroke:#666666,color:#1A1A1A
    class WEB,GW,L1,L2,C1,C2 svc
    class SQL,V1,V2,V3 data
    class MQ msg
    class B,PROM ext
```

- As APIs não publicam portas; o gateway é a única entrada e as réplicas escalam com `docker compose up -d --scale lancamentos-api=N`.
- Os volumes nomeados preservam os dados em `docker compose down`.
- **Limite:** SQL Server e RabbitMQ são instância única no Compose. A alta disponibilidade deles (SQL zona-redundante, RabbitMQ em cluster/quorum ou Service Bus) pertence ao ambiente de produção.
- O modo sem Docker (`run\run.cmd`) não usa este desenho: roda as duas APIs e o Angular em processos locais com SQLite e HTTP no lugar do broker.
