# Segurança

Este documento descreve a segurança no consumo das APIs, para atender ao requisito RNF06 de [001-requisitos.md](001-requisitos.md).

Legenda do estado: **Implementado** existe na versão atual; **Evolução** é um próximo passo; **Alvo** é o esperado em produção.

## 1. Controles de segurança

| Camada | Controle | Estado |
|---|---|---|
| Autenticação | JWT Bearer (issuer, audience e assinatura validados) em `/lancamentos`, `/consolidado` e `/logs` | **Implementado** (chave simétrica de desenvolvimento) |
| Autorização | Escopos ou roles por operação (`lancamentos:write`, `consolidado:read`) | Evolução |
| Identidade | OIDC com IdP (Entra ID ou Keycloak), chaves assimétricas (RS256) via JWKS e tokens curtos | Alvo |
| Serviço a serviço | Não há chamadas síncronas entre serviços; o broker usa usuário e senha próprios por serviço, com permissões mínimas (publish ou consume) | Alvo |
| Transporte | TLS em todos os saltos (gateway → serviços, serviços → banco e broker) | Alvo |
| Borda | API Gateway com rate limiting por cliente, WAF e proteção contra DDoS | Alvo |
| Segredos | Key Vault ou variáveis de ambiente; **nunca** no repositório (os valores atuais são só para desenvolvimento local) | Alvo |
| Dados | Criptografia em repouso (TDE) e mínimo de dados pessoais; detalhes de LGPD na seção 3 | Alvo |
| Validação | Valor, tipo e tamanho da descrição validados no domínio; erros de domínio retornam 400, sem vazar detalhes internos | **Implementado** |
| CORS | Origens explícitas (`Cors:Origins`) | **Implementado** |
| Auditoria | Lançamentos imutáveis; `CriadoEm` e `EventId` rastreáveis | **Implementado** |

## 2. Login e identificação do usuário

- **Rotas:** cadastro em `POST /auth/registrar` e login em `POST /auth/login`, no serviço de Lançamentos. O Consolidado só valida o token.
- **Token:** JWT HS256 com validade de 8 horas, contendo `sub` (id do usuário), `name` e `email`.
- **Senhas:** PBKDF2-SHA256 com 210 mil iterações, salt por usuário e comparação em tempo constante. A resposta de login é idêntica para e-mail inexistente e para senha errada.
- **Autoria:** cada lançamento grava o `UsuarioId` e o `UsuarioNome` de quem o registrou, obtidos do token e nunca do corpo da requisição.
- **Sessão no front-end:** o token fica no `sessionStorage`, e some ao fechar a aba. Se a API responder 401, o usuário volta para a tela de login.
- **Usuário de demonstração:** em `Development` é criado `demo@verx.com` / `Demo@12345` (`Auth:SeedDemoUser`).

## 3. Dados sensíveis e LGPD

**Dados pessoais tratados:** nome e e-mail do comerciante (cadastro) e a autoria de cada lançamento (`UsuarioId` e `UsuarioNome`). Não são coletados CPF, endereço nem dados bancários; os lançamentos são dados financeiros do negócio.

| Tema | Prática | Estado |
|---|---|---|
| Base legal | Execução de contrato (art. 7º, V) para cadastro e autoria; legítimo interesse para logs de segurança | Alvo |
| Minimização | Apenas nome, e-mail e senha (em hash); o evento de integração **não** carrega dados pessoais, só o id do lançamento | **Implementado** |
| Senhas | PBKDF2-SHA256, 210 mil iterações, salt por usuário; nunca armazenadas nem logadas em texto | **Implementado** |
| Em trânsito | TLS 1.2 ou superior em todos os saltos; HSTS no gateway | Alvo |
| Em repouso | TDE no SQL Server e criptografia dos discos e backups; para campos pessoais, criptografia de coluna (Always Encrypted) | Alvo |
| Chaves e segredos | Key Vault com rotação periódica; chaves separadas por ambiente; nada no repositório | Alvo |
| Logs | Sem senha, token ou corpo de requisição; e-mail mascarado (`d***@verx.com`); correlation-id em vez de dado pessoal | Evolução |
| Retenção | Logs por 30 a 90 dias; lançamentos conforme a obrigação fiscal e contábil (em geral 5 anos); dados de cadastro até o encerramento da conta | Alvo |
| Direitos do titular | Acesso, correção, portabilidade e eliminação; na exclusão, os dados pessoais são anonimizados e os lançamentos mantidos pela obrigação legal | Evolução |
| Acesso interno | Menor privilégio, acesso ao banco por identidade gerenciada e trilha de auditoria | Alvo |
| Incidentes | Plano de resposta com comunicação à ANPD e aos titulares em prazo razoável | Alvo |
| Governança | Encarregado (DPO), registro das operações de tratamento (ROPA) e RIPD, se exigido | Alvo |

## 4. Limitações conhecidas
O alvo para resolvê-las é um IdP com OIDC.
- Sem rate limiting nem bloqueio por tentativas de login.
- Sem refresh token nem revogação de tokens.
- Sem confirmação de e-mail.
- Chave de assinatura simétrica compartilhada entre os serviços.
