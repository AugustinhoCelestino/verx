# ADR-0001: Dois serviços orientados a eventos (em vez de monolito)

**Status:** aceito

**Contexto:** RNF01 exige que Lançamentos continue disponível se o Consolidado cair; RNF02 exige escalar leitura (50 req/s) de forma independente.

**Decisão:** dois serviços independentes (Lançamentos e Consolidado), cada um com seu banco, integrados por eventos. CQRS leve: o Consolidado é um modelo de leitura pré-agregado.

**Alternativas:** monolito modular (mais simples, mas falha e escala acopladas); serverless (bom para picos, mas o consumo contínuo de fila e o controle de custo/latência a frio agregam pouco aqui).

**Consequências:** + isolamento de falhas e escala independente; − consistência eventual, mais componentes operacionais. Aceito porque o relatório diário tolera segundos de atraso.
