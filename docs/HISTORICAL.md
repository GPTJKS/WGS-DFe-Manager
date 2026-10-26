# Recuperação histórica — Fase 10 / 0.10.0

## O que está pronto

Importação de chaves XLSX/CSV/TXT, validação/classificação automática 55/57/62/65/67, deduplicação, associação a empresas, agrupamento por empresa/emitente/modelo/ano/mês, conferência do acervo, consulta oficial por chave quando aplicável, pipeline externo com envio/status/download, cancelamento/retomada e relatório CSV completo. Não é necessário classificar o modelo antes da importação.

**Nenhum fornecedor comercial de recuperação está conectado nesta entrega.** Espião, SIEG e outros exigem contrato, documentação da API e credenciais antes de implementar um adapter. O pipeline é funcional e ensaiado com adapter sintético em testes, que não é registrado no aplicativo. Não se promete XML antigo disponível nem se simula retorno de uma API real.

## Uso

1. Abra Recuperação histórica. Escolha Produção ou Homologação explicitamente; o ambiente não pode ser obtido da chave.
2. Escolha empresa ou deixe automática. Automática associa pelo CNPJ/CPF emitente da chave se cadastrado; não cria empresa. Para notas de entrada, escolha a empresa destinatária. Linhas sem associação ainda podem ser resolvidas no acervo da organização; consultas externas exigem empresa ativa e modelo habilitado. O botão de associação atende somente linhas pendentes sem empresa e sem pedido externo.
3. Importe arquivo. Reconhece CHAVE, CHAVE DE ACESSO, CHAVE NFE ou CHAVE CTE. Uma coluna única sem cabeçalho também é aceita. XLSX usa a primeira aba visível; CSV aceita vírgula/ponto e vírgula; TXT usa uma chave por linha. Espaços ASCII/minúsculas são normalizados pelo parser de chave; caracteres arbitrários não são retirados.
4. Confira contagens, inválidas, duplicadas e agrupamentos. A importação é local e não chama fontes. Cada linha fica preservada; inválidas/duplicadas não são enviadas. Uma chave repetida no mesmo lote só participa uma vez da recuperação. Lotes distintos voltam a conferir o acervo e não presumem arquivo presente apenas pelo banco.
5. Selecione Conferir / Retomar lote. Com fontes desligadas, funciona como conferência local. Tentar fontes oficiais exige habilitar consultas na sessão e certificados associados conforme os módulos existentes. Histórico só é habilitável quando há adapter registrado; cobrança exige autorização e limite total por lote.
6. Confira motivo e prazo por linha. Retome após o prazo indicado, sem polling automático ou sleep na UI. Use Cancelar operação e aguarde o ponto seguro antes de fechar.
7. Salve o relatório CSV: inclui arquivo, linha, ambiente, entrada/chave, modelo, emitente, ano/mês, empresa, resultado/código/disponibilidade, provedor, tentativas e próxima tentativa UTC. Abrir no Excel não executa fórmulas da entrada. O relatório inclui todas as páginas.

Os últimos 50 lotes aparecem na UI; seus dados anteriores não são excluídos. Linhas são paginadas de 50 em 50. O modelo CSV de chaves tem exemplos sintéticos, sem XML autorizado: substitua pelos seus dados e mantenha as células como texto.

## Regras de entrada

Até 100.000 linhas de dados, arquivo de até 20 MiB, até 32 colunas. XLSX tem até 2.000 partes, 64 MiB descompactados, parte até 20 MiB e até 200.000 shared strings. Fórmula, erro ou tipo numérico na coluna da chave é inválido, inclusive quando a célula numérica parece mostrar 44 dígitos. Não se reconstrói chave arredondada/notação científica. CSV/TXT são texto, UTF-8 (com ou sem BOM), com fallback Windows-1252.

Cada entrada preservada tem até 200 caracteres; entradas maiores são sinalizadas inválidas e truncadas para o relatório. A chave canônica valida UF, mês/ano, modelo, emitente e DVs. Importação em transação, escrita em grupos de 500 e cancelamento com rollback; não deixa lote parcial. A importação de empresas continua com limite de 10.000, separado do limite de chaves.

## Resolução

1. Verifica índice, XML físico e hash no acervo pelo par chave+ambiente. Disponível significa presença íntegra, sem prova de assinatura/autenticidade. Com empresa associada, o documento precisa pertencer à empresa por emitente/destinatário ou vínculo de participante. Arquivo corrompido bloqueia chamadas e sinaliza conferência; arquivo ausente deixa de contar como disponível.
2. Retorno histórico já recebido é reaplicado localmente antes da rede. Arquivo ausente pode ser restaurado desse retorno; arquivo adulterado não é sobrescrito.
3. Se habilitado, tenta fonte oficial: NF-e SC para SC/produção, Nacional para demais combinações; NFC-e/NFCom apenas SC/produção. Não há CT-e/OS oficial por chave no contrato implementado. Para usar outra rota NF-e disponível, consulte-a na janela NF-e; o histórico reconhecerá o acervo. Ano/mês ajudam na classificação, mas não comprovam data de autorização/recepção nem disponibilidade atual; não se inventa acesso histórico por NSU.
4. Fonte oficial mantém inbox, associação de certificado, limite e cooldown próprios. Erro de credencial, espera, timeout ou retorno inválido não vira chamada paga. Um resultado sem XML/fora da janela pode seguir ao adapter histórico se habilitado.
5. Se habilitado e registrado para o modelo, envia uma chave por pedido histórico, preserva o job externo e acompanha em retomadas. PollSeconds define espera por item. Depois baixa e grava a resposta antes de validar/incorporar. Sem fonte, mantém motivo explícito por linha; NFC-e não localizada não aborta o resto do lote.

Não há varredura automática de todas as pastas procurando XML órfão por chave. Importe acervo antigo em Acervo de documentos antes de recuperar, para indexar arquivos ainda desconhecidos. O check físico usa o caminho/hash indexados; recibos duráveis permitem reaproveitar publicações físicas em rollback.

## Reserva de chamadas e envio incerto

Budget é número máximo de chamadas potencialmente pagas acumulado no lote, não reais em moeda nem orçamento do fornecedor. Reserva ocorre antes de Submit, GetStatus e Download, mesmo em timeout/queda. Consultas gratuitas não consomem esse budget, mas contam tentativas. Aumentar limite permite novas chamadas, sem zerar o já reservado. Autorizar teto não é prova de cobrança exata ou de preço do contrato.

CorrelationId do HistoricalRequest é o ID estável da linha. Adapter só pode declarar IdempotentSubmission=true se o contrato real garantir reenvio seguro desse identificador. Se Submit ficou incerto e não há essa garantia, a linha vira SUBMISSION_REVIEW_REQUIRED e não é reenviada automaticamente. Conferir no fornecedor é necessário antes de uma intervenção administrativa. Não há botão para apagar a incerteza ou trocar provider de um job já enviado. Um novo lote constitui nova solicitação e exige a mesma cautela.

Job externo mantém provider original e ID; trocar provider com pedido em andamento é recusado. NOT_FOUND/NeedsReview não se reenviam automaticamente, mas uma futura presença íntegra local é reconhecida. Em atraso, retome após o horário UTC. Agendamento automático será tratado na fase 12.

## Persistência e falhas

Sete migrations; HistoricalRecoveryBatches/Items são tenant e têm FKs compostas para job/batch/empresa. Empresas com histórico não podem ser excluídas nem trocar CNPJ. BLOB é JSON de payloads fiscais recebidos, com SHA-256; nunca credenciais. Limite de até 50 XMLs por download, 10 MiB por XML, soma até 32 MiB; JSON até 48 MiB na reaplicação.

O retorno é persistido antes de observar cancelamento após Download; ID externo idem após Submit. Chave/modelo/ambiente de todos os XMLs devem corresponder à linha, e o retorno precisa conter documento integral, não só evento/resumo. Documento integral precisa ter participação compatível com a empresa; CT-e inclui os grupos adicionais conhecidos. Nenhum dado do XML é fabricado. Vínculos adicionais CT-e são preservados como participação, sem promover tomador a destinatário.

Validação inconsistente mantém o retorno para conferência. Commit inclui todos os XMLs/indexação/links e estado disponível em uma transação. Falha SQL faz rollback; retorno continua disponível para reaplicação sem baixar de novo. Conflito não substitui o XML principal. Publicação física anterior ao SQL pode deixar órfãos reaproveitados. Blobs não têm expurgo nem criptografia adicional ao banco; proteger pasta/backup.

Startup sinaliza jobs históricos interrompidos e conserva pedidos, reservas e retornos. Não há consulta nem replay automático na abertura. Cancelar preserva os itens já confirmados. Cada retomada usa scopes/IDs e carrega um item/retorno por vez, sem carregar todos os blobs nas listagens.

## Limites reais

Sem adapter/API comercial contratada não há recuperação histórica remota fora das fontes já disponíveis. Capabilities não comprovam autorização. Credenciais reais, XMLDSig, XSD fiscal completo, avaliação visual WPF e DPAPI/cadeia em Windows continuam pendentes de homologação. O pipeline não estima valores de fornecedores, não faz scraping, emissão ou manifestação e não contorna limites SEFAZ.
