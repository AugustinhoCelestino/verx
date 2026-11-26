# ADR-0006: DDD tático em camadas dentro de cada serviço

**Status:** aceito

**Decisão:** cada API (Lançamentos e Consolidado) é um *bounded context* organizado em Domain, Application, Infrastructure e Controllers (API REST com ASP.NET Core MVC), no mesmo projeto (pastas e namespaces). O domínio não referencia EF Core nem ASP.NET: agregados com invariantes, value objects (`Dinheiro`, `Email`), portas de repositório e eventos de domínio. A Application orquestra casos de uso e converte eventos de domínio no evento de integração publicado via outbox.

**Justificativa:** as regras (valor positivo com 2 casas, e-mail normalizado, saldo = créditos − débitos) ficam testáveis sem banco, e trocar SQL Server/SQLite ou RabbitMQ/HTTP afeta só a Infrastructure.

**Consequências:** mais arquivos e indireção que o necessário numa PoC. Projetos separados por camada (e testes de arquitetura) são a evolução natural se o time crescer; mantivemos um projeto por serviço para não alterar Dockerfile e pipeline. O schema dos bancos e o contrato `LancamentoRegistrado` permanecem inalterados.
