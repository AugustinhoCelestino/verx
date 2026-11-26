# Decisões e escolhas

Este documento reúne as decisões que sustentam o [desenho da solução](003-desenho-da-solucao.md): como a arquitetura atende aos requisitos não funcionais de [001-requisitos.md](001-requisitos.md), o padrão adotado e o mapeamento para nuvem.

## 1. Tecnologias escolhidas e por quê

Cada escolha responde a um requisito de [001-requisitos.md](001-requisitos.md). As decisões completas estão nos ADRs em [`adr/`](adr/).

| Tecnologia | Por que foi escolhida | Vantagens |
|---|---|---|
| **.NET 10 / ASP.NET Core** (controllers MVC) | Stack de domínio do autor, com bom suporte a APIs REST, serviços em segundo plano e observabilidade (ADR-0004) | Alto desempenho; `BackgroundService` para o outbox e o consumer; health checks, OpenAPI/Swagger e autenticação JWT nativos; ecossistema maduro |
| **DDD em camadas** (Domain, Application, Infrastructure, Controllers) | Manter as regras de negócio isoladas de banco, fila e HTTP (ADR-0006) | Regras testáveis sem infraestrutura; troca de SQL Server/SQLite ou RabbitMQ/HTTP afeta só a Infrastructure |
| **Dois serviços orientados a eventos** | Atender ao RNF01 (isolamento de falha) e escalar a leitura de forma independente (ADR-0001) | Lançamentos segue no ar com o Consolidado fora; escala independente; cada serviço tem seu banco |
| **RabbitMQ** | Integração assíncrona sem perder eventos (RNF01 e RNF04) (ADR-0002) | Fila durável, mensagens persistentes, *publisher confirms*, DLQ; mais simples de operar que Kafka para este volume; pode ser trocado por Service Bus sem mudar o domínio |
| **Transactional Outbox + consumidor idempotente** | Gravar no banco e publicar no broker não são atômicos (ADR-0003) | Nenhum evento perdido; Lançamentos independente do broker; reprocessar uma mensagem não duplica o saldo |
| **SQL Server + EF Core** (SQLite no modo local) | Transações e concorrência otimista, necessárias ao outbox e ao saldo (ADR-0004) | Consistência transacional; migração de provider facilitada pelo EF Core; modo local sem Docker usando SQLite |
| **Saldo pré-agregado + cache em memória** | Atender ao RNF02 (50 req/s, no máximo 5% de perda) (ADR-0005) | Leitura por chave primária (custo constante); o cache evita ir ao banco na maioria das requisições; TTL curto limita a defasagem |
| **Angular** (standalone components + signals) | Interface web tipada e testável (ADR-0004) | SPA organizada em componentes; tipagem com TypeScript; estado reativo com signals |
| **JWT (Bearer)** | Autenticar o acesso às APIs (RNF06) | Sem estado no servidor (favorece o escalonamento horizontal); trocável por um provedor OIDC em produção |
| **Health checks, `/metrics` (Prometheus) e `/logs`** | Observabilidade (RNF07) | Probes de liveness e readiness para orquestradores; métricas padrão de mercado; consulta rápida de logs recentes pela própria interface |
| **Mermaid + draw.io** | Documentar a arquitetura | Mermaid fica versionado junto do texto; draw.io oferece a versão editável do mesmo desenho |

## 2. Como os requisitos não funcionais são atendidos

**Lançamentos disponível com o Consolidado fora (RNF01).** Não há dependência síncrona. Cenários:

| Falha | Efeito em Lançamentos | Recuperação |
|---|---|---|
| Consolidado (API ou consumer) fora | Nenhum | A fila durável acumula; ao voltar, consome o backlog |
| RabbitMQ fora | Nenhum (grava no banco); o publisher reconecta | O outbox entrega depois (at-least-once) |
| Banco do Consolidado fora | Nenhum | O consumer rejeita a mensagem, há retentativa e depois DLQ; reprocessável |

**50 req/s com no máximo 5% de perda (RNF02).** 50 req/s sobre uma leitura por chave primária é uma carga baixa para uma instância; com cache, a maioria das requisições nem toca o banco. Para dar folga: pelo menos 2 réplicas atrás de um balanceador, *readiness probe* e *autoscaling* por CPU ou requisições por segundo. Validação: `tests/load/consolidado.js` (k6) com o limiar `http_req_failed < 5%`.

**Corretude do saldo (RNF03 e RNF05).** O at-least-once exige idempotência: `EventoProcessado(EventId)` é gravado na mesma transação do saldo. A concorrência entre consumidores é tratada com *optimistic concurrency* (`Versao`) e retentativa.

**Durabilidade (RNF04).** O lançamento e a mensagem do outbox são gravados na mesma transação. A fila é durável, com mensagens persistentes e *publisher confirms*, e as mensagens com falha vão para a DLQ.

## 3. Padrão arquitetural e trade-offs
Dois serviços (microsserviços enxutos) orientados a eventos, com CQRS leve: o modelo de escrita é diferente do modelo de leitura pré-agregado. Um monolito seria mais simples, mas não isola falhas (RNF01) nem escala leitura e escrita de forma independente. Os detalhes estão nos ADRs em [`adr/`](adr/).

**Por que não Event Sourcing?** O Event Sourcing guarda os eventos como fonte da verdade e reconstrói o estado por replay. Aqui os lançamentos já são imutáveis e o saldo é pré-agregado, então o ganho principal (histórico completo) já existe de forma mais simples: a tabela de lançamentos mais o outbox. O Event Sourcing exigiria um *event store*, *snapshots*, versionamento de eventos e uma curva de aprendizado maior, sem benefício claro para um volume de 50 req/s. Fica como **evolução** se surgirem estornos, auditoria temporal ou reconstrução de saldo em qualquer data; o contrato `LancamentoRegistrado` já é um evento de domínio e facilita essa migração.

## 4. Mapeamento para nuvem (alvo)

| Local (docker-compose) | Alvo de produção (ex.: Azure; equivalente em AWS) |
|---|---|
| Containers .NET | Azure Container Apps / AKS (pelo menos 2 réplicas, autoscale) |
| RabbitMQ | Azure Service Bus (ou RabbitMQ gerenciado em cluster quorum) |
| SQL Server | Azure SQL (um banco por serviço, zona redundante) |
| Cache em memória | Azure Cache for Redis (cache compartilhado e invalidação central) |
| JWT local | Microsoft Entra ID / Keycloak (OIDC) |
| Prometheus | Azure Monitor / Managed Prometheus + Grafana; OpenTelemetry |
| nginx (SPA) | Static Web Apps / CDN |
| — | API Gateway (APIM / YARP) com rate limiting e WAF |
