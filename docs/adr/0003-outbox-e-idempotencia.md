# ADR-0003: Transactional Outbox e consumidor idempotente

**Status:** aceito

**Contexto:** gravar no banco e publicar no broker são duas operações; sem coordenação, uma pode ocorrer sem a outra (evento perdido ou fantasma).

**Decisão:** o lançamento e a mensagem de outbox são gravados na **mesma transação**. Um `BackgroundService` publica pendentes e marca como publicados. No Consolidado, o `EventId` é gravado na mesma transação que atualiza o saldo, descartando duplicatas. Conflitos entre consumidores são resolvidos com concorrência otimista (`Versao`) e retentativa.

**Alternativas:** 2PC/DTC (frágil e pouco portável); publicar direto após o commit (perde eventos se o processo cair).

**Consequências:** + nenhum evento perdido, Lançamentos independente do broker; − latência de até ~1 s no polling e necessidade de limpeza periódica do outbox (evolução).
