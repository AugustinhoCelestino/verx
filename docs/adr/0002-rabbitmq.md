# ADR-0002: Mensageria assíncrona com RabbitMQ

**Status:** aceito

**Contexto:** a integração entre os serviços não pode ser síncrona (RNF01) e não pode perder eventos (RNF04).

**Decisão:** RabbitMQ com exchange *topic* durável, fila durável, mensagens persistentes, *publisher confirms*, confirmação manual de processamento e DLQ. Ambos os serviços declaram a topologia (`RabbitTopology`), assim a fila existe mesmo com o Consolidado parado.

**Alternativas:** Kafka (excelente para alto volume/replay, operação mais pesada que o necessário); chamada HTTP síncrona (viola RNF01); polling de banco compartilhado (acopla os contextos).

**Consequências:** entrega *at-least-once* → consumidor idempotente (ADR-0003). Em nuvem pode ser trocado por Service Bus sem alterar o domínio.
