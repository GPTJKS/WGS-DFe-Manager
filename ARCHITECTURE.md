# Arquitetura inicial

## Dependências

| Projeto | Responsabilidade | Referências internas |
|---|---|---|
| WgsDfe.Domain | Entidades, enums, NSU e invariantes | Nenhuma |
| WgsDfe.Application | Contratos de providers, organização, logs e cofre | Domain |
| WgsDfe.Database | EF Core/SQLite, constraints, migrations, fábrica | Domain |
| WgsDfe.Providers | Registro dos adaptadores | Application |
| WgsDfe.Infrastructure | Bootstrap, DI, configuração, diretórios e log | Application, Database, Providers |
| WgsDfe.Desktop | Ponto de entrada e janela WPF | Infrastructure |
| WgsDfe.Tools | Inicialização/verificação por CLI | Infrastructure |
| WgsDfe.Tests | Testes da fundação | Infrastructure |

A interface usa Infrastructure somente no ponto de composição/inicialização. As futuras telas devem depender de serviços Application. Domain não conhece SQLite, WPF, HTTP ou credenciais.

## Decisões

1. .NET 10 LTS + EF Core 10, SDK 10.0.401 e pacotes 10.0.12, verificados em 07/10/2026. Versões centralizadas, sem intervalos flutuantes. Dependências transitivas ficam registradas em packages.lock.json.
2. Uma organização por instalação/pasta na V1. OrganizationId é dado persistente em configuração; não é recriado a cada abertura. WGS é configuração inicial, não regra espalhada nas camadas.
3. FiscalDocument é único por organização + ambiente + chave. FiscalDocumentCompany permite múltiplas empresas participantes sem duplicar o XML. Uma futura troca de escritório pode exigir armazenamento físico independente por organização.
4. Estado NSU também inclui ambiente, papel do participante e modo chaves/XML. Não reutilizar o cursor de uma listagem de chaves como se XMLs já tivessem sido armazenados. O limitador NF-e serializa chamadas no processo, com intervalo de 250 ms, e persiste bloqueios por requisitante; outras máquinas/aplicações exigem coordenação externa.
5. Documentos e eventos independentes: eventos podem chegar antes da nota e são vinculados posteriormente. Inutilização é evento/registro de faixa; não existe chave individual de NF-e inutilizada. Não gerar FiscalDocument fictício.
6. ProcessingJob + ProcessingJobItem representam download, histórico, importação e exportação por JobKind; tabelas separadas não são necessárias neste estágio.
7. Data/hora interna em UTC com DateTime. Datas de negócio/períodos usam DateOnly nos contratos. Importação preserva offset e bytes originais; NF-e nacional preserva docZip descompactado e SC preserva o SOAP recebido mais o elemento fiscal serializado. Horários de espera na tela NF-e são UTC explícitos.
8. Decimal é persistido como texto para não perder precisão em SQLite. Não usar ordenação/soma SQL desse texto como valor numérico. A Fase de pesquisa deve definir projeção monetária adequada ou agregação decimal segura; futura mudança de provider exige migration do tipo.
9. Chaves e CNPJ são strings compatíveis com alfanuméricos. DfeAccessKey valida formato por modelo e DV desde a Fase 4; isso não substitui autenticação/validação fiscal completa. Constraints iniciais apenas de tamanho não substituem esse parser.
10. Esta versão registra adaptadores NF-e SC/nacional reais e distingue XML integral de chaves/resumos. A rede é desabilitada por padrão; EnableExternalRequests=true ou habilitação explícita na sessão permite o fluxo fiscal.

## Persistência futura do lote

Gravar XML em arquivo temporário, validar, calcular hash e publicar de forma atômica sem sobrescrever. Em uma transação SQL, indexar todos os documentos/eventos e avançar NSU somente após processar o lote completo. Arquivo e banco não compartilham transação; planejar reconciliação de arquivos órfãos após crash. Falha parcial não avança cursor. Acervo implementado na Fase 5; coordenação do commit de páginas NF-e implementada na Fase 6.

## Fase 2 — Serviços de empresas

Contratos e validação em Application/Companies; CNPJ e normalização de pesquisa em Domain; implementações CompanyService, CompanyImportService e leitores em Infrastructure/Companies. EF permanece fora da interface. Cada operação da UI usa um scope próprio fora da thread da janela. Importação executa em background com progresso/cancelamento; não compartilha DbContext entre operações.

Leitura XLSX usa APIs .NET ZipArchive/XmlReader, sem dependência nova ou Excel instalado. Só lê a primeira aba visível. Não usa fórmulas nem URLs externas. CompanyFileReader é interno; testes o exercitam pelos serviços públicos.

## Fase 3 — Certificados

Application/Certificates: ICertificateService e ICertificateKeySource. Infrastructure/Certificates: serviço EF, codec PKCS#12 limitado e DPAPI. GUI usa scopes independentes em background. ICertificateKeySource devolve X509Certificate2 descartável com chave efêmera; o contrato permite futura fonte A3 sem exigir PFX. Produção sempre usa DPAPI, sem fallback. Nenhum provider externo é registrado.

## Fase 4 — Parser

DfeAccessKey e Cpf ficam em Domain, sem dependência de XML/EF/WPF. Contratos DfeParseResult/IDfeParser em Application/Parsing; implementação segura em Infrastructure/Parsing. DfeParser é stateless e registrado como singleton; UI executa leitura em background. CLI `key` e `parse` usam o mesmo domínio/parser e não inicializam banco/cofre.

Leitor usa preflight XmlReader com resolver nulo/DTD proibido e limites; XDocument usa as mesmas configurações. Seleção de campos por caminho direto e namespace, cardinalidade singular em campos críticos e rejeição de múltiplos grupos de identificação. Não há busca genérica por descendentes para escolher valores fiscais.

DfePayloadKind mantém documento, resumo e evento distintos. Environment é nullable para resumos sem tpAmb. Datas com DateTimeOffset preservam offset; valores com decimal e cultura invariável; hash dos bytes originais, sem reserializar. Status é apenas o declarado no XML, sem confiança criptográfica. Eventos não alteram a nota principal; isso dependerá da conciliação do acervo.

## Fase 5 — Acervo

IArchiveService em Application/Archive; ArchiveService e ArchiveFileStore/SourceReader em Infrastructure/Archive. DbContext por operação e gate singleton serializa importação, revinculação e integridade dentro do processo. Pesquisa usa scopes independentes. A trava de instância da fundação impede dois aplicativos na mesma pasta.

Arquivo publicado primeiro em nome derivado do hash, por staging+flush+move sem overwrite. Uma transação por item grava documento/evento, variante ArchiveFile, vínculos, auditoria e checkpoint do job. Falha SQL não apaga arquivos: pode deixar órfão, conferido/preservado pela integridade. Reimportação reutiliza arquivo íntegro; nunca sobrescreve conteúdo adulterado.

ArchiveFiles guarda todas as versões aceitas e divergentes; cada arquivo pertence a documento OU evento, com FKs compostas por organização. Versão principal não é promovida por divergência. Eventos são indexados antes da nota e ligados posteriormente pela mesma chave/ambiente. Cancelamento é somente situação declarada local, com critério restrito documentado.

CheckAvailabilityAsync confere arquivo, hash e correspondência do XML à chave/ambiente antes de retornar Available. Isso indica presença local, não autenticidade fiscal. Nenhum adaptador HTTP foi registrado. Os relatórios e a busca do acervo não alteram sincronização/NSU.

## Fase 6 — NF-e

Application/Nfe contém contratos de sincronização, credencial, transporte e página; Providers/Nfe contém adaptadores SOAP puros e codec. Infrastructure/Nfe resolve A1/identidade/cadeia, transporta mTLS e coordena SQLite/acervo/inbox. Registry passa a ser scoped e contém os dois providers; configuração desabilitada não oculta capabilities implementadas.

NfeSyncGate serializa sincronizações, NfeNetworkGate serializa HTTP (250 ms), ArchiveGate protege publicação/índice e é adquirido depois do gate de sincronização. A resposta é durável antes de ser aplicada; a transação por página inclui arquivos indexados, chaves, recibo, auditoria e cursor. Publicação física anterior ao commit pode deixar órfão; replay reaproveita. A1 nunca atravessa o contrato HTTP como XML. Interfaces de transporte/credencial/TimeProvider permitem ensaios sem rede. NfeWindow usa escopo por execução e cancelamento cooperativo. Ver docs/NFE.md.

## Fase 7 — CT-e / CT-e OS

Application/Cte contém coordenador/contratos da UI. Providers/Cte implementa SOAP 1.2, distNSU/consNSU e validação de retorno com descompactação segura compartilhada. Infrastructure/Cte contém o inbox/itens, participante do transporte e sincronização; a UI CteWindow usa escopo por chamada e permite reprocessar/pesquisar/visualizar preservados. O registry scoped tem três fontes fiscais. NfeSyncGate/NfeNetworkGate também serializam CT-e; ArchiveGate mantém ordem de aquisição única.

Uma posição CTE por empresa/ambiente representa os dois modelos. A transação por página inclui o registro de todos os itens, até os ainda não arquiváveis. Applied no recibo significa recebido duravelmente; Archived no item significa incorporado ao acervo. Os estados são distintos e aparecem na UI. Erro de envelope/NSU/armazenamento mantém o recibo não aplicado e o cursor anterior. Layout/modelo não suportado é preservado com bytes/hash e pendência explícita.

Reprocessamento usa snapshot de IDs, com progresso em grupos de 50; carrega um payload por vez e libera o ChangeTracker após cada item/página. ConsNSU não altera o NSU de distribuição; cache é conferido por conteúdo, modelo selecionado e integridade. CteParticipants resolve grupos diretos e toma3/toma4/tomaOS. Vínculos adicionais usam OtherParticipant e Participation, sem confundir tomador com destinatário.

## Fase 8 — NFC-e SC

O fluxo SC é compartilhado por implementação, com fonte/modelo explícitos. A NFC-e usa SEF_SC_NFCE, modelo 65 e flag NFCe, sem compartilhar cursor, associação de certificado ou recibos com SEF_SC_NFE. São quatro fontes fiscais no registry. NfeWireCodec parametriza namespace, raiz, versão, lote e operação; NFC-e envia distNFCeSC diretamente no elemento nfceDownloadContab, conforme WSDL. O armazenamento NfeDistributionReceipts é genérico para esses dois modelos por ProviderCode; não houve alteração de esquema.

A atomicidade NF-e se aplica à NFC-e: lote fiscal inconsistente não é parcialmente incorporado, e o cursor anterior é preservado com SOAP durável. As auditorias Nfe* / códigos NFE_* são nomes técnicos compartilhados; ProviderCode e Model distinguem a fonte nos jobs/cursores. Startup reconhece NFE_STARTED também para NFC-e e não faz HTTP.

## Fase 9 — NFCom SC

Fonte NFCOM no pipeline comum INfeDistributionProvider / INfeSyncService. Perfis SC substituem condicionais de wire por namespace/operação/raiz/versão/endpoint/payload permitido, com regressões NF-e/NFC-e. NFCom usa modelo 62, flag NFCom e certificado contador associado ao código NFCOM. O registry tem cinco fontes fiscais. NfeWindow recebe DocumentModel para resolver fonte fixa e avisos próprios.

DocumentSyncState e ProcessingJobItem registram modelo 62; NfeDistributionReceipt recebe ProviderCode=NFCOM e é reaplicado com o modelo dessa fonte. Não há cursor ou recibo compartilhado com NF-e/NFC-e/CT-e. A transação integral por página, hash, cooldown de incerteza e recuperação do inbox são mantidos. Não há alteração de banco ou dependências. O parser adiciona perfil de wrapper NFComProc conforme boletim SC, ao lado de nfcomProc nacional, sem transformar o XML.

## Fase 10 — Recuperação histórica

IHistoricalRecoveryService faz importação, classificação, associação, pesquisa, agrupamento e relatório; a parte DocumentResolutionService faz cache → oficial → histórico. HistoricalRecoveryBatch referencia ProcessingJob, e HistoricalRecoveryItem guarda linha original (até 200 caracteres), chave classificada, empresa, estado, external job id, tentativa/espera e retorno durável com hash. FKs compostas tenant e restrição de exclusão protegem origem/empresa.

IHistoricalDocumentProvider continua fornecedor-independente: SubmitAsync, GetStatusAsync, DownloadAsync. HistoricalProviderRegistration declara cobrança, idempotência e intervalo. FoundationRuntime aceita configureHistorical no composition root para registrar adapters/políticas via DI antes do build; a distribuição padrão registra zero adapters comerciais. Não há descoberta dinâmica de plugins nem API fictícia.

Chamadas são reservadas antes do envio; limite pago é cumulativo por lote e conta cada método potencialmente cobrado. Submit usa CorrelationId igual ao item.Id como identificador estável. Resposta de envio/download é persistida mesmo após cancelamento, antes de observar o token. Retorno fiscal validado e acervo/índice/item disponível são confirmados juntos; rollback preserva o retorno e pode deixar arquivo físico reaproveitável. Startup sinaliza interrupção e não faz replay/HTTP automático.

## Exportação local (fase 11)

IExportService separa seleção/prévia, geração e relatório. O snapshot de metadados usa filtros compartilhados do acervo sob ArchiveGate; a leitura de cada XML confere hash e identidade novamente. ExportGate serializa entregas. A saída é preparada em staging, relida integralmente e publicada por rename no mesmo diretório pai, com nome único. SQLite e filesystem não formam uma transação única: uma falha de auditoria após publicação preserva o pacote válido e informa EXPORT_PUBLISHED_AUDIT_PENDING. Consulte docs/EXPORT.md.

## Automação (fase 12)

Agendamento diário persistente → claim/auditoria → AutomaticDispatcher → coordenador fiscal existente. O runner exige habilitação de sessão, não restaurada do banco. Cada tick atende até 25 horários vencidos sequencialmente; a próxima janela é persistida antes da rede, evitando replay de dias perdidos ou crash. Cooldown posterior desloca para o primeiro horário diário elegível. Falhas não geram retry imediato; falha de infraestrutura do tick pausa a sessão. Runtime encerra o runner antes do container e do lock de instância.
