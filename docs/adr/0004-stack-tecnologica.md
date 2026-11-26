# ADR-0004: .NET 10, SQL Server e Angular

**Status:** aceito

**Decisão:** back-end em ASP.NET Core (controllers MVC) com EF Core sobre SQL Server; front-end em Angular (standalone components + signals).

**Justificativa:** stack de domínio do autor; ASP.NET Core tem bom desempenho e ecossistema de observabilidade (OpenTelemetry, health checks); SQL Server atende a transações e a concorrência otimista usadas aqui; Angular entrega SPA tipada e testável.

**Consequências:** licenciamento de SQL Server em produção (usar PaaS ou avaliar PostgreSQL – ver `006-estimativa-custos.md`). Os serviços acessam o banco via EF Core, o que mantém a troca de provider viável.
