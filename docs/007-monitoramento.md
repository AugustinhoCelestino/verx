# Monitoramento

Este documento descreve como a solução é monitorada, para atender ao requisito de observabilidade (RNF07) de [001-requisitos.md](001-requisitos.md).

## 1. O que está implementado

| Recurso | Descrição | Onde |
|---|---|---|
| Health live | `GET /health/live` informa se o processo está de pé; serve de probe de liveness | Lançamentos e Consolidado |
| Health ready | `GET /health/ready` verifica as dependências, como o banco. Devolve o detalhe de cada verificação e responde 503 se alguma falhar; serve de probe de readiness | Lançamentos e Consolidado |
| Consulta de logs | `GET /logs?nivel=Warning&busca=texto&limite=100` (autenticado) devolve os últimos 1000 logs, em memória, do mais recente para o mais antigo | Lançamentos e Consolidado |
| Tela de logs | Aba **Logs** da interface web, com filtro por serviço, nível mínimo, texto e limite | Web (Angular) |
| Métricas | `GET /metrics` (Prometheus via OpenTelemetry): taxa, latência e erros HTTP | Lançamentos e Consolidado |
| Prometheus | Coleta as métricas dos dois serviços; no `docker-compose` fica em `http://localhost:9090` | `docker-compose` |
| Logs estruturados | Logs do ASP.NET Core; os erros do consumer incluem o delivery tag e a flag de reentrega | Lançamentos e Consolidado |

Os endpoints de health e de logs são controllers e aparecem no Swagger de cada serviço.

**Limitação:** a consulta de logs é um apoio para a PoC, pois o buffer é em memória e se perde ao reiniciar o serviço. Em produção, os logs devem ir para um agregador (ELK, Loki ou Application Insights).

## 2. Indicadores e objetivos (SLIs e SLOs sugeridos)

| SLI | SLO |
|---|---|
| Disponibilidade de `POST /lancamentos` | 99,9% (independente do Consolidado) |
| Sucesso de `GET /consolidado` em pico | Pelo menos 95% (requisito); meta interna de 99% |
| Latência p95 de `GET /consolidado` | Abaixo de 500 ms |
| Atraso do consolidado (publicação até saldo atualizado) | p95 abaixo de 10 s |

## 3. Alertas recomendados
- Profundidade crescente da fila `consolidado.lancamento-registrado`.
- Mensagens na DLQ maiores que zero.
- Mensagens do outbox pendentes há mais de 1 minuto.
- Taxa de erros 5xx acima de 1%.
- Readiness falhando.

## 4. Evolução
- Traces distribuídos (OpenTelemetry, com `traceparent` nas mensagens).
- Dashboards no Grafana.
- Métricas de negócio, como lançamentos por minuto e atraso do consolidado.
- Logs centralizados.
