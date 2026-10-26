# NFCom SC — Fase 9 / 0.9.0

## Operação

1. Cadastre empresa ativa com CNPJ, UF SC e NFCom habilitada.
2. Associe A1 de contador/escritório vinculado à fonte NFCom — SC (contador), código NFCOM. DEFAULT e associações NF-e/NFC-e não substituem essa fonte.
3. Abra Consultar NFCom. Selecione uma, várias ou todas as empresas, papel 1/2/3/9 e XML completo ou somente chaves. A fonte é SC e o ambiente é produção.
4. Deixe chave vazia para continuar pelo NSU salvo. Informe chave modelo 62 e uma única empresa para consulta pontual. A chave não contém ambiente, que é o da fonte.
5. Habilite consultas oficiais na sessão e clique em Consultar selecionadas. O checkbox não inicia consulta por si só; abertura e fechamento não fazem HTTP. A rede é desligada ao fechar.
6. Confira resultado por empresa, cStat, NSU e próxima tentativa em UTC. Abra o acervo para pesquisar NFCom e seus eventos, visualizar XML/hash e filtrar período. Chaves sem XML continuam pendentes.

Somente a fonte SC foi implementada para distribuição NFCom. Não se usou NFeDistribuicaoDFe nem uma consulta de protocolo como se fornecesse XML integral. O cadastro continua CNPJ, embora o serviço oficial também contemple contribuinte CPF. Homologação não é um ambiente disponível nesse contrato.

## Contrato oficial

BT SC-2026/003 v1.00-1, agosto/2026. WSDL real obtido em 18/10/2026. Endpoint HTTPS: https://dfe.sat.sef.sc.gov.br/nfcom/ws/distribuicao/DistribuicaoNfcomDownload.asmx . Namespace SOAP da aplicação: http://www.satnfcom.sef.sc.gov.br/ws/distribuicao-v1 . SOAP 1.1 document/literal; action termina /nfcomDownloadContab; elemento nfcomDownloadContab, response/result com a mesma caixa. O nome abstrato no WSDL é NfcomDownloadContab, mas a mensagem efetiva começa com nfcom minúsculo.

Pedido distNFComSC 1.00 diretamente dentro do método, sem pXml/Header: tpAmb=1, verAplic=WGS_0.9.0, cUF=42, CNPJ, escolha solRel/indXML/indAtor/ultNuNSU ou solDFe/chAcesso. NSU até 18 dígitos; CNPJ e chave alfanuméricos mantidos como texto. Ator 1 emitente, 2 destinatário, 3 qualquer um, 9 ambos. XML=1 ou chaves=0. Datas, consNSU e participação de transporte não existem nesse pedido.

Retorno retDistNFComSC 1.00, loteDistComp base64/GZip, loteDistNFComSC 1.00 sem namespace, grupos distNFComSC com NSU/chAcesso. Até 50 registros em ordem crescente e cursor igual ao último NSU recebido. Payload fiscal no namespace http://www.portalfiscal.inf.br/nfcom, modelo 62, ambiente produção. O parser confere nSiteAutoriz e código numérico próprios da chave NFCom e protocolo de 16 dígitos.

O BT escreve NFComProc para XML processado; o XSD fiscal nacional procNFCom_v1.00.xsd declara nfcomProc. Ambos são aceitos por perfis explícitos, com o mesmo conteúdo interno NFCom/infNFCom/protNFCom e validações, sem renomear o arquivo recebido. Isso não prova validade de NFComProc no XSD nacional. A tabela do BT também usa distNfcomSC enquanto o exemplo usa distNFComSC: adotou-se o nome dos exemplos. Retorno real deve ser conferido na homologação.

XSDs completos de distribuição SC (dist/ret/lote) não foram obtidos no material publicado consultado. O WSDL contém xs:any e não valida os dados fiscais internos. Os XSDs nacionais da fase 4 são referência, não uma validação dinâmica de assinatura ou do XML inteiro.

## Eventos

| Tipo | Interpretação no acervo |
|---|---|
| 110111 | Cancelamento; só afeta situação declarada com cStat 135/155, protocolo e datas consistentes |
| 240150 | Autorizada NFCom de ajuste; evento preservado, sem cancelar a original |
| 240140 | Autorizada NFCom de substituição; evento preservado, sem cancelar a original |
| 240151 | Cancelada NFCom de ajuste; evento preservado, sem cancelar a original |
| 240161 | Cancelada NFCom de cofaturamento; evento preservado, sem cancelar a original |
| 240162 | Substituída NFCom de cofaturamento; evento preservado, sem cancelar a original |

Eventos procEventoNFCom 1.00 permanecem separados e são ligados quando a nota chega. Um evento isolado não vira documento integral. O código de resposta é preservado; por exemplo, 136 não comprova cancelamento vinculado. Tipos fora do contrato ficam no recibo pendente, sem avanço. A fase não interpreta o grafo completo de documentos relacionados nem recalcula valores/tributos a partir de ajustes.

## Janela, regime e esperas

A página oficial consultada em 18/10/2026 informa testes desde 17/06/2026 e lançamento definitivo previsto em 01/01/2027; admite suspensão por problema crítico. A janela do BT é o mês atual e os três anteriores, com atraso possível de até um dia. Não presumir histórico ilimitado nem usar a janela da NFC-e (três referências no total).

117 ou 118 com menos de 50: 12 horas; 118 com 50 permite continuação serial. 110 espera uma hora. 657 bloqueia conservadoramente por 12 horas, incluindo outros filtros/chaves e empresas com o mesmo certificado nessa fonte/ambiente. Outros erros têm espera conservadora de uma hora, sem avanço. O transporte global é serial, com intervalo de 250 ms e timeout de 60 s.

8002 distingue recusa de vínculo contábil, 8004 acesso à chave, 632 fora de prazo. Erros usam mensagem estática local e cStat, sem expor texto arbitrário do servidor. Certificado precisa de finalidade contador, validade, chave privada, política ICP-Brasil e cadeia/revogação; a SEF/SC decide se há vínculo. Não é necessário ter certificado da raiz CNPJ da empresa consultada quando se usa contador autorizado.

## Persistência e recuperação

O pipeline técnico compartilhado tem nomes Nfe*, mas fonte NFCOM determina modelo 62, flag NFCom, recibos e estados próprios. O registry possui cinco fontes fiscais. Não há migration/dependência nova: as seis migrations existentes já contêm ProviderCode e Model.

SOAP e SHA-256 são gravados antes da aplicação, inclusive em cancelamento logo após HTTP. Documento/evento, arquivo indexado, links, chaves, auditoria, recibo aplicado e cursor entram na mesma transação por página. XML/layout/modelo/ambiente/papel divergente, conflito ou erro SQL mantém lote pendente e cursor anterior. Arquivos físicos publicados antes do rollback podem ser reaproveitados na retomada. Nunca apague um recibo ou reinicie NSU para contornar inconsistência.

A próxima consulta reaplica recibos próprios pendentes antes de novo HTTP, inclusive com rede desligada. Um recibo NF-e/NFC-e não é interpretado como NFCom. Uma chave já presente e íntegra no acervo retorna localmente; chave consultada externamente não avança o cursor de distribuição. Operações concorrentes são serializadas pelo mesmo gate que NF-e/CT-e/NFC-e.

Reserva de 12 horas é persistida antes de HTTP para proteger queda/timeout/cancelamento incerto. Erro comprovado de preflight libera reserva para correção. Fechamento aguarda cancelamento seguro; inicialização sinaliza jobs interrompidos e conserva inbox, sem download/replay automático.

SOAP/BLOB é retido sem expurgo ou criptografia adicional ao cofre A1 e pode aumentar o banco. Restrinja acesso e inclua SQLite/XMLs no backup. A extração do nó fiscal serializa XML em arquivo imutável; SOAP/GZip original fica no recibo. Não se afirma identidade byte a byte da serialização interna antes da extração, nem autenticidade XMLDSig.

## Homologação e testes

NfcomTests usa codec, SQLite e arquivos reais, com XML/certificados sintéticos e rede/relógio simulados. Inclui ambos wrappers, seis eventos, protocolos, filtros, cursores, bloqueios, cache, corrupção, cancelamento e rollback. As fases anteriores continuam cobertas. Consulte VALIDATION.md e docs/validation.

Pendem execução visual WPF/DPAPI em Windows, certificado A1 real, cadeia/revogação, vínculo contábil, resposta fiscal real e obtenção/conferência de XSD completo de distribuição. Não houve chamada fiscal autenticada. Não há Setup.exe, emissão, agendamento automático ou apuração tributária.

## Fontes preservadas

- Página oficial: https://www.sef.sc.gov.br/saiba-mais/web-service-para-download-de-nfcom .
- BT oficial: https://www.sef.sc.gov.br/api/download?id=8608&mime=application/pdf .
- WSDL: https://dfe.sat.sef.sc.gov.br/nfcom/ws/distribuicao/DistribuicaoNfcomDownload.asmx?WSDL .
- XSD nacional procNFCom_v1.00.xsd, do pacote oficial NFCOM_Schemas_2026_004.zip já preservado em docs/sources/phase4; a matriz de fontes originais está em PARSER.md.
- Relação oficial de serviços NFCom, para distinguir recepção/consulta/status de distribuição: https://dfe-portal.svrs.rs.gov.br/nfcom/Servicos .

Capturas novas e hashes em docs/sources/phase9/sources.json. O código implementado e os testes locais não são homologação pela SEF/SC.
