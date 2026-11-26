# Requisitos

## 1. Contexto
O comerciante precisa controlar o fluxo de caixa diário (créditos e débitos) e consultar o saldo consolidado de cada dia. O sistema é composto por dois serviços: **Lançamentos** e **Consolidado**.

## 2. Requisitos funcionais

| ID | Requisito |
|---|---|
| RF01 | Registrar um lançamento com tipo (crédito ou débito), valor maior que zero (2 casas decimais), data (padrão: hoje) e descrição opcional. |
| RF02 | Listar os lançamentos de uma data. |
| RF03 | Consultar o saldo consolidado de uma data: total de créditos, total de débitos, saldo e quantidade de lançamentos. |
| RF04 | Consultar os saldos consolidados de um período. |
| RF05 | Retornar saldo zero para um dia sem movimento. |
| RF06 | Cadastro e login do comerciante, com identificação do usuário em cada lançamento registrado. |
| RF07 | Disponibilizar uma interface web para registrar lançamentos e visualizar o consolidado. |
| RF08 | Disponibilizar na interface web um menu com as telas de lançamentos e de consulta de logs. |
| RF09 | Consultar os logs de cada serviço, com filtro por nível e texto. |

## 3. Requisitos não funcionais

| ID | Requisito | Meta |
|---|---|---|
| RNF01 | **Isolamento de falha:** o serviço de Lançamentos não pode ficar indisponível se o Consolidado cair. | Disponibilidade da escrita independente do Consolidado. |
| RNF02 | **Pico de leitura:** o serviço de Consolidado deve suportar 50 requisições por segundo. | No máximo 5% de perda de requisições (pelo menos 95% de sucesso). |
| RNF03 | **Consistência:** o consolidado pode ser eventualmente consistente. | O saldo reflete os lançamentos em poucos segundos. |
| RNF04 | **Durabilidade:** nenhum lançamento registrado pode ser perdido. | Zero perda de lançamentos confirmados. |
| RNF05 | **Idempotência:** o reprocessamento de um mesmo lançamento não pode alterar o saldo duas vezes. | Cada lançamento é contabilizado uma única vez. |
| RNF06 | **Segurança:** o acesso às rotas de negócio e de logs exige autenticação. | Token JWT válido em todas as rotas, exceto login, cadastro e health. |
| RNF07 | **Observabilidade:** os serviços expõem saúde, métricas e logs. | Rotas de health, de métricas e de consulta de logs disponíveis. |
| RNF08 | **Escalabilidade:** os serviços devem poder escalar horizontalmente. | Serviços sem estado local necessário. |

## 4. Tecnologias e ambiente
- Back-end em .NET e front-end em Angular.
- Deve existir um modo de execução local sem Docker, para demonstração.

## 5. Escopo
- Um comerciante e uma conta de caixa nesta versão. O suporte a vários comerciantes é uma evolução.
- A data do lançamento é a data contábil informada; o fechamento do dia usa UTC.
- Lançamentos são imutáveis; um estorno é um novo lançamento (evolução).
- A consistência eventual no consolidado é aceitável para o negócio, pois o uso é um relatório diário.
