# Fase 7 — CT-e / CT-e OS — versão 0.7.0

Integração ao **Ambiente Nacional do CT-e**, conforme NT 2015.002 v1.05. A fonte local usa o código **CTE** e atende modelos 57 e 67. Não há emissão, manifestação, envio de eventos ou acesso com certificado de contador em substituição ao participante.

## Usar no Windows

1. Cadastre a empresa e habilite CT-e e/ou CT-e OS. Em **Certificados A1**, associe um certificado de finalidade participante à fonte **CT-e / CT-e OS — Nacional**. A raiz CNPJ do certificado deve coincidir com a empresa consultada. A associação DEFAULT não é usada como substituta.
2. Abra **Consultar CT-e / OS**. Selecione uma, várias ou todas as empresas ativas, ambiente e modelos. Os dois modelos podem ser selecionados independentemente; considera-se também o que está habilitado no cadastro de cada empresa.
3. Deixe **NSU pontual** vazio para continuar a distribuição a partir da posição confirmada. Informe um NSU maior que zero, até 15 dígitos, para consulta pontual de uma única empresa. A posição da distribuição não é alterada por consNSU.
4. Marque **Habilitar consultas oficiais nesta sessão** e clique em **Consultar selecionadas**. O padrão do pacote é desabilitado. Abrir a tela, importar um certificado ou marcar a caixa não inicia consulta. Fechar a tela desliga a habilitação da sessão.
5. O limite é de 10 lotes por empresa, configurável entre 1 e 100. Se houver continuação ao atingir o limite, execute novamente; não reinicie o NSU. Cancelar mantém os recebimentos já confirmados e aguarda um ponto seguro antes de fechar.
6. Confira **Resultado**, **Ver cursores salvos** e **Preservados pendentes**. A última lista é paginada em 50 itens. Selecione um item e use **Ver XML preservado** para uma prévia somente leitura, com SHA-256 conferido e limite de 200 mil caracteres.
7. **Reprocessar preservados** reavalia respostas pendentes e itens já recebidos, com os modelos/cadastros atuais, sem nova chamada à rede. Para incorporar CT-e OS anteriormente fora da seleção, selecione CT-e OS e reprocese. Os documentos arquivados aparecem em **Acervo de documentos**.

Os horários de espera na grade são UTC. O período de emissão e os valores continuam sendo filtros do acervo, não parâmetros inventados de distribuição. O campo NSU pontual não recebe chave de acesso: o contrato consultado define distNSU e consNSU, sem consulta por chave.

## Contrato e credencial

| Item | Implementação |
|---|---|
| Serviço/método | CTeDistribuicaoDFe / cteDistDFeInteresse |
| Namespace SOAP | http://www.portalfiscal.inf.br/cte/wsdl/CTeDistribuicaoDFe |
| Mensagem de dados | cteDadosMsg / distDFeInt, namespace CT-e, versão 1.00 |
| Transporte | SOAP 1.2, action no Content-Type application/soap+xml, HTTPS/mTLS, TLS 1.2 |
| Produção | https://www1.cte.fazenda.gov.br/CTeDistribuicaoDFe/CTeDistribuicaoDFe.asmx |
| Homologação | https://hom1.cte.fazenda.gov.br/CTeDistribuicaoDFe/CTeDistribuicaoDFe.asmx |
| Distribuição | distNSU/ultNSU, até 50 docZip por retorno |
| Consulta pontual | consNSU/NSU; resposta deve corresponder ao NSU solicitado |
| Certificado | Participante, mesma raiz CNPJ; validação local de validade, chave, política ICP-Brasil, cadeia/revogação; servidor decide autorização |
| Disponibilidade da fonte | Até três meses da recepção no Ambiente Nacional, conforme NT; não garante XML das próprias emissões |

O cadastro atual é por CNPJ; participante pessoa física/CPF não foi adicionado ao cadastro nesta fase. e-CPF de contador não autoriza consultar todos os clientes no serviço nacional. Não se solicita certificado ao usuário para testes automáticos.

O domínio e a requisição preservam CNPJ alfanumérico como texto, conforme a orientação conjunta 2025.001. O ZIP de distribuição ainda publicado no link consultado tem tipo TCnpj com expressão numérica antiga. O teste XSD do pedido cobre o CNPJ **numérico** com o pacote efetivamente obtido; não se afirma que esse XSD antigo valide CNPJ alfa. Compatibilidade alfa no serviço deve ser confirmada na homologação, sem adulterar os XSD oficiais preservados.

Os endpoints foram conferidos nas relações oficiais de produção e homologação. O WSDL não pôde ser obtido nesta sessão (erro de acesso/HTTP); o adaptador deve ter operação, wrapper e binding confrontados com o WSDL acessível no ambiente Windows e certificado válido antes do uso operacional. Não há declaração de homologação fiscal real nesta entrega.

## Um cursor compartilhado, sem perda por seleção

A distribuição nacional não contém filtro de modelo. O mesmo fluxo pode trazer **CT-e, CT-e OS, CT-e Simplificado, GTV-e e eventos**. Criar um NSU para cada modelo causaria consultas concorrentes à mesma sequência.

O aplicativo mantém **uma posição por organização, empresa e ambiente**, fonte CTE, papel Either e conteúdo FullXml. Model=57 é apenas o representante técnico desse fluxo compartilhado no esquema existente; não existe cursor separado de modelo 67. O cStat não é protocolo de autorização do documento.

Cada página recebida é decomposta em itens duráveis. Os modelos selecionados e habilitados, quando reconhecidos, consistentes e conciliados com a empresa, são incorporados ao acervo. Os demais permanecem em **CteDistributionItems**, com bytes descompactados, hash, NSU, schema e motivo. O recibo SOAP original também permanece armazenado.

Há dois estados diferentes: **recebido e preservado** e **arquivado no acervo**. O cursor de recebimento só avança quando todos os docZip da página foram registrados duravelmente, incluindo os preservados pendentes. Uma pendência nunca é apresentada como XML integral disponível no acervo. A grade informa a quantidade pendente e o job termina com advertências enquanto ela existir.

| Retorno | Destino |
|---|---|
| CT-e/CT-e OS reconhecido, selecionado e habilitado | Acervo, versões, vínculos e chave conhecida |
| Modelo 57/67 fora da seleção ou desabilitado | Preservado; pode ser arquivado depois por reprocessamento local |
| CT-e Simplificado, GTV-e ou leiaute ainda não reconhecido | Preservado com motivo; sem fingir suporte ao documento integral |
| Divergência de ambiente, schema, conteúdo ou participação | Preservado para conferência; sem promoção a Available |
| Versão conflitante com o acervo | Arquivo divergente preservado como conflito; principal mantido; item continua pendente |
| SOAP inválido, quantidade/NSU/cursor inconsistentes, GZip inválido ou XML inseguro | Recibo não aplicado; não avança cursor; tentativa seguinte reaplica antes de consultar |

Reprocessar não muda o cursor já confirmado. Layout desconhecido continua preservado até uma atualização que o suporte; não há expurgo ou descarte automático para silenciar pendências. O reprocessamento não amplia os modelos desta entrega para GTV-e ou CT-e Simplificado. Preservar os bytes permite uma futura implementação sem depender de nova consulta a um documento já fora da janela da fonte.

## Participantes do transporte

Para notas completas suportadas, o serviço lê emitente, destinatário, remetente, expedidor, recebedor, tomador e autorizados autXML em caminhos diretos do namespace CT-e. Resolve toma3 pelos indicadores 0–3 (remetente, expedidor, recebedor, destinatário), toma4=4 pelos dados do terceiro e o grupo toma do CT-e OS. Grupos ambíguos/identificação inválida não criam vínculos confiáveis.

Empresas cadastradas que participem do documento recebem vínculo com descrição dos papéis em **FiscalDocumentCompany.Participation**. Papel distinto de emitente/destinatário usa OtherParticipant; não se renomeia um tomador como destinatário. A pesquisa do acervo aceita **Outros participantes**, além dos filtros anteriores. A descrição é metadado persistido; o XML preservado permite conferir cada grupo.

Eventos podem chegar antes da nota e são vinculados quando ela chega, mantendo a reconciliação restrita de cancelamento do acervo. Evento isolado não torna o XML completo da nota disponível.

A NT prevê que chaves **referenciadas** podem ser mascaradas com noves quando o acesso ocorre por autXML. Preservam-se os bytes efetivamente distribuídos; não se tenta reconstruir essas referências nem se promete identidade com o XML original do emitente. Nenhuma assinatura XMLDSig ou XSD fiscal integral é validado nesta fase. Situação fiscal é a declarada pelo XML/evento, não confirmação atual do protocolo na SEFAZ.

## Espera, falhas e retomada

- 138 com continuação (ultNSU menor que maxNSU): próxima página sequencial, até o limite de lotes.
- 137 ou fim da distribuição: espera mínima de uma hora persistida.
- 656, demais rejeições e falha de comunicação incerta: espera local conservadora de uma hora, sem repetição automática.
- Antes de enviar, grava reserva de uma hora. Queda do processo ou cancelamento durante HTTP não permite repetir imediatamente.
- Erro comprovadamente anterior ao HTTP, como configuração/certificado recusado no preflight, libera a reserva para correção.
- 656 também persiste bloqueio por certificado/ambiente; não se tenta outro cliente com o mesmo certificado durante o prazo.
- A consulta pontual obedece ao prazo da empresa e a um teto local conservador de 20 consultas/hora por empresa/ambiente. Esse teto é política do aplicativo, não uma quota de CT-e afirmada pela NT consultada.
- Um NSU pontual já arquivado usa os bytes locais conferidos antes de consultar. Pode restaurar arquivo físico ausente, sem mexer no cursor da distribuição; adulteração física não é sobrescrita.

NF-e e CT-e compartilham o bloqueio de execução e o limitador HTTP do processo, com intervalo global de 250 ms. Outros softwares/computadores consultando o mesmo CNPJ precisam coordenar a sequência; o aplicativo não controla a rede inteira.

O recebimento SOAP é gravado antes de aplicar a página, inclusive se o cancelamento chegar logo após o HTTP. Uma única transação SQL grava itens/pendências, índices do acervo, vínculos, chaves, auditoria, recibo aplicado e avanço do cursor. Arquivos são publicados imutavelmente antes do commit; rollback pode deixar órfão físico, que será reaproveitado no replay. Falha no banco não confirma parte da página.

Após uma queda, somente jobs Download identificados por auditoria CTE_STARTED que ficaram Running são marcados CTE_INTERRUPTED_INBOX_PRESERVED. Não há HTTP ou replay na inicialização. A próxima operação explícita de consulta/reprocessamento reaplica os recibos pendentes antes de usar a rede. Existe uma janela inevitável entre receber HTTP e conseguir gravar o recibo; se houver queda nesse intervalo, o cursor anterior e a reserva persistem.

## Banco, retenção e limites

A sexta migration adiciona Participation aos vínculos existentes, sem recriar o banco, e cria CteDistributionReceipts e CteDistributionItems. Ambas têm filtro de organização, revisão e FKs compostas; cada item aponta para seu recibo/empresa. NSU é único dentro do recibo. Reconsultas pontuais podem criar outro recibo, mas os XMLs do acervo deduplicam pelo hash.

Resposta SOAP: até 32 MiB; lote: até 50 docZip; XML descompactado: até 10 MiB/item e 64 MiB somados. Base64, GZip CRC/ISIZE e limites XML são verificados. DTD, resolução externa e instruções de processamento são proibidos; profundidade/nós/atributos são limitados. O parser fiscal aplica seus próprios limites mais estritos. IDs de pendências são lidos para uma lista de trabalho e os IDs agrupados de 50 para progresso; cada payload é carregado isoladamente e o rastreamento EF liberado após cada item e página.

SOAP e bytes docZip descompactados ficam retidos no SQLite sem cifra adicional, além dos XMLs arquivados em disco. Isso aumenta o banco e preserva evidência/retomada. Não há expurgo, backup automático, exportação em lote ou instalador nesta fase. Proteja e faça backup do conjunto de dados; DPAPI continua dependendo do perfil Windows original. Logs técnicos não recebem XML, PFX, senha, xMotivo ou mensagens arbitrárias de exceção.

## Fontes conferidas em 16/10/2026

- [Índice oficial das NTs](https://www.cte.fazenda.gov.br/portal/listaConteudo.aspx?tipoConteudo=Y0nErnoZpsg%3D): versão mais recente listada para distribuição é a 1.05.
- [NT 2015.002 v1.05 — documento primário](https://www.cte.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=LLSQYxGtwv0%3D), preservado em sources/phase7/NT2015_002_v1_05.pdf.
- [Pacote de distribuição oficial](https://www.cte.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=l6I2ehbBicE%3D), preservado como ZIP e XSDs extraídos sem alterações.
- [Relação oficial — produção](https://www.cte.fazenda.gov.br/portal/webServices.aspx?tipoConteudo=wpdBtfbTMrw%3D) e [homologação](https://hom.cte.fazenda.gov.br/portal/webServices.aspx?tipoConteudo=wpdBtfbTMrw%3D), cópias HTML preservadas para os endpoints.
- NT Conjunta DFe 2025.001 e pacote CT-e 4.00 preservados na Fase 4; usados para identificação/participantes. Não se trata o pacote antigo da distribuição como se já contivesse a revisão alfa.

## Homologação pendente

Executar no Windows com A1 participante autorizado, cadeia ICP-Brasil/LCR acessíveis e consulta isolada de outros sistemas do mesmo CNPJ. Conferir WSDL/binding/wrappers, resposta real para 57/67, nomes dos schemas docZip e protocolos. Conferir CNPJ alfa, autXML com referências mascaradas, apresentação WPF, cancelamento/reinício, espera persistida e backup/retomada. Nenhuma requisição fiscal com certificado real foi realizada nos testes desta entrega.
