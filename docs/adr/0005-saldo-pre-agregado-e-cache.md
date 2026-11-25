# ADR-0005: Saldo pré-agregado com cache para leitura

**Status:** aceito

**Contexto:** o consolidado recebe 50 req/s em pico com no máximo 5% de perda.

**Decisão:** manter uma linha por dia (`SaldoDiario`, PK = data) atualizada incrementalmente por evento, em vez de somar lançamentos a cada consulta. Cache em memória com TTL curto (10 s) e invalidação ao processar evento do dia.

**Alternativas:** agregação sob demanda (custo cresce com o volume); Redis desde já (melhor com várias réplicas, mas adiciona infraestrutura – previsto como evolução).

**Consequências:** leitura O(1). Com várias réplicas, o cache em memória é por instância; o TTL curto limita a defasagem a alguns segundos. Redis compartilhado remove essa defasagem.
