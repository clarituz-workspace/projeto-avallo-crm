---
tags:
  - avallo
  - relatorios
  - exportacao
  - pdf
  - excel
  - csv
status: concluido
ultima-revisao: 2026-08-03
---

# Exportacao de Relatorios

> Motor central de exportacao para gerar documentos financeiros em formatos adequados para contabilidade, analise e integracoes externas.

## Checklist funcional

- [x] Motor unico de exportacao utilizado por todos os modulos
- [x] Exportacao em PDF com layout formatado para o contador
- [x] Exportacao em Excel (`.xlsx`) com aba consolidada e abas por marketplace
- [x] Exportacao em CSV para integracao com outros sistemas
- [x] Relatorio mensal consolidado com todos os marketplaces
- [x] Relatorio por plataforma com dados separados por marketplace

## Estado atual

O `ReportExportService` centraliza as exportacoes financeiras do painel nos formatos PDF, Excel e CSV. Ele recebe os mesmos filtros do dashboard, consulta apenas os dados do tenant autenticado e produz a resposta com nome, MIME type e conteudo do arquivo.

O contrato `IReportExportEngine` e a unica fronteira de renderizacao. O `ReportExportService` prepara os dados financeiros e delega PDF, Excel e CSV ao motor; o `PeriodClosingService` prepara o documento contabil e delega seu PDF ao mesmo motor. Nenhum modulo de negocio gera arquivos diretamente.

## Evidencias da implementacao

### PDF para contador

- Documento A4 em orientacao paisagem.
- Cabecalho com tenant, periodo, modo de visualizacao e data de geracao.
- Cards com Faturado, Recebido, Taxas e A receber.
- Resumo mensal consolidado.
- Tabela de lancamentos consolidada ou agrupada por plataforma.
- Numeracao de paginas e estado vazio.

### Excel

- Arquivo `.xlsx` gerado com ClosedXML.
- Aba `Consolidado` com totais mensais e total geral.
- Uma aba adicional para cada marketplace.
- Cabecalhos formatados, valores monetarios, filtros automaticos e linhas congeladas.
- Tratamento de nomes invalidos e duplicados nas abas.

### CSV

- Codificacao UTF-8 com BOM para compatibilidade com Excel e sistemas brasileiros.
- Separador `;`.
- Valores monetarios em formato decimal invariavel.
- Campos escapados para preservar aspas e caracteres especiais.
- Inclui data, ID externo, lancamento, plataforma, pagamento, status e valores financeiros.

### Modos de relatorio

| Modo | Resultado |
|---|---|
| `consolidated` | Todos os marketplaces reunidos em um relatorio |
| `platform` | Lancamentos agrupados e separados por marketplace |

### Seguranca e limites

- A exportacao exige usuario autenticado com acesso ao tenant.
- Os filtros globais do banco impedem dados de outro tenant no arquivo.
- O periodo e o formato sao validados no endpoint.
- O limite atual e de 50.000 lancamentos por exportacao.
- Formatos aceitos: `pdf`, `xlsx` e `csv`.

## Arquitetura concluida

- [x] Abstracao compartilhada `IReportExportEngine` extraida.
- [x] PDF produzido pelo `PeriodClosingService` migrado para o motor compartilhado.
- [x] Renderizacao encapsulada na implementacao `ReportExportEngine` para uso por futuros modulos.
- [x] Testes de integracao cobrindo PDF, XLSX e CSV nos modos `consolidated` e `platform`.
- [x] Teste arquitetural garantindo que o fechamento dependa da abstracao e nao possua gerador PDF proprio.

## Criterio de aceite

O modulo estara totalmente concluido quando PDF, Excel e CSV forem produzidos por um unico motor reutilizavel e nenhuma feature mantiver geracao paralela de relatorios. Todos os arquivos devem respeitar tenant, filtros, periodo e agrupamento selecionado.
