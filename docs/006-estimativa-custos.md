# Estimativa de custos

Estimativa mensal, em ordem de grandeza, para o ambiente de produção descrito em [003-desenho-da-solucao.md](003-desenho-da-solucao.md) e no mapeamento para nuvem de [004-decisao-escolha.md](004-decisao-escolha.md).

> Os valores são aproximados, para a região Brasil ou EUA, com preços de lista e sem desconto ou reserva. Devem ser revalidados na calculadora do provedor antes de qualquer decisão.
>
> Os valores em reais usam a cotação de referência de **US$ 1 = R$ 5,50**, arredondados. A cotação varia e deve ser atualizada na revisão.

## 1. Produção

| Item | Dimensionamento | Custo estimado (US$) | Custo estimado (R$) |
|---|---|---|---|
| Containers (2 APIs × 2 réplicas, 0,5 vCPU / 1 GiB) | Container Apps | ~ US$ 60–120 | ~ R$ 330–660 |
| Azure SQL (2 bancos, General Purpose, 2 vCore serverless com pausa) | Zona redundante opcional | ~ US$ 150–400 | ~ R$ 825–2.200 |
| Service Bus Standard | Até 13 M operações por mês | ~ US$ 10–20 | ~ R$ 55–110 |
| Redis (Basic/Standard C0–C1) | Cache compartilhado | ~ US$ 16–55 | ~ R$ 90–300 |
| Observabilidade (Monitor/Log Analytics, Grafana gerenciado) | ~ 10 GB por dia | ~ US$ 50–150 | ~ R$ 275–825 |
| API Gateway/WAF + CDN/Static Web | — | ~ US$ 30–150 | ~ R$ 165–825 |
| Entra ID | Tier gratuito ou P1, conforme os recursos | US$ 0–6 por usuário | R$ 0–33 por usuário |
| **Total aproximado** | Ambiente produtivo pequeno | **~ US$ 300–900 por mês** | **~ R$ 1.650–4.950 por mês** |

## 2. Desenvolvimento e homologação
Cerca de 30% a 40% do custo de produção, com réplica única e bancos *serverless*.

## 3. Licenças
- A stack é majoritariamente open source: .NET, Angular, RabbitMQ e Prometheus.
- O SQL Server em PaaS já inclui a licença. No `docker-compose` usa-se a edição Developer, apenas para desenvolvimento.
- Alternativa para reduzir custo: PostgreSQL gerenciado, sem licença de SQL Server.
