# Riscos

Mapa dos principais riscos da solução, com probabilidade (P) e impacto (I) em escala Baixa, Média ou Alta. A severidade é a combinação das duas. Os requisitos relacionados estão em [001-requisitos.md](001-requisitos.md).

## 1. Matriz de riscos

| ID | Risco | P | I | Severidade | Mitigação | Req. |
|---|---|---|---|---|---|---|
| R01 | Consolidado indisponível durante o horário de pico | Média | Alta | **Alta** | Eventos ficam no broker e são aplicados depois; leitura com cache; réplicas e autoescala | RNF01, RNF02 |
| R02 | Perda de lançamento entre o banco e o broker | Baixa | Alta | **Média** | Outbox transacional: o evento é gravado na mesma transação do lançamento e republicado até confirmar | RNF04 |
| R03 | Evento entregue em duplicidade e saldo errado | Alta | Alta | **Alta** | Idempotência por `EventId` e concorrência otimista no saldo | RNF05 |
| R04 | Divergência entre o saldo consolidado e os lançamentos | Média | Alta | **Alta** | Rotina de conferência diária (soma dos lançamentos × saldo) com alerta; reprocessamento por replay | RNF03 |
| R05 | Vazamento de dados pessoais ou de credenciais | Baixa | Alta | **Média** | Criptografia, segredos em cofre, mascaramento em logs, mínimo de dados (ver [008-seguranca.md](008-seguranca.md)) | RNF06 |
| R06 | Abuso ou força bruta no login | Média | Média | **Média** | Rate limiting no gateway, bloqueio por tentativas, WAF (evolução) | RNF06 |
| R07 | Falha na migração do legado (dados divergentes) | Média | Alta | **Alta** | Strangler Fig, execução paralela e reconciliação antes da virada (ver [005-transicao-legado.md](005-transicao-legado.md)) | — |
| R08 | Fila acumulada (broker lento ou consumidor parado) | Média | Média | **Média** | Alerta de tamanho da fila e de idade da mensagem; dead-letter; consumidores escaláveis | RNF03 |
| R09 | Custo de nuvem acima do previsto | Média | Média | **Média** | Orçamento e alertas de custo; dimensionamento revisado a cada trimestre (ver [006-estimativa-custos.md](006-estimativa-custos.md)) | — |
| R10 | Incidente sem causa identificável | Média | Média | **Média** | Correlation-id nos dois serviços, logs, métricas e health (ver [007-monitoramento.md](007-monitoramento.md)) | RNF07 |
| R11 | Mudança de contrato do evento quebra o consumidor | Baixa | Média | **Baixa** | Contrato versionado em `Verx.Contracts`; campos novos sempre opcionais | RNF01 |

## 2. Priorização
1. **Alta:** R01, R03, R04 e R07 entram no escopo da primeira entrega ou da migração, com teste e alerta.
2. **Média:** R02, R05, R06, R08, R09 e R10 têm mitigação definida e acompanhamento contínuo.
3. **Baixa:** R11 é tratada por convenção de versionamento.

## 3. Acompanhamento
Os riscos são revisados a cada fase da migração e após cada incidente. Um risco novo entra na matriz com responsável e mitigação.
