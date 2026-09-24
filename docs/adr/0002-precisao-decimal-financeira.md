# ADR 0002 — Precisão decimal dos valores financeiros

- **Status:** Aceito
- **Data:** 2026-08-12
- **Contexto normativo:** *Documento Técnico de Arquitetura Geral v2.0*, seção 10 — Regras de Desenvolvimento e Segurança

## Regra original

> **SEGURANÇA — Precisão Financeira:** O PostgreSQL deve utilizar o tipo `DECIMAL(10,2)` para transações financeiras. O uso de `FLOAT` é expressamente proibido.

## Decisão

A proibição de `FLOAT` é mantida integralmente e sem exceção. A escala fixa `(10,2)` é **substituída** por uma tabela de precisões por natureza de grandeza:

| Natureza | Precisão | Onde se aplica |
|---|---|---|
| Valor monetário | `DECIMAL(18,2)` | Receita bruta, taxas, repasses, débitos/créditos contábeis, despesas, base tributável, valor de imposto |
| Quantidade de estoque | `DECIMAL(18,4)` | `QuantityOnHand`, quantidade em movimentos e itens de NF-e |
| Custo unitário | `DECIMAL(18,6)` | `AverageUnitCost`, `UnitCost` de entradas e baixas de CMV |
| Alíquota | `DECIMAL(9,6)` | Alíquotas de regras fiscais e apurações |

## Justificativa

**1. `(10,2)` tem teto de R$ 99.999.999,99.** O limite não é por transação, mas por coluna — e o mesmo tipo alimenta acumuladores do razão contábil e a base tributável anual consolidada. Um tenant de porte médio no Lucro Presumido ultrapassa esse teto no acumulado; o resultado seria um erro de *numeric field overflow* no fechamento, não um arredondamento. `(18,2)` remove a classe inteira de falha e continua sendo decimal exato.

**2. Custo médio ponderado com 2 casas corrompe o CMV.** O cálculo em `InventoryCostService` é:

```
novoCusto = (quantidadeAnterior × custoAnterior + quantidadeEntrada × custoEntrada) / novaQuantidade
```

Arredondar esse quociente para 2 casas a cada entrada faz o erro se acumular a cada compra, e o CMV é justamente o número que determina o lucro distribuível com isenção de IRPF. Com `(18,6)` o custo unitário preserva precisão; o arredondamento para 2 casas acontece **uma única vez**, no lançamento contábil do valor total.

**3. Quantidade fracionária é requisito de negócio.** NF-e de fornecedor emite quantidades em unidades fracionárias (kg, metro, litro). `(18,4)` acompanha o padrão da própria NF-e.

**4. Alíquotas não são valores monetários.** Uma alíquota de Simples Nacional como 0,0605 (6,05%) exige mais de 2 casas para ser representada; `(9,6)` cobre as faixas com folga.

## Consequências

- A regra passa a ser lida como: **"todo valor financeiro usa `DECIMAL` de escala explícita; `FLOAT` e `DOUBLE` são proibidos em qualquer coluna ou campo financeiro"** — o que preserva a intenção original (exatidão decimal), com escalas dimensionadas por grandeza.
- Nenhuma migração de dados é necessária: o schema já está em `(18,2)`/`(18,4)`/`(18,6)`.
- O único `double` do domínio é `AzureAiChatOptions.Temperature`, que é parâmetro de inferência do modelo de linguagem e não representa valor financeiro.
- **Pendência:** o documento no Notion (seção 10) ainda diz `DECIMAL(10,2)`. Este ADR não substitui a atualização da fonte normativa — ver texto sugerido abaixo.

## Texto sugerido para a seção 10 do Notion

> **SEGURANÇA — Precisão Financeira:** valores monetários usam `DECIMAL(18,2)`; quantidades de estoque, `DECIMAL(18,4)`; custos unitários, `DECIMAL(18,6)`; alíquotas, `DECIMAL(9,6)`. O uso de `FLOAT` ou `DOUBLE` em qualquer campo financeiro é expressamente proibido.

## Verificação

`ConnectorLayerTests`, `InventoryCostServiceTests` e `FiscalFoundationTests` exercitam os cálculos com esses tipos. Uma varredura por `float`/`double` em `Avallo.Web/Domain` e `Avallo.Web/Features` deve retornar apenas `AzureAiChatOptions.Temperature`.
