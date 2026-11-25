# 4. Segurança e observabilidade

## 4.1 Segurança no consumo das APIs

| Camada | Controle | Estado |
|---|---|---|
| Autenticação | JWT Bearer (issuer, audience e assinatura validados) em `/lancamentos` e `/consolidado` | **Implementado** (chave simétrica de dev) |
| Autorização | Escopos/roles por operação (`lancamentos:write`, `consolidado:read`) | Evolução |
| Identidade | OIDC com IdP (Entra ID/Keycloak), chaves assimétricas (RS256) via JWKS, tokens curtos | Alvo |
| Serviço-a-serviço | Não há chamadas síncronas entre serviços; o broker usa usuário/senha próprios por serviço com permissões mínimas (publish vs. consume) | Alvo |
| Transporte | TLS em todos os saltos (gateway → serviços, serviços → BD/broker) | Alvo |
| Borda | API Gateway com rate limiting por cliente, WAF, proteção DDoS | Alvo |
| Segredos | Key Vault / variáveis de ambiente; **nunca** no repositório (os valores atuais são só para dev local) | Alvo |
| Dados | Criptografia em repouso (TDE), mínimo de dados pessoais (não há PII nesta versão) | Alvo |
| Validação | Valor, tipo, tamanho de descrição validados no domínio; erros de domínio → 400, sem vazar detalhes internos | **Implementado** |
| CORS | Origens explícitas (`Cors:Origins`) | **Implementado** |
| Auditoria | Lançamentos imutáveis; `CriadoEm` e `EventId` rastreáveis | **Implementado** |

O endpoint `/dev/token` foi substituído por login real (`POST /auth/login`). Detalhes da implementação atual:

- Cadastro (`POST /auth/registrar`) e login (`POST /auth/login`) no serviço de Lançamentos; o token (JWT HS256, 8 h) leva `sub` (id do usuário), `name` e `email`.
- Senhas com PBKDF2-SHA256 (210 mil iterações, salt por usuário, comparação em tempo constante); a resposta de login é idêntica para e-mail inexistente e senha errada.
- Cada lançamento grava o `UsuarioId`/`UsuarioNome` de quem o registrou (vem do token, nunca do corpo da requisição).
- Em `Development` é criado o usuário demo `demo@verx.com` / `Demo@12345` (`Auth:SeedDemoUser`).
- Limitações conhecidas (alvo: IdP/OIDC): sem rate limiting/bloqueio por tentativas, sem refresh token/revogação, sem confirmação de e-mail e chave de assinatura simétrica compartilhada entre os serviços.

## 4.2 Monitoramento e observabilidade

**Implementado**
- `GET /health/live` (processo) e `GET /health/ready` (inclui banco) em ambos os serviços → probes de orquestrador.
- `GET /metrics` (Prometheus via OpenTelemetry: taxa, latência e erros HTTP).
- `docker-compose` com Prometheus em `http://localhost:9090`.
- Logs estruturados do ASP.NET Core; erros do consumer incluem delivery tag e flag de reentrega.

**SLIs/SLOs sugeridos**
| SLI | SLO |
|---|---|
| Disponibilidade `POST /lancamentos` | 99,9% (independente do Consolidado) |
| Sucesso `GET /consolidado` em pico | ≥ 95% (requisito) – meta interna 99% |
| Latência p95 `GET /consolidado` | < 500 ms |
| Atraso de consolidação (publicação → saldo atualizado) | p95 < 10 s |

**Alertas recomendados:** profundidade da fila `consolidado.lancamento-registrado` crescente; mensagens na DLQ > 0; mensagens do outbox pendentes há > 1 min; taxa 5xx > 1%; readiness falhando.

**Evolução:** traces distribuídos (OpenTelemetry + `traceparent` nas mensagens), dashboards Grafana, métricas de negócio (lançamentos/min, atraso de consolidação), logs centralizados.
