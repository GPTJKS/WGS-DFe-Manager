# Fase 6 — NF-e (0.6.0)

Implementação do download NF-e modelo 55. Execução manual por uma, várias ou todas as empresas ativas com NF-e habilitada. A seleção da fonte é explícita; não há migração automática de cursor entre fontes. A automação agendada fica para a Fase 12.

## Operação

1. Em **Certificados A1**, importe um A1 válido. Associe cada empresa ao código **SEF_SC_NFE** ou **NFE_NACIONAL**, conforme o serviço. A associação DEFAULT não substitui esse vínculo específico.
2. Abra **Consultar NF-e**. Escolha fonte, ambiente e empresas. SEF/SC atende empresas cadastradas em SC e somente produção, conforme o contrato fornecido; não foi inventado endpoint de homologação SC.
3. Para SEF/SC, selecione emitente/destinatário/qualquer/ambos e XML ou somente chaves. Nacional usa um único fluxo por empresa e ambiente, sem esses filtros.
4. Deixe a chave vazia para continuar pelo NSU salvo. Para uma consulta pontual, selecione uma empresa e informe a chave modelo 55. O acervo local é conferido primeiro, incluindo hash, XML, ambiente e participação da empresa.
5. Marque **Habilitar consultas oficiais nesta sessão** e clique em **Consultar selecionadas**. Marcar a opção, abrir o programa ou importar um certificado não faz requisição. O padrão distribuído em appsettings é false. Ao fechar a janela, a habilitação da sessão é desligada. Configuração explícita Providers.EnableExternalRequests=true também é aceita para uso controlado.
6. O limite inicial é de 10 lotes por empresa (configurável de 1 a 100). Ao atingi-lo com continuação disponível, execute novamente; o cursor confirmado é mantido. Cancelar mantém lotes já confirmados. A janela aguarda a finalização antes de permitir fechar.
7. Pesquise os XMLs em **Acervo de documentos**. Período, valor e fornecedor filtram o acervo; não há filtro por data inventado na requisição. Não descarte itens fora do período ao avançar NSU.

Horários da grade são explicitamente UTC. O resultado apresenta cStat separado do resultado local. Os status 117/118 e 137/138 são de distribuição e nunca viram protocolo de autorização da nota.

## Fontes e credenciais

| Fonte | Contrato implementado | Credencial e alcance |
|---|---|---|
| SEF/SC | SOAP 1.1, operação **NfeDownloadContab**, parâmetro pXml, namespace distribuicao-v2, layout 2.02 | Finalidade contador; e-CPF/e-CNPJ ICP-Brasil. SEF verifica vínculo contábil. Notas emitidas autorizadas em SC e notas destinadas a SC conforme boletim |
| Nacional | SOAP 1.1, nfeDistDFeInteresse/nfeDadosMsg, distDFeInt 1.01, distNSU ou consChNFe | Finalidade participante; CNPJ-base igual ao da empresa consultada. Produção ou homologação com endpoints oficiais distintos |

Antes de abrir HTTP, o serviço valida cadastro, associação específica, finalidade, validade, chave privada, política ICP-Brasil do certificado, cadeia confiável do sistema e revogação online. Instalar a cadeia ICP-Brasil correta e disponibilizar acesso a LCR é requisito operacional Windows. Não se desabilita a validação TLS do servidor. O servidor ainda decide a autorização fiscal. Não foi executado teste real dessas verificações com certificado do usuário.

O cadastro atual de empresas é CNPJ. e-CPF é aceito como **contador SC**, não como empresa participante CPF. A ingestão de notas nacionais está limitada ao emitente/destinatário do cadastro. Um documento nacional em que a empresa figure exclusivamente como transportador/autXML fica pendente, sem descarte e sem avanço do cursor; esses papéis não são promovidos indevidamente a emitente/destinatário.

A distribuição nacional não recupera automaticamente o XML das próprias emissões nem garante retroatividade para novos usuários ou após interrupção prolongada. Resumo pode exigir manifestação para liberar o XML integral. **Nenhuma manifestação é enviada automaticamente** e esta fase não implementa envio de eventos, consNSU pontual, serviço pago ou recuperação histórica. Ausência na fonte não prova inexistência da nota.

## Continuação e consumo

| Situação | Próxima tentativa local |
|---|---|
| SC 118 com 50 registros | Continua sequencialmente, respeitando limite de lotes |
| SC 117 ou 118 com menos de 50 | 12 horas |
| SC 110 | 1 hora |
| SC 657 | 12 horas conservadoras; pode abranger bloqueio após sincronismo |
| Nacional 138 e ultNSU menor que maxNSU | Continua sequencialmente |
| Nacional 137, 138 sem continuação ou 656 | 1 hora |
| Demais rejeições oficiais | 1 hora conservadora; nenhuma repetição automática |
| Rede incerta, timeout ou interrupção durante requisição | Reserva já persistida: SC 12 horas, Nacional 1 hora |
| Certificado/configuração recusados antes do HTTP | Libera a reserva local; corrija antes de repetir |

A política de consulta por chave é conservadora: também respeita o prazo de sincronização da empresa e a espera após uma consulta pontual. Há ainda teto nacional local de 20 consultas pontuais por hora por CNPJ/ambiente; não há preenchimento automático de lacunas por consNSU nesta fase. Bloqueios 656/657 também geram configuração persistente por certificado/fonte/ambiente, evitando tentar outro cliente do mesmo certificado durante o bloqueio. Filtros não burlam bloqueios oficiais. Nunca se copia para o cursor o ultNSU de uma rejeição 656, pois isso poderia pular dados ainda não armazenados.

Requisições são serializadas em todo o processo, com intervalo global de 250 ms entre encerramento de uma e início da seguinte. Outros programas e computadores que consultem o mesmo CNPJ devem coordenar a sequência nacional; o aplicativo não controla aplicações externas nem limites agregados do IP.

A página atual da SEF informa atraso mínimo estimado de **8 horas**, enquanto o boletim mantém texto antigo de 24 horas. A consulta não presume disponibilidade imediata nem transforma esse atraso em janela histórica. O boletim define SC como mês atual mais duas referências; o Nacional trabalha com até 90 dias após recepção, sujeito às regras de geração de NSU.

## Recebimento, banco e arquivo

A quinta migration cria **NfeDistributionReceipts**: organização/empresa/fonte/ambiente/filtros, chave opcional, NSU solicitado, bytes SOAP, SHA-256 e estado Applied. O conteúdo fiscal da resposta fica retido no SQLite, sem cifragem própria, inclusive após aplicação. Isso preserva o original recebido de SC, docZip nacional, resumos e prova de recebimento. Não contém PFX ou senha. Não existe expurgo automático nesta fase; o banco cresce com o uso. Fazer backup do conjunto completo de dados segue sendo responsabilidade operacional.

Sequência por requisição:

1. Grava uma reserva de espera e auditoria **antes** de chamar HTTP, para manter proteção após queda do processo.
2. Recebe com limite de 32 MiB e grava a resposta no inbox. Se o cancelamento chegar logo após o recebimento, grava esse recibo antes de obedecer ao cancelamento.
3. Valida SOAP, cardinalidade, namespace, ambiente, ecos opcionais SC, NSUs/cursor, quantidade, GZip/base64 e leiautes. GZip tem limite de expansão e conferência de CRC/ISIZE; SC aceita até 50 entradas e 64 MiB expandidos por lote; Nacional até 10 MiB por docZip e 64 MiB somados. XML usa DTD/resolver externo proibidos, profundidade 80, 500 mil nós e 64 atributos; o parser fiscal mantém limites mais estritos de 10 MiB por XML.
4. Valida todos os itens antes de gravar índices. Layout desconhecido, conflito fiscal, ambiente/chave divergente ou XML inválido mantém o recibo pendente e o cursor anterior.
5. Sob o mesmo bloqueio do acervo, publica os XMLs imutáveis e grava documentos/eventos, versões, vínculos, chaves conhecidas, auditoria, estado do recibo e cursor na **mesma transação SQL por página**. Não existe commit parcial de metadados/cursor da página.
6. Se SQL falhar, XML físico publicado pode ficar órfão, preservado para reaproveitamento. A próxima consulta reaplica o recibo pendente **antes de qualquer nova chamada**, mesmo com rede desabilitada. Duplicação por hash é idempotente. Falha física ou conflito não avança cursor.

Existe uma janela inevitável entre o término da resposta HTTP e a gravação do recibo. Se houver queda aí, o cursor permanece anterior e a reserva evita repetição imediata. A retomada seguirá sem pular dados; eventualmente a fonte poderá exigir espera adicional por sequência/consumo. Falha antes do commit nunca autoriza avançar ao cursor remoto conhecido por outra aplicação.

Na abertura, somente jobs Download identificados por auditoria NFE_STARTED que ficaram Running são marcados NFE_INTERRUPTED_INBOX_PRESERVED. Não há replay ou HTTP na inicialização. A próxima consulta explícita faz a retomada. Recibo pendente inválido não é descartado automaticamente; preserve o banco e a resposta para diagnóstico/ajuste do leiaute antes de retomar. A consulta informa o código sem revelar XML/mensagem arbitrária em logs.

**SC:** o XML fiscal é serializado do elemento embutido no lote, preservando namespace e conteúdo textual; não se promete identidade byte a byte com a forma embutida. O SOAP original recebido é preservado integralmente no recibo. **Nacional:** os bytes descompactados do docZip são preservados integralmente no arquivo fiscal. Em ambos, hash atesta os bytes armazenados; esta fase não acrescenta validação XMLDSig/XSD fiscal completo. Cancelamento permanece uma declaração reconciliada conforme as regras do acervo.

Chaves/resumos ficam em KnownDocumentKeys; só há Available quando o arquivo integral local é conferido. Um evento não torna a nota completa disponível. A resposta original de resumos fica no inbox; não há apresentação do resumo como DANFE/XML integral. Chave pontual não altera LastNsu do fluxo.

## Fontes consultadas em 16/10/2026

- [SEF/SC — página do serviço](https://www.sef.sc.gov.br/saiba-mais/web-service-para-download-de-nfe): versão 2.02-1/CNPJ alfanumérico, limites IP e atraso informado. Boletim e três XSD fornecidos preservados em sources/sefsc-nfe.
- [SEF/SC — WSDL efetivamente obtido](https://satnfe.sef.sc.gov.br/ws/distribuicao/nfedownloadV2.asmx?WSDL): cópia em sources/phase6/sc.wsdl. A operação publicada usa NfeDownloadContab, com inicial maiúscula; seguida pelo código.
- [Portal Nacional — NT vigentes](https://www.nfe.fazenda.gov.br/portal/listaConteudo.aspx?AspxAutoDetectCookieSupport=1&tipoConteudo=04BIflQt1aY%3D): índice oficial confirma NT 2014.002 v1.40, de 03/07/2026.
- Documento primário **NT 2014.002 v1.40**, autoria do projeto NF-e/ENCAT, preservado em sources/phase6/NT2014_002_v1_40.pdf, obtido de [espelho do PDF original](https://notagateway.com.br/wp-content/uploads/2026/07/NT2014.002_v1.40-WsNFeDistribuicaoDFe-CNPJ-alfa.pdf). O Portal Nacional apresentou loop de redirecionamento para download direto nesta sessão. O PDF foi lido diretamente, sem substituir a especificação por resumo editorial.
- [Relação oficial de serviços nacionais](https://www.nfe.fazenda.gov.br/PORTAl/webServices.aspx?AspxAutoDetectCookieSupport=1&tipoConteudo=OUC%2FYVNWZfo%3D) e [aviso oficial de consumo](https://hom.nfe.fazenda.gov.br/Portal/informe.aspx?AspxAutoDetectCookieSupport=1&Informe=R%2F52iNPKnGs%3D&ehCTG=false).

O WSDL nacional não foi baixado: os hosts retornaram 403/erro de acesso nesta sessão. Envelope/método seguem os exemplos do documento primário e endpoints seguem a relação oficial. Confirmar o WSDL nacional e fazer homologação com certificado válido no Windows antes de uso operacional. Não há declaração de homologação fiscal real nesta entrega.

## Homologação Windows pendente

Executar DPAPI no mesmo usuário Windows, conferir visualmente a nova tela, instalar cadeia ICP-Brasil, validar A1 de contador SC/participante nacional, testar rejeição de vínculo, LCR, TLS e resposta real com permissão. Confirmar namespaces/layout efetivamente devolvidos, conservação XMLDSig em arquivo SC, cursor e espera após reiniciar, cancelamento e restauração de recibo. Usar cliente isolado para não competir por NSU com outro software do mesmo CNPJ. Sem certificado real fornecido, nenhum endpoint de distribuição foi chamado com credenciais durante esta fase.
