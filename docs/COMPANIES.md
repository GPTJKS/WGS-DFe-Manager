# Empresas — Fase 2

## Cadastro

Obrigatórios: razão social, CNPJ válido e UF. O CNPJ aceita os 14 caracteres, com máscara `XX.XXX.XXX/XXXX-DD` ou sem máscara; letras são normalizadas para maiúsculas. O algoritmo de DV usa ASCII - 48 e módulo 11, conforme https://www.gov.br/receitafederal/pt-br/centrais-de-conteudo/publicacoes/documentos-tecnicos/cnpj/manual-dv-cnpj.pdf (consultado em 07/10/2026). O exemplo oficial `12.ABC.345/01DE-35` é coberto por teste, junto com CNPJs numéricos.

Nome fantasia, IE, município e observações são opcionais. A inscrição estadual é texto, pois formatos/regras variam por UF; não se declara validação de IE nesta fase. Município é texto manual; nenhuma consulta externa ou código IBGE é inventado.

Documentos habilitados são independentes. Nenhum documento habilitado é permitido para manter empresa apenas no cadastro. Certificados são associados pelo módulo Certificados A1. Situação ativa/inativa é controle interno do escritório, não situação fiscal oficial.

Subpasta é opcional e relativa ao acervo; em branco fica a organização padrão futura. Não aceita caminhos absolutos, `..`, componentes vazios, nomes reservados (CON, AUX, NUL etc.) ou caracteres inválidos para Windows. A pasta é apenas configuração nesta fase, sem movimentar acervo físico.

Editar salva com Revision: se houver alteração concorrente, exige recarregar o cadastro. Não permite trocar CNPJ de empresa com vínculos/acervo/histórico; nesses casos cadastrar outra empresa. Excluir bloqueia dependências e não apaga XML. Inativar preserva todas as informações.

## Pesquisa

Pesquisa por nome, fantasia, CNPJ (com/sem máscara) e município; termos separados por espaço precisam aparecer no texto do cadastro. Acentos e caixa são normalizados. Lista paginada em 50 registros, com filtros Todas/Ativas/Inativas. O índice de pesquisa é atualizado ao criar/editar; a migration preenche cadastros da Fase 1 sem modificar os dados originais.

## Layout de importação

Primeira linha não vazia deve ser cabeçalho. Obrigatórios: **CNPJ, RazaoSocial, UF**. Aceita também títulos com espaços/acentos, por exemplo Razão Social e Inscrição Estadual. Colunas adicionais desconhecidas são ignoradas.

| Coluna do modelo | Obrigatória | Valores |
|---|---|---|
| CNPJ | Sim | 14 caracteres, numérico/alfanumérico, com ou sem máscara |
| RazaoSocial | Sim | Até 300 caracteres |
| NomeFantasia | Não | Até 300 caracteres |
| InscricaoEstadual | Não | Até 40 caracteres |
| UF | Sim | Uma das 27 siglas brasileiras |
| Municipio | Não | Até 150 caracteres |
| Ativa | Não | Sim/Não ou Ativa/Inativa; em branco = Sim |
| Documentos | Não | NFe\|NFCe\|CTe\|CTeOS\|NFCom; em branco = Todos; Nenhum desabilita todos |
| Pasta | Não | Subpasta relativa, até 200 caracteres |
| Observacoes | Não | Até 4.000 caracteres |

CSV: UTF-8 com/sem BOM ou Windows-1252; separador `;` ou `,` detectado pelo cabeçalho; aspas duplas, escapes de aspas e campos com quebra de linha são suportados. Prefira `;` e o modelo fornecido. O relatório indica o número físico da primeira linha do registro, inclusive quando um campo ocupa várias linhas.

XLSX: lê **a primeira aba visível**, identificada na prévia. Cabeçalho nela, no máximo 32 colunas usadas. Suporta células de texto direto/compartilhado e números inteiros. CNPJ numérico de 14 posições pode ser lido sem perda; CNPJ com zero inicial deve ser armazenado como **Texto** no Excel. Não completa automaticamente CNPJ que perdeu dígitos. Fórmulas/erros nas colunas importadas tornam a linha inválida, mesmo com resultado armazenado aparentemente válido. Não calcula fórmulas e não acessa referências externas.

Limites: arquivo até 20 MB; XLSX expandido até 64 MB; 2.000 entradas ZIP; parte XML até 20 MB; 10.000 empresas por arquivo; 32 colunas. Dividir volumes maiores. `.xls`, arquivos protegidos por senha e `.xlsm` não são aceitos.

## Conferência e execução

1. Escolher arquivo e ler a prévia. A análise não grava empresas.
2. Conferir prontas, inválidas e duplicadas; salvar relatório se necessário.
3. Clicar **Importar linhas prontas**. Linhas inválidas/repetidas não impedem as válidas.
4. Salvar relatório final. Corrigir as pendências no arquivo original e reanalisar.

Nunca sobrescreve cadastro existente com dados da planilha. Cancelamento conclui a transação já confirmada; a transação em andamento é revertida quando cancelada. Para retomar, analisar novamente o mesmo arquivo: cadastros concluídos são duplicadas no banco, e as restantes continuam prontas. Não existe botão de retomar um job antigo; esse mecanismo seguro de reimportação evita repetição.

Cada operação gera ProcessingJob e cada linha concluída gera ProcessingJobItem persistente. O histórico está no banco; a tela geral de histórico de jobs ainda não faz parte da Fase 2. Empresas importadas ficam vinculadas ao histórico, portanto devem ser inativadas quando não utilizadas.

O CSV de relatório neutraliza textos que poderiam virar fórmulas no Excel. É relatório, não modelo de reimportação. Ao abrir no Excel, configure a coluna CNPJ como texto para preservar zeros iniciais.

Versão 0.3.0: **Certificados A1 → Associar às empresas** gerencia vínculos locais. Empresa com vínculo continua protegida contra exclusão/troca de CNPJ; inativar mantém os vínculos.
