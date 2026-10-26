# NFC-e SC — Fase 8 / 0.8.0

## Operação

1. Cadastre a empresa com CNPJ, UF SC, ativa e NFC-e habilitada.
2. Importe um A1 de contador/escritório e associe-o à empresa com fonte SEF_SC_NFCE. DEFAULT, SEF_SC_NFE e NFE_NACIONAL não substituem essa associação.
3. Abra Consultar NFC-e, selecione uma ou várias empresas (Ctrl/Shift ou Selecionar todas), participação e conteúdo. Produção é o único ambiente desta fonte.
4. Para continuar a distribuição, deixe chave vazia. O cursor é salvo por empresa/fonte/modelo/ambiente/ator/conteúdo. Não redefina NSU a zero após cada consulta.
5. Para uma chave, selecione uma empresa e informe modelo 65. O acervo local e seu hash são conferidos antes da rede; consulta pontual não avança o NSU de distribuição.
6. Habilite consultas oficiais na sessão e clique em Consultar selecionadas. A habilitação não faz chamadas por si só e é desligada ao fechar.
7. Consulte resultados, cStat, NSU e horário UTC. XMLs e eventos ficam disponíveis em Acervo de documentos, modelo NFC-e. Somente chaves ficam pendentes, sem XML fictício.

A fonte está em regime de testes desde 15/04/2026. A página oficial consultada em 18/10/2026 prevê a versão definitiva para 01/01/2027 e admite encerramento em caso de problema crítico. Não há integração nacional ou outras UFs presumida. Cadastro de contribuinte CPF, embora previsto no serviço, não é oferecido pelo cadastro de empresas CNPJ desta versão.

## Contrato implementado

SOAP 1.1 document/literal, namespace http://www.satnfce.sef.sc.gov.br/ws/distribuicao-v1. Endpoint: https://dfe.sat.sef.sc.gov.br/nfce/ws/distribuicao/DistribuicaoNfceDownload.asmx . A action termina /nfceDownloadContab. O WSDL tem nome de operação abstrata NfceDownloadContab, mas os elementos da mensagem, Response/Result e action começam com nfce minúsculo. O pedido distNFCeSC versão 1.00 é filho direto de nfceDownloadContab; não há pXml nem SOAP Header.

Dados: tpAmb=1, verAplic identifica a versão WGS instalada, cUF=42, CNPJ e escolha solRel (indXML, indAtor, ultNuNSU até 18 dígitos) ou solDFe/chAcesso. Ator 1 emitente, 2 destinatário, 3 qualquer um, 9 ambos. Conteúdo 0 chaves, 1 XML. Papel OtherParticipant não é um filtro aceito. Não há data inicial/final, consNSU ou manifestação automática.

Resposta retDistNFCeSC 1.00, loteDistComp base64/GZip com loteDistNFCeSC 1.00 sem namespace, grupos distNFCeSC/NSU/chAcesso e nfeProc/procEventoNFe no namespace NF-e. Máximo 50, NSUs crescentes, cursor igual ao último NSU retornado. Modelo da chave/XML deve ser 65, ambiente produção e participação compatível quando há documento integral. Eventos aceitos: 110111 e 110112. Uma chave não é XML completo; evento isolado não prova presença da nota.

O boletim contém inconsistências editoriais (distNfceSC na tabela versus distNFCeSC no exemplo, menções a modelo 55/CT-e em trechos copiados e nfeProc/nfceProc). A implementação usa os nomes dos exemplos coerentes de NFC-e e modelo 65, conserva o retorno inválido e nunca o converte silenciosamente. A homologação com retorno real deve confirmar esses detalhes. Não se inventou um XSD oficial a partir da tabela: o WSDL obtido tem xs:any para o payload, e os XSDs de distribuição completos não foram disponibilizados no material consultado.

## Esperas e autorização

117 (vazio) ou 118 com menos de 50 itens: 12 horas; 118 com 50: continuação serial. 110: uma hora. 657: bloqueio conservador de 12 horas, compartilhado entre empresas que associem o mesmo certificado nessa fonte/ambiente. Outros erros: espera conservadora de uma hora, sem avanço. Não tentar contornar espera trocando filtros/chave quando há bloqueio. O transporte global serializado aguarda 250 ms entre chamadas e usa timeout de 60 s.

8002 indica recusa de vínculo contábil; 8004 falta de vínculo com a chave; 632 fora da janela; 116/217 chave/NSU não localizado conforme retorno. O aplicativo mostra cStat e mensagem local estática, sem propagar texto arbitrário do servidor. Validade, chave privada, política ICP-Brasil e cadeia/revogação são verificadas antes do envio; só o servidor decide acesso. Certificado de empresa participante não substitui o de contador nesta fonte.

O serviço disponibiliza mês corrente e dois anteriores, com possível atraso de até um dia. Não existe recuperação histórica ilimitada nesta fase. Agendamento só será tratado na fase 12; abertura do aplicativo não consulta o serviço.

## Persistência e recuperação

Reutiliza INfeSyncService, NfeSyncGate e NfeDistributionReceipts com ProviderCode=SEF_SC_NFCE. O nome técnico Nfe* não mistura os modelos: o estado usa Model=NFCe; jobs e arquivos usam 65, e certificados/recibos são filtrados pela fonte.

A resposta SOAP e SHA-256 são gravados antes da aplicação, inclusive se houver cancelamento logo após o recebimento. Todos os documentos/eventos, chaves, índice, links, auditoria, recibo aplicado e cursor são confirmados em uma transação por página. XML malformado, modelo/ambiente divergente, conflito ou falha SQL preservam recibo e cursor anterior; arquivos físicos publicados antes de rollback podem ficar órfãos e são reaproveitados no replay.

A próxima consulta reaplica recibos pendentes antes de novo HTTP, inclusive com rede desabilitada. Não se descarta automaticamente um lote bloqueado: correção de parser/contrato deve ser testada e o recibo reaplicado. Fechar exige aguardar o ponto seguro após cancelar. Ao reiniciar, job interrompido é sinalizado; não há replay nem HTTP automático.

Reserva de 12 horas é persistida antes do HTTP para timeout/queda/cancelamento incerto. Erro comprovado de preflight libera a reserva para corrigir certificado/configuração. Não apagar cursores ou recibos como tentativa de recuperação.

SOAP/BLOB fica no SQLite e pode crescer; não há expurgo nem criptografia adicional ao cofre A1. Backup deve incluir banco e XMLs, com acesso restrito. XMLs do lote são serializados do nó fiscal para arquivo imutável; SOAP/GZip original fica no recibo. Não se afirma preservação byte a byte da serialização interna recebida após extração, nem verificação XMLDSig/XSD fiscal completo.

## Validação e limites

Suite sintética usa o codec/SQLite/acervo reais e transporte/relógio simulados. Ver docs/VALIDATION.md e NfceTests.cs. Captura WSDL verifica elementos/action/wrapper e compila o XSD externo do SOAP, sem afirmar validação dos dados internos por xs:any. Não houve download fiscal autenticado, teste de vínculo real nem avaliação visual WPF/DPAPI em Windows. A1 real, cadeia/revogação, XML de retorno real e XSD completo permanecem na homologação.

## Fontes

- Página oficial: https://www.sef.sc.gov.br/saiba-mais/web-service-para-download-de-nfce (captura phase8/official-service.html).
- BT SC-2026/003 v1.00-1, agosto/2026: https://www.sef.sc.gov.br/api/download?id=8609&mime=application/pdf (PDF fornecido preservado em docs/sources).
- WSDL público obtido em 18/10/2026: https://dfe.sat.sef.sc.gov.br/nfce/ws/distribuicao/DistribuicaoNfceDownload.asmx?WSDL (phase8/nfce.wsdl).

Hashes e origem das capturas em docs/sources/phase8/sources.json. A validação local não é homologação pela SEF/SC.
