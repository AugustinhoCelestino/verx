# Verx – Fluxo de Caixa (Lançamentos + Consolidado diário)

Solução para o desafio de Arquiteto de Soluções: um comerciante registra **débitos e créditos** e consulta o **saldo diário consolidado**.

- **Back-end:** .NET 10 (ASP.NET Core minimal APIs, EF Core, SQL Server)
- **Mensageria:** RabbitMQ
- **Front-end:** Angular
- **Testes:** xUnit (unitários/integração com SQLite em memória) e k6 (carga)

## Como funciona

```
Angular ──► Lançamentos API ──► SQL Server (lançamento + outbox, mesma transação)
                                   │ OutboxPublisher
                                   ▼
                               RabbitMQ (fila durável + DLQ)
                                   │
                                   ▼
Angular ──► Consolidado API ◄── Consumer ──► SQL Server (saldo diário + eventos processados)
```

- **Lançamentos** nunca chama o Consolidado. Se ele (ou o broker) cair, os lançamentos continuam sendo aceitos; os eventos aguardam no outbox/fila e são aplicados depois.
- **Consolidado** mantém o saldo pré-agregado por dia, aplica cada evento uma única vez (idempotência por `EventId`) e responde consultas com cache. Meta: 50 req/s com ≤ 5% de perda.

## Documentação

| Documento | Conteúdo |
|---|---|
| [docs/01-dominios-e-requisitos.md](docs/01-dominios-e-requisitos.md) | Domínios, capacidades, requisitos funcionais e não funcionais |
| [docs/02-arquitetura-alvo.md](docs/02-arquitetura-alvo.md) | Desenho da solução (C4/Mermaid), fluxos, trade-offs, mapeamento para nuvem |
| [docs/03-transicao-e-custos.md](docs/03-transicao-e-custos.md) | Arquitetura de transição (legado) e estimativa de custos |
| [docs/04-seguranca-e-observabilidade.md](docs/04-seguranca-e-observabilidade.md) | Segurança de consumo, monitoramento e SLOs |
| [docs/adr/](docs/adr) | Decisões arquiteturais (ADRs) |

## Executar localmente

### Opção A – sem Docker (Windows, mais simples)
Execute [`run/run.cmd`](run/run.cmd). Ele verifica/instala o .NET 10 SDK e o Node.js (via winget), compila, abre as duas APIs e o Angular, e abre o navegador. Para parar, use [`run/stop.cmd`](run/stop.cmd).

Neste modo, para dispensar infraestrutura, o banco é **SQLite** (arquivos em `run/data`) e os eventos vão do Lançamentos ao Consolidado por **HTTP**, ainda via outbox, então a retentativa e o isolamento de falhas continuam valendo. Os provedores são escolhidos por configuração (`Database:Provider` e `Messaging:Provider`); a arquitetura alvo (SQL Server + RabbitMQ) é a da Opção B.

### Opção B – tudo via Docker (arquitetura alvo: SQL Server + RabbitMQ)
Pré-requisito: Docker com Compose. No Windows, execute [`run/run-docker.cmd`](run/run-docker.cmd) (instala/inicia o Docker Desktop, sobe tudo e abre o navegador) e [`run/stop-docker.cmd`](run/stop-docker.cmd) para parar.

Ou manualmente:

```bash
docker compose up --build
```

| Serviço | URL |
|---|---|
| Web (Angular) | http://localhost:4200 |
| Lançamentos API | http://localhost:5101 |
| Consolidado API | http://localhost:5102 |
| RabbitMQ (guest/guest) | http://localhost:15672 |
| Prometheus | http://localhost:9090 |

As APIs criam os bancos automaticamente na primeira execução (aguardam o SQL Server ficar pronto).

### Opção C – APIs com `dotnet run` contra SQL Server e RabbitMQ
Pré-requisitos: .NET 10 SDK, Node 22+, e SQL Server + RabbitMQ (suba só eles: `docker compose up sqlserver rabbitmq`).

```bash
dotnet run --project src/Verx.Lancamentos.Api   # http://localhost:5101
dotnet run --project src/Verx.Consolidado.Api   # http://localhost:5102
cd web && npm install && npm start              # http://localhost:4200
```

### Login e usuários

A tela do Angular pede login (ou cadastro). Em Development já existe o usuário demo **`demo@verx.com` / `Demo@12345`**. O token JWT (`POST /auth/login`) identifica o usuário (`sub`, `name`, `email`) e cada lançamento guarda quem o registrou. Endpoints: `POST /auth/registrar`, `POST /auth/login`, `GET /auth/me`.

> Ao atualizar de uma versão sem login, o schema do SQLite local é atualizado automaticamente (lançamentos antigos ficam sem usuário). Com SQL Server existente, apague o banco para recriar o schema.

### Swagger

Em Development (Opções A, B e C) cada API expõe o Swagger UI; a Opção A (`run\run.cmd`) já o abre no navegador:

- Lançamentos: http://localhost:5101/swagger
- Consolidado: http://localhost:5102/swagger

Para chamar endpoints protegidos: execute `POST /auth/login` no Swagger de Lançamentos (usuário demo acima), copie o `access_token` e informe-o em **Authorize** (Bearer) em qualquer um dos dois. No modo sem Docker os dados ficam em `run\data\*.db` (SQLite).

### Testando pela API

```bash
# login (usuário demo, somente em Development) -> access_token
curl -X POST http://localhost:5101/auth/login -H "Content-Type: application/json" \
  -d '{"email":"demo@verx.com","senha":"Demo@12345"}'

curl -X POST http://localhost:5101/lancamentos \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"tipo":1,"valor":150.50,"descricao":"venda"}'      # tipo: 1=Crédito, 2=Débito

curl http://localhost:5102/consolidado/2026-11-24 -H "Authorization: Bearer <token>"
curl "http://localhost:5102/consolidado?de=2026-11-01&ate=2026-11-30" -H "Authorization: Bearer <token>"
```

### Demonstrar o isolamento de falhas
```bash
docker compose stop consolidado-api      # Consolidado fora
# registre lançamentos: continuam retornando 201
docker compose start consolidado-api     # o backlog é consumido e o saldo converge
```

## Testes

```bash
dotnet test                                   # xUnit
k6 run -e TOKEN=<token> tests/load/consolidado.js   # 50 req/s por 60s; falha se > 5% de erros
```

Cobertura dos testes: regras de validação do lançamento, gravação atômica lançamento + outbox, cálculo do saldo, idempotência de eventos duplicados, invalidação de cache e concorrência entre consumidores.

## Estrutura

```
src/Verx.Observability      health (/health/*), consulta de logs (/logs) e buffer de logs, compartilhados pelas APIs
src/Verx.Contracts          evento de integração LancamentoRegistrado + topologia RabbitMQ
src/Verx.Lancamentos.Api    contexto Lançamentos, em camadas DDD (veja abaixo)
src/Verx.Consolidado.Api    contexto Consolidado, em camadas DDD (veja abaixo)
tests/                      testes xUnit e script k6
web/                        SPA Angular
docs/                       arquitetura, requisitos, ADRs
ops/                        configuração do Prometheus
```

### Camadas (DDD)

Cada API é um *bounded context* com as mesmas camadas, em pastas/namespaces, e as dependências apontam sempre para dentro (`Api → Application → Domain`; `Infrastructure` implementa as portas):

| Camada | Lançamentos | Consolidado |
|---|---|---|
| **Domain** (sem EF/ASP.NET) | agregados `Lancamento` e `Usuario`, value objects `Dinheiro` e `Email`, evento de domínio `LancamentoRegistradoEvent`, portas de repositório | agregado `SaldoDiario`, `EventoProcessado`, portas de repositório |
| **Application** | casos de uso `LancamentoService` e `AuthService`, DTOs, portas (`IUnitOfWork`, `IOutbox`, `ITokenService`) | caso de uso `ConsolidadoService` (idempotência, concorrência otimista), portas (`IUnitOfWork`, `ISaldoCache`) |
| **Infrastructure** | EF Core (repositórios, outbox), publisher e transportes RabbitMQ/HTTP, JWT, PBKDF2 | EF Core, consumer RabbitMQ, cache em memória, JWT |
| **Controllers / Program** | `AuthController` e `LancamentosController`, composição | `ConsolidadoController` e `EventosInternosController`, composição |

O evento de domínio fica dentro do contexto; só o evento de integração `LancamentoRegistrado` (em `Verx.Contracts`) cruza a fronteira entre os serviços. Os bancos e os contratos HTTP/JSON não mudaram.

## Limitações conhecidas e evoluções

O que foi simplificado por limite de tempo, e o que eu faria a seguir:

- **Migrations EF Core** em vez de `EnsureCreated`, com pipeline de deploy.
- **Autenticação real** (OIDC/RS256, escopos e roles) – hoje há JWT com chave simétrica de desenvolvimento.
- **Redis** como cache compartilhado e **API Gateway** com rate limiting.
- **Limpeza/retenção do outbox** e *backoff* exponencial; reprocessamento da DLQ por ferramenta/endpoint administrativo.
- **Tracing distribuído** (OpenTelemetry com propagação nas mensagens) e dashboards/alertas Grafana.
- **Multi-tenant** (vários comerciantes/contas), estornos e fechamento de dia com fuso configurável.
- **Testes de integração** com Testcontainers (SQL Server + RabbitMQ) e testes do front-end.
- **CI/CD** (GitHub Actions) com build, testes e varredura de vulnerabilidades.
- Alta disponibilidade de produção: RabbitMQ em cluster (quorum queues) ou Service Bus, SQL com réplica zona-redundante.
