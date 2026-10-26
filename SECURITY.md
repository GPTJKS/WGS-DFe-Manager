# Segurança

## Implementado na Fase 1

- Armazenamento local em pasta da conta do Windows, fora da instalação.
- Não há senha, PFX, token, chave privada ou bytes do certificado nas entidades/configurações.
- Certificados guardam só metadados e SecureStoreReference; ISecretStore tem implementação WindowsDpapiSecretStore desde a Fase 3.
- Logs aceitam apenas nível/evento enumerado e GUID de correlação, sem XML, exception.Message, response bodies, certificados ou parâmetros arbitrários.
- Configuração rejeita propriedades desconhecidas. Consultas NF-e usam habilitação explícita, desabilitada por padrão.
- Guarda de organização em escrita, filtros de leitura, FKs compostas, índices únicos e Revision para conflitos.
- Nenhuma rotina remove XML nem busca endpoints externos.

## Trabalho obrigatório nas próximas fases

Fase 3: implementação DPAPI/A1 concluída; homologação nativa Windows pendente.

Fase 4: leitor XML com DTD/resolução externa proibidos, limites e hash implementado. Fase 5: ZIP com limites, rejeição de caminhos inseguros, hash e escrita atômica implementados. GZip/docZip com limites e CRC/ISIZE implementados na Fase 6. XSD integral e XMLDSig permanecem pendentes. Preservar originais e não aceitar HTML como documento.

Fase 6: HTTPS/TLS, validação normal do servidor e mTLS; nunca aceitar todo certificado de servidor. Segredos fora de source/appsettings/SQLite. Backoff e cooldown oficiais persistidos; nenhuma consulta real durante testes automáticos.

Backup: banco consistente + XML + configurações/metadados; certificados separados com procedimento seguro. DPAPI ligado à conta/máquina não é backup portátil. Uma cópia ZIP não deve conter PFX + senha.

## Limitações explícitas

SQLite e XML não são criptografados por esta entrega. Proteção em repouso depende das permissões do perfil Windows e, quando necessário, BitLocker/política do escritório. OrganizationId prepara isolamento de dados; não é login, autenticação ou autorização multiusuário. A GUI A1 e o cofre estão implementados; o importador de XML ainda não existe.

## Implementado na Fase 2

Leitor XLSX sem resolução XML externa/DTD, com limites de arquivo/ZIP expandido/parte XML/linhas/campos e rejeição de referência de aba externa. Nenhum arquivo ZIP é extraído no disco; só abre entradas do pacote referenciadas pelo workbook. Fórmulas nas colunas importadas são pendências, não executadas. Relatório CSV neutraliza prefixos de fórmula. Exclusão não remove acervo; com vínculos/histórico é bloqueada.

Validar CNPJ pelo DV não confirma inscrição/situação na Receita. Normas do acervo XML/GZip continuam nas fases futuras.

## Fase 3 — Proteção A1

PFX/P12 de até 5 MiB, senha até 4096 caracteres e limites padrão do X509CertificateLoader. Chaves carregadas com EphemeralKeySet, sem persistência automática no Windows Store. Um certificado de usuário com chave privada por arquivo; bundles ambíguos e certificados CA rejeitados. Prova RSA/ECDSA por assinatura/verificação de desafio aleatório.

O envelope em memória contendo PFX e senha UTF-8 é protegido por DPAPI CurrentUser. Entropia de contexto inclui organização e referência UUID; não é chave secreta. Staging recebe somente bytes cifrados, com flush/move sem sobrescrever blob existente. Senha da GUI via PasswordBox.SecurePassword; char[]/PFX/envelope/cópias DPAPI apagados em finally; objetos X509/chaves descartados. Redução de exposição não garante apagar cópias internas do runtime, swap ou dumps.

Nenhum segredo entra no SQLite, logs ou configurações. Não existe fallback em texto puro fora do Windows. Cofre em memória fica exclusivamente no projeto de testes. Arquivo original do usuário permanece intacto; a aplicação não exporta credenciais; somente o fluxo NF-e explicitamente habilitado pode usar o A1 para mTLS.

Alteração SQL e fila de limpeza transacionais. Blob antigo só é removido depois do commit e sem referência ativa; falhas ficam pendentes e têm retry na abertura Windows e na tela. Falha na gravação SQL tenta compensar o novo blob. Crash entre a escrita do novo blob e commit, ou falha na compensação, pode deixar blob cifrado órfão; reconciliação automática desses órfãos ainda não existe. Remover arquivo não garante apagamento físico do disco.

DPAPI depende do perfil Windows: copiar o cofre para outra conta/reinstalar Windows pode impedir decifração. Guarde original e senha separadamente sob política do escritório. Cofre não é backup portátil nem proteção contra processos maliciosos na mesma conta. Permissões herdadas da pasta de dados; usar pasta restrita e política de disco apropriada.

Os dois testes nativos DPAPI precisam rodar no Windows. Não foi validada confiança ICP-Brasil, revogação, EKU/KeyUsage por serviço, vínculo contábil ou autorização SEFAZ. Provedores futuros devem verificar essas condições. A3 tem apenas contrato de chave, sem suporte operacional.

## Fase 4 — Leitura segura de DF-e

XML de até 10 MiB; preflight limita profundidade a 64, nós a 200.000 e atributos por elemento a 64; DTD proibido, XmlResolver nulo, instruções de processamento rejeitadas. SchemaLocation não é seguido e nenhum XSD externo é baixado pelo aplicativo. Leitura de bytes preserva original e calcula SHA-256 antes de qualquer reserialização.

Namespace oficial e raiz/leiaute suportado são obrigatórios; campos críticos são singulares e em caminhos/namespace esperados. Chave e metadados/protocolo precisam coincidir. Dados inválidos produzem enum/código e mensagem estática, sem XML ou exception.Message de biblioteca. O parser não escreve logs, arquivos, banco ou cursores.

Isso é interpretação segura e consistente de metadados, não validação de autenticidade. Um XML forjado mas estruturalmente consistente também pode ser lido. Nenhuma assinatura, certificado, cadeia/revogação, digest da assinatura ou XSD completo é verificado nesta fase. Código/status/protocolo são declarações do arquivo, não consulta fiscal. A chave não permite concluir ambiente ou existência do documento. Eventos permanecem registros separados e resumos não substituem XML integral.

## Fase 5 — Acervo

ZIP é lido por entrada, sem extração de seus caminhos no disco. Preflight rejeita nomes absolutos, ../, unidades/ADS, controles e symlinks/dispositivos; impõe quantidade, tamanho, soma expandida e razão de compressão. ZIP aninhado não é expandido; CRC-32 declarado é conferido contra bytes realmente lidos. Leitura XML limita bytes reais e passa pelo parser seguro da Fase 4.

Pastas não seguem reparse points/symlinks, têm profundidade/listagem limitada e não importam a própria pasta de dados. Destino é derivado somente de organização, ambiente, campos validados da chave e hash. Caminhos de leitura/escrita do acervo ficam sob XML/<OrganizationId>; reparse points existentes no trajeto são recusados. Isso não é defesa contra malware executando na mesma conta ou races deliberados fora do controle da aplicação.

Arquivo existente precisa ter hash correspondente; nenhum arquivo adulterado é substituído. Versões conflitantes permanecem sinalizadas e nunca substituem o cadastro principal. Falha depois da publicação pode deixar órfão; integridade não apaga nem adota arquivo automaticamente. Apagar arquivo não é rotina de atualização.

Leitura do XML em tela é texto formatado, com hash conferido; não lança navegador ou processador externo e limita prévia a 200.000 caracteres. Relatório CSV neutraliza fórmulas e não contém o conteúdo XML. Logs/auditoria continuam com códigos estáticos e IDs, sem dados arbitrários do arquivo.

O acervo é local e sem cifra própria. Status/cancelamento/protocolo são declarações dos arquivos, não autenticação fiscal. Nenhuma assinatura, digest da assinatura, confiança ICP-Brasil, revogação ou XSD integral é verificado. Conferir integridade não torna um XML fiscalmente autêntico. O acervo não faz consultas; a janela NF-e tem habilitação independente e explícita.

## Fase 6 — Consulta NF-e

Endpoints oficiais fixos, HTTPS/TLS 1.2, validação padrão do servidor, revogação habilitada e redirects proibidos. Cadeia ICP-Brasil e finalidade/identidade são verificadas antes do envio. Sem fallback inseguro. Sem PFX/senha no payload ou banco. Original SOAP fiscal, incluindo resumos, é retido como BLOB no SQLite sem cifra adicional; proteger a pasta de dados. Logs técnicos não recebem SOAP, xMotivo, documentos, tokens ou exceções arbitrárias. O hash do recibo é conferido antes do replay.

NSU só avança junto do commit completo da página. Bloqueio de sincronização e bloqueio do acervo têm ordem única. Recebimentos inválidos ficam pendentes; nenhum descarte automático para desbloquear cursor. Reserva antes do envio protege inclusive após crash; erros comprovadamente anteriores ao HTTP liberam a reserva. Os testes não chamam a SEFAZ. Veja docs/NFE.md.

## Fase 7 — Distribuição CT-e

Endpoints nacionais CTe de produção/homologação acrescentados à allowlist explícita, sem redirects ou bypass de TLS/revogação. SOAP 1.2 usa action em Content-Type; NF-e mantém seu contrato anterior. Finalidade participante e raiz CNPJ são verificadas antes do envio. Não há substituição por certificado de contador nem DEFAULT.

CteDistributionReceipts e Items retêm conteúdo fiscal no SQLite sem cifra adicional. GZip/XML desconhecido também passa por limites e preflight antes de um checkpoint; todo conteúdo fora do escopo recebe pendência explícita. SHA-256 é conferido no replay e na prévia. Preview não executa HTML/XML, não resolve URLs e limita texto exibido. Pendências não viram documentos disponíveis. Credenciais nunca entram no XML/banco/log. Ver docs/CTE.md.

## NFC-e SC — Fase 8

Endpoint HTTPS NFC-e oficial acrescentado à allowlist, sem redirect e com TLS 1.2/mTLS/revogação. A fonte exige associação SEF_SC_NFCE, finalidade contador, validade/chave/política ICP-Brasil e cadeia; a SEF/SC verifica o vínculo contábil. CPF do responsável pelo e-CNPJ não é uma autorização local. Nenhum PFX/senha entra no SOAP ou no banco. Empresas continuam CNPJ; o cadastro de contribuinte CPF não foi ampliado nesta fase.

A proteção XML/GZip, hash do recibo, limites e transação integral da NF-e são reutilizados. NFC-e verifica modelo 65 e eventos 110111/110112; payload divergente fica pendente e bloqueia avanço. Disponibilidade e status declarado não comprovam assinatura/autorização fiscal. Testes não usaram certificado real nem chamadas fiscais autenticadas.

## NFCom SC — Fase 9

Somente endpoint oficial NFCom SC acrescentado à allowlist mTLS/TLS 1.2. Sem redirects, consulta nacional inventada, scraping ou serviço pago. A fonte NFCOM exige A1 de contador associado, empresa SC ativa/habilitada, validade, chave e cadeia ICP-Brasil; o servidor valida vínculo contábil. A associação de NF-e/NFC-e/DEFAULT não é fallback.

Payloads precisam de namespace NFCom, modelo 62 e ambiente compatível. Eventos aceitos: 110111/240150/240140/240151/240161/240162; outros ficam pendentes, sem avançar cursor. Protocolo da NFCom tem 16 dígitos. Formatos NFComProc/nfcomProc são reconhecidos explicitamente, sem renomeação, recomputação de assinatura ou afirmação de XSD válido. Proteção XML/GZip/hash/commit da fase anterior é reutilizada. Sem dados/certificados reais nos testes.

## Fase 10 — Recuperação histórica

Importação de chaves não faz HTTP. Rede desligada por padrão; providers comerciais não estão registrados. Chamadas pagas exigem opção explícita e teto cumulativo por lote, com reserva durável antes de cada método. O total não prova custo real; submit/status/download podem ser cobrados conforme contrato. Estado Submitting incerto não é reenviado sem declaração de idempotência do adapter.

XLSX é lido sem macros, fórmulas ou vínculos externos executados; chave numérica é rejeitada para não aceitar arredondamento do Excel. XMLs externos passam pelo parser seguro, correspondência de chave/modelo/ambiente/empresa, limites e SHA-256 antes do commit. Fonte histórica pode fornecer XML coerente sem comprovar autenticidade: XMLDSig/XSD fiscal completo continuam fora do escopo.

Credenciais não aparecem em opções de lote, tabelas, relatórios ou logs. Adapter futuro deve usar ISecretStore/DPAPI CurrentUser em configuração segura, mantendo somente referência no SQLite; jamais token em appsettings.json ou no código. A integração deve implementar allowlist HTTPS, timeout, revogação/rate limit e redigir exceções. Nenhum segredo foi fornecido, solicitado ou armazenado nesta fase.

Relatórios CSV neutralizam prefixos de fórmula, preservam motivo por linha e são publicados por arquivo temporário. Input inválido é limitado a 200 caracteres. Blobs de retorno fiscal são retidos sem expurgo/encriptação adicional do banco; proteger pasta e backup. A reserva/cadastro é tenant; não impede acesso direto de quem controla os arquivos locais.

## Exportação XML

A saída contém apenas XMLs selecionados e relatórios opcionais. Não inclui PFX/P12, senhas ou SQLite. Destino deve ser uma pasta absoluta existente, fora do diretório de dados; caminhos relativos perigosos, links simbólicos/reparse points e colisões são recusados. Nome único e FileMode.CreateNew/Move sem sobrescrita protegem entregas anteriores. Conferência de identidade, hash, inventário e tamanho precede publicação. Esses controles não autenticam o XML fiscal nem substituem a homologação de junctions no Windows. O destino escolhido pode conter dados fiscais sensíveis.

## Automação

Nenhum timer ou acesso fiscal inicia no bootstrap. Agendamentos salvos ativos exigem consentimento explícito da sessão; rede desligada revoga também esse consentimento. A1 continua no cofre existente, sem senha em agendamentos/relatórios. Dispatcher não chama histórico comercial, não manifesta documentos e não altera regras de credencial. Pausa/desligamento cancela e aguarda gravações seguras. Um claim durável precede a rede; uma chamada interrompida não é repetida automaticamente na reabertura. Histórico expõe códigos sanitizados, não texto arbitrário de exceptions.
