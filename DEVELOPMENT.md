# Desenvolvimento

## Pré-requisitos

SDK .NET 10.0.401; Windows para executar WPF. Em Linux é possível compilar WPF com EnableWindowsTargeting=true, mas não executá-lo. Domínio, banco, CLI e testes rodam em Linux.

```powershell
dotnet restore WgsDfeManager.sln --locked-mode
dotnet build WgsDfeManager.sln -c Release --no-restore
dotnet test tests/WgsDfe.Tests/WgsDfe.Tests.csproj -c Release --no-build
dotnet run --project src/WgsDfe.Desktop
```

Se alterar dependências, renovar intencionalmente os locks com `dotnet restore --force-evaluate` e revisar a diferença. Não instalar SDK/IDE no computador do usuário final; publicação self-contained será preparada após a V1 estável.

## Ferramenta de verificação

```powershell
dotnet run --project src/WgsDfe.Tools -- init appsettings.json
```

A CLI e a GUI não podem abrir a mesma pasta ao mesmo tempo. Para testes/manutenção isolados, copiar a configuração e definir DataDirectory absoluto em diretório novo. Não apontar testes para acervo real.

## Migrations

```powershell
dotnet tool restore
dotnet ef migrations add NomeDaAlteracao --project src/WgsDfe.Database --startup-project src/WgsDfe.Database --output-dir Migrations
dotnet ef migrations script --project src/WgsDfe.Database --startup-project src/WgsDfe.Database
```

O design-time factory usa caminho temporário. O programa aplica migrations no bootstrap à pasta configurada. Não executar `database update` no banco do usuário sem resolver configuração, backup e plano de migração. Nunca remover/recriar banco como rotina de atualização.

## Sequência do projeto

1. Fundação — concluída na 0.1.0.
2. Empresas — concluída na 0.2.0: cadastro, pesquisa, CNPJ, XLSX/CSV e relatório.
3. Certificados — implementada na 0.3.0; homologação nativa Windows pendente.
4. Parser DF-e — implementada na 0.4.0: chave/DV, cinco modelos, XML seguro e inspeção local.
5. Acervo — implementada na 0.5.0: XML/ZIP/pastas, versões, eventos, vínculos, pesquisa e integridade.
6. NF-e — implementada na 0.6.0: SC/nacional, inbox durável, cursores/espera e testes sintéticos; homologação Windows e WSDL nacional pendentes.
7. CT-e/CT-e OS — implementada na 0.7.0: fluxo compartilhado, distNSU/consNSU, retenção/reprocessamento e participantes; homologação nativa pendente.
8. NFC-e — implementada na 0.8.0: WSDL real SC, NSU/chave, filtros, eventos, inbox/retomada; regime de testes e homologação nativa pendente.
9. NFCom — implementada na 0.9.0: SC, NSU/chave, seis eventos, cursor/recibo e retomada; homologação A1/Windows pendente.
10. Recuperação histórica — implementada na 0.10.0: importação/classificação, acervo primeiro, resolução oficial, pipeline externo retomável, reservas de chamadas e relatório; adaptador comercial depende de contrato/credenciais.
11. Exportação — implementada: ZIP/pastas, filtros, eventos, versões, integridade e auditoria.
12. Automação — implementada: horários diários, autorização da sessão, coordenadores existentes, cooldowns e histórico persistente.

Só prosseguir após compilar, testar e documentar a fase. Credenciais/dados reais não devem entrar no repositório ou nos testes. Arquivos sintéticos nos testes de banco verificam constraints e não são exemplos fiscais autorizados.

## Instalador

Não há Setup.exe nem versão final publicável nesta fase. Uma publicação self-contained Windows futura poderá usar `dotnet publish src/WgsDfe.Desktop -c Release -r win-x64 --self-contained true`, seguida de empacotamento/assinatura/validação no Windows. Não oferecer o código-fonte como se fosse instalador.

## Homologação Windows

Execute os testes no Windows: os dois WindowsFact devem passar, sem serem ignorados. Depois siga `docs/CERTIFICATES.md` para avaliação visual e teste com outra conta. Use apenas certificados sintéticos; nenhum dado/segredo real entra no código ou suite.

## CLI de inspeção da Fase 4

```powershell
dotnet run --project src/WgsDfe.Tools -- key "CHAVE_DE_44_POSICOES"
dotnet run --project src/WgsDfe.Tools -- parse "C:\Documentos\nota.xml"
```

Saída JSON com metadados; não inicia FoundationRuntime, não acessa banco/cofre ou rede. Código 0 indica leitura consistente; código 1 é erro seguro de validação/leitura; código 2 é uso incorreto. O JSON pode conter dados fiscais: redirecionar somente para destino autorizado, nunca a logs técnicos. Chave agrupada deve ser um único argumento entre aspas.

A UI WPF foi compilada por cross-targeting; a nova tela ainda precisa de avaliação visual no Windows. A suite não chama serviços fiscais reais. Fixtures de metadados são sintéticas e deliberadamente não são prova de XSD/assinatura/autorização.

## Validação do acervo

Suite ArchiveTests usa diretórios temporários, arquivos reais e SQLite isolado. Exercita cinco modelos, ZIP/CRC, limites, links Unix, eventos antes/depois da nota, variantes/conflitos, filtros/centavos, cancelamento, concorrência, rollback e órfãos. Não aponta para acervo/certificado real. Homologar WPF e reparse points no Windows conforme docs/ARCHIVE.md. A execução dos dois WindowsFact DPAPI continua necessária no perfil Windows de homologação.

## Fase 6

Providers NF-e registrados via DI scoped; não chamar GetDocumentsAsync diretamente em fluxos de produção, pois o coordenador INfeSyncService é o responsável pelo inbox/cursor/cooldown. A rede fica desabilitada no pacote. FakeTransport usa envelopes sintéticos e FakeClock valida esperas sem sleep. Documentação operacional em docs/NFE.md.

Teste de integração real requer Windows, A1 e cliente fiscal isolado, e não foi executado aqui. WPF compilado por cross-targeting não substitui QA visual. Ao alterar leiautes, preservar recibos pendentes e testar reaplicação sem nova chamada. O SDK/locks permanecem iguais; não há pacote adicional nesta fase.

## Fase 7

ICteSyncService coordena o fluxo CTE. O codec SOAP 1.2 é separado do NF-e; transporte mTLS, credencial, gates e DTOs de wire da fase anterior são compartilhados sem criar adaptadores fictícios. IDocumentProvider é baixo nível: a UI usa o coordenador para respeitar inbox/cursor/espera. Fonte CTE exige participante, não contador.

CT-e/OS têm um único NSU por empresa/ambiente. A seleção de modelo decide arquivamento; todos os retornos são recebidos duravelmente. CteDistributionItems guarda pendências e permite reprocessar offline sem reiniciar o cursor. Conferir docs/CTE.md e ensaios CteTests antes de alterar esse comportamento.

## Fase 8 — NFC-e

A infraestrutura NF-e foi parametrizada por fonte/modelo, preservando os contratos e cursores anteriores. NfeSources.Model/Documents/IsSantaCatarina centralizam a distinção 55/65 e flags. CompaniesAsync aceita source opcional; StatesAsync sem source continua exibindo apenas NF-e, enquanto source=SEF_SC_NFCE retorna os estados NFC-e. As APIs antigas continuam com os defaults anteriores. NfeWindow(runtime,true) usa a tela comum com avisos/restrições próprios. Não há dependência nem migration nova: o inbox existente possui ProviderCode, e os estados já possuem Model.

NfceTests usa SOAP/XML e certificados sintéticos, transporte falso, relógio controlado e SQLite real. Há compilação do XSD embutido e conferência estrutural do SOAP contra o WSDL capturado; isso não valida os campos fiscais internos, pois o WSDL usa xs:any. Não apresentar esse ensaio como validação do XSD distNFCeSC nem homologação do serviço.

## Fase 9 — NFCom

NfeSources passa a mapear NFCOM ao modelo 62 e flag NFCom. Registro DI inclui essa fonte como quinto provider fiscal. NfeWireCodec usa perfis SC para namespace, operação, envelope, versões e payloads, mantendo NFe/NFCe. O coordenador recebe fonte explícita e conserva o inbox/cursor por fonte, sem migration/dependência nova. Os nomes Nfe* e códigos NFE_* são contratos técnicos compartilhados.

NfeWindow(runtime,DocumentModel.NFCom) usa a UI comum com janela de quatro referências e fonte fixa. Os testes novos NfcomTests exercitam o codec, SQLite e acervo reais com transporte/relógio simulados. O WSDL público está capturado; xs:any impede alegar validação dos dados internos por esse schema. Parser aceita NFComProc (BT SC) e nfcomProc (XSD nacional) como formatos explícitos, preservando o conteúdo e hash, sem renomear tags.

## Fase 10

Application/Historical contém contratos, opções e políticas de registro; HistoricalRecoveryService é um serviço scoped parcial. DocumentResolutionService.cs contém o fluxo de resolução. HistoricalGate serializa históricos, ArchiveGate protege o commit de XML/índice; o serviço oficial conserva seus próprios gates/inboxes/cooldown. Não manter transação SQLite aberta durante HTTP.

CompanyFileReader expõe leitores internos CSV/XLSX com limite parametrizado; a importação de empresas continua limitada a 10.000. Chaves admitem 100.000 e recusam células numéricas/fórmulas, sem tentar reconstruir dígitos perdidos pelo Excel. Uma transação grava o lote inteiro em grupos de 500, liberando ChangeTracker. Cancelamento/erro de importação não deixa lote parcial.

Migration HistoricalRecovery adiciona duas tabelas tenant, mantendo as seis anteriores. APIs de páginas/listagens projetam DTOs sem carregar blobs; processamento usa IDs e um retorno por vez. Últimos 50 lotes na UI, páginas de 50 linhas e CSV completo por streaming. Consulte HISTORICAL.md para limites/recovery e HISTORICAL_PROVIDER_INTEGRATION.md para integrar um adapter real.

## Fase 11

Application/Export define seleção, prévia, progresso e resultado. ExportService é scoped, com ExportGate singleton para serializar operações; ArchiveGate protege somente a captura dos metadados. ArchiveService.FilteredDocuments centraliza os mesmos filtros usados na busca, sem limitar a exportação à página visível. A seleção é recalculada no início da operação.

Leitura usa IArchiveFileStore e IDfeParser, preservando bytes e conferindo identidade/hash. A saída temporária fica no mesmo diretório pai; verificação por streaming confere inventário, tamanhos e hashes de XML/relatórios antes de Move sem sobrescrita. ProcessingJobs/ProcessingJobItems e auditoria existentes registram o fluxo; nenhuma migration ou dependência nova. ExportTests exercita SQLite e arquivos reais, incluindo adulteração, cancelamento, concorrência e falha SQL posterior à publicação. QA visual WPF e junctions Windows continuam pendentes.

## Fase 12

Application/Automation define contratos e DTOs. AutomationService scoped usa AutomationGate singleton para serializar CRUD/claims/ticks; não mantém transação SQL durante rede. AutomaticDispatcher abre um scope separado por consulta e chama INfeSyncService/ICteSyncService, preservando gates, inbox e controles oficiais. Não chamar os providers diretamente do scheduler.

AutomationRunner singleton inicia somente em ArmAsync explícito e executa em Task.Run com PeriodicTimer de 30 segundos. PauseAsync revoga consentimento, desabilita rede, cancela e aguarda o ponto seguro. Runtime pausa o runner antes de liberar a instância e o DI. ProviderOptions usa leitura/escrita atômica e notifica revogação da rede; fechar telas manuais que desligam rede também revoga automação. Não há serviço Windows nem execução após fechar o aplicativo.

ScheduleClock calcula o próximo horário local estritamente futuro, corrige minutos inexistentes em transição DST e escolhe a ocorrência UTC posterior em horários ambíguos. SQLite materializa DateTime UTC com Kind Unspecified; cooldown é marcado como UTC antes do cálculo da próxima janela. A API aceita São Paulo/UTC; testes da função cobrem também fuso com DST.

Migration AutomaticSynchronization adiciona SyncSchedules e SyncScheduleRuns, ambas com filtro/guard/FKs tenant. Próxima janela e tentativa Running são salvas juntas antes de qualquer chamada. No bootstrap, tentativas abertas são encerradas como interrompidas, sem executar rede. Sem alterações nos sete schemas anteriores ou nos locks. AutomationTests e AutomationIntegrationTests usam SQLite/arquivos reais; integrações usam coordenadores/codec/acervo reais com transporte e credenciais sintéticos.
