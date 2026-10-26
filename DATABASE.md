# Banco e migrations

SQLite local, foreign_keys ligado na conexão, timeout de 30 s e concorrência otimista via Revision. `WgsDbContext` é scoped: não compartilhar entre jobs/threads. Uma instância por pasta de dados reduz risco durante migrations; esse mecanismo não substitui um servidor multiusuário futuro.

## Tabelas

| Entidade | Finalidade / unicidade |
|---|---|
| Organization | Escritório, nome e identificador persistente |
| Company | Cadastro; CNPJ único por organização |
| Certificate | Metadados e referência segura; nenhum PFX/senha |
| CertificateAssociation | Certificado por empresa e provider |
| DocumentProvider | Configuração de provider, código único por organização |
| FiscalDocument | XML principal; organização + ambiente + chave únicos |
| FiscalDocumentCompany | Vínculos de participantes com o XML central |
| FiscalDocumentEvent | Chave + ambiente + tipo + sequência; nota opcional |
| DocumentSyncState | Empresa + provider + modelo + ambiente + papel + modo |
| ProcessingJob | Operação grande, status e totais |
| ProcessingJobItem | Item de job, disponibilidade e código de falha |
| KnownDocumentKey | Chaves conhecidas por empresa para futura cobertura |
| AuditLog | Ação enumerada, entidade e correlação |
| ApplicationSetting | Configuração não secreta por organização |

Todas as entidades relevantes contêm OrganizationId. Query filters isolam leituras; SaveChanges valida a organização de entradas modificadas/excluídas e revisa Revision. FKs compostas impedem vínculos com empresa, certificado, documento ou job de outro escritório, mesmo que um ID seja conhecido. IgnoreQueryFilters deve ser reservado à manutenção auditada; não é uma autorização.

CompanyId não é campo singular no documento: uma NF-e pode envolver duas empresas cadastradas. Papel é armazenado no vínculo. Cada empresa tem seu próprio SyncState.

## Evolução

Inicialização usa migrations, nunca EnsureCreated. Snapshot é versionado. Comandos de evolução no DEVELOPMENT.md. Sempre conservar banco e XMLs; nunca pedir que o usuário apague o banco para atualizar. Antes de futuras migrations destrutivas, acrescentar backup consistente (SQLite online backup, não cópia simples de banco ativo).

A migration inicial não insere chaves fiscais, notas ou empresas demonstrativas. O bootstrap insere organização e um registro de auditoria, em transação, somente na primeira abertura. Fases futuras devem adicionar regras de estados de job, validação fiscal e reconciliação de arquivos; não são implementadas por este schema.

## Fase 2

CompanySearchIndex adiciona SearchText e preenche cadastros antigos usando normalização de acentos comuns do português. LegalName/CNPJ/cidade originais são preservados. CompanyService atualiza esse campo em cada gravação de cadastro. Não há alterações destrutivas. Novos AuditAction foram acrescentados ao final, mantendo os valores numéricos antigos.

Importação grava Company, auditoria e checkpoint de ProcessingJobItem na mesma transação por linha. ProcessedItems conta todas as linhas concluídas, incluindo pendências; erro temporário marca job Failed e cancelamento marca Cancelled. Uma nova análise/importação ignora empresas já cadastradas.

## Fase 3

CertificatePurpose acrescenta `Certificate.Use`: 0 empresa participante; 1 escritório/contador. Preserva metadados e associações anteriores. O banco passa a três migrations. ApplicationSetting contém fila não secreta `CertificateCleanup/<GUID>` com referência de blob pós-commit. Nenhuma tabela/empresa/XML é apagada ao atualizar.

Associação única por organização + empresa + fonte. Outro certificado exige remoção explícita do vínculo anterior. Associate/Unlink incrementam revisão do certificado. Substituição preserva ID e associações; remoção retira vínculos, preserva empresas/acervo e agenda limpeza do blob. Auditoria usa apenas ações enumeradas/códigos seguros.

## Fase 4

Nenhuma alteração no schema: continuam três migrations. Inspeção de chave/XML não salva documentos, eventos, cursores, certificados ou auditoria fiscal. Os contratos do parser serão consumidos pelo futuro importador, que deverá transacionar arquivo/indexação/deduplicação e tratar pendências de confiança/protocolo.

## Fase 5 — Quarta migration

ArchiveFilesAndSearch acrescenta ArchiveFiles e campos de pesquisa/valor em centavos/data original/status declarado/resposta nos documentos; acrescenta modelo/resposta/recebimento/data original nos eventos. ArchiveFiles é única por organização+SHA-256 e tem exatamente um proprietário (documento OU evento). FKs compostas impedem referências entre organizações.

Migration preserva dados e copia Status para DeclaredStatus. No bootstrap, ArchiveMaintenance preenche índices de registros legados em lotes de 500, usando decimal C# exato para centavos e normalização sem acentos. Não converte dinheiro a double/float SQL. Data local legada usa a data UTC que existia; o offset original ausente não é inventado. Valores antigos com fração de centavo/overflow não recebem índice de centavos. Arquivos e referências antigos não são movidos/apagados automaticamente; a integridade relata caminhos incompatíveis/ausentes para reimportação segura.

Jobs de importação do acervo usam Kind=Import e auditoria ArchiveImportStarted, com ProviderCode LOCAL_ARCHIVE nos itens. Checkpoint e cadastro do XML são transacionais por item. Bootstrap identifica jobs do acervo interrompidos, marca Failed/ARCHIVE_INTERRUPTED e preserva progresso. Nova importação é idempotente; não há retomada automática por posição do ZIP.

## Fase 6 — Quinta migration

NfeDistributionInbox cria NfeDistributionReceipts com FK composta de empresa/organização, filtro tenant e Revision. Armazena bytes originais SOAP e SHA-256, NSU solicitado, filtros, chave pontual, Applied/cStat/código. Recibos aplicados e pendentes permanecem retidos; sem expurgo automático. Documento/evento/versões/vínculos/chaves conhecidas, auditoria e avanço de DocumentSyncState usam uma transação por página. ApplicationSettings recebe bloqueio não secreto por fonte/ambiente/certificado após 656/657. DocumentSyncStates preserva as dimensões antigas; Nacional aceita somente Either+FullXml e evita criar fluxos concorrentes por filtro.

Atualização normal aplica cinco migrations; não apagar banco. Backup deve incluir SQLite e todo o diretório de XML/cofre, no mesmo perfil Windows para DPAPI. Ver docs/NFE.md para retomada e retenção.

## Fase 7 — Sexta migration

CteDistributionInbox adiciona Participation (texto vazio para vínculos antigos) e cria CteDistributionReceipts e CteDistributionItems, com revisão, query filter por organização, FKs compostas e índice único por recibo/NSU. Respostas SOAP e XMLs descompactados ficam retidos como BLOB, sem PFX/senha ou cifra própria; não há expurgo automático.

DocumentSyncState com fonte CTE, Model=57, Actor=Either, PayloadMode=FullXml é o único representante da sequência nacional compartilhada por 57/67/outros; não criar um fluxo de modelo 67 separado. A posição é checkpoint de recebimento durável. Itens Archived=false continuam pendentes até reprocessamento; Available nunca é inferido da existência do BLOB. ApplicationSettings recebe bloqueio CteBlock/ambiente/certificado após 656.

A atualização aplica seis migrations sem apagar banco. Exportar esquema não é mandar recriar o banco do usuário. Backup deve incluir SQLite, XML e cofre do mesmo perfil Windows.

## NFC-e — Fase 8

Sem migration adicional (continuam seis). NfeDistributionReceipts recebe ProviderCode=SEF_SC_NFCE; o provider determina modelo 65 ao reaplicar o recibo. DocumentSyncStates usa Model=NFCe e o provider específico, com ambiente/ator/conteúdo próprios. ProcessingJobItems registra modelo 65. Os dados anteriores, enums e constraints permanecem compatíveis. Respostas SOAP são retidas em BLOB sem expurgo/criptografia adicional.

## NFCom — Fase 9

Sem migration adicional (seis existentes). ProviderCode=NFCOM no inbox e estados; Model=NFCom (62) em estados e jobs. Conhecidas/arquivos/eventos usam chave+ambiente e fonte original como nos demais modelos. Eventos de ajuste/substituição/cofaturamento permanecem distintos no índice de eventos; não são convertidos a cancelamento da nota original. Não há coluna nova para grafo de notas relacionadas. XML e SOAP originais preservam seus detalhes.

## Fase 10 — Migration HistoricalRecovery

20261018165457_HistoricalRecovery adiciona HistoricalRecoveryBatches (job, nome de origem, ambiente e reservas pagas) e HistoricalRecoveryItems (linha, chave/modelo/emitente/ano/mês, empresa opcional, estado/disponibilidade/motivo, provedor/job externo, tentativas/próxima consulta, retorno/hash). Sete migrations no total.

FKs compostas por organização para job/batch/empresa, índice único lote+linha e check de chave nula ou 44 caracteres. Uma empresa com linha histórica não pode ser excluída nem trocar CNPJ. Dados anteriores são preservados. Payload é JSON limitado de RetrievedPayloads (XMLs em base64), armazenado em BLOB antes da incorporação; não contém credenciais. Não há expurgo/criptografia adicional do SQLite. Budget é reserva de chamadas, não comprovante de cobrança ou valor monetário.

## Fase 11 — exportação

Sem mudança de schema: continuam sete migrations. JobKind.Export usa ProcessingJobs/ProcessingJobItems existentes; auditorias Started/Prepared/Completed/Failed/Cancelled acompanham o job. Reinício marca operações Running como interrompidas para conferência do pacote, sem repetir nem apagar arquivos automaticamente. A exportação não altera NSU, XMLs originais, certificados ou revisão dos documentos.

## Fase 12 — agendamentos

Oito migrations cumulativas. AutomaticSynchronization adiciona SyncSchedules (empresa, fonte, ambiente, filtros, hora/fuso, próxima execução, revisão/estado) e SyncScheduleRuns (tentativas com início/fim, código e job fiscal). FKs compostas tenant protegem empresa, agendamento e job. Índices atendem vencimentos e últimos resultados; horário/limite de lotes têm check constraint. Não altera XML/NSU anteriores. Agendamentos com histórico são pausados em vez de excluídos; empresa/fonte/ambiente/filtros ficam fixos após a primeira execução, para conservar a identificação correta do histórico. A existência de agendamento bloqueia exclusão da empresa e troca de CNPJ.
