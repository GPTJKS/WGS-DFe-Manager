# Providers — versão 0.12.0

SEF_SC_NFE, NFE_NACIONAL, SEF_SC_NFCE, NFCOM e CTE estão implementados e registrados. O uso operacional precisa de homologação Windows/certificado real. Rede desabilitada por padrão e habilitação explícita na janela NF-e. Usar INfeSyncService para persistir recibos e controlar cursor/consumo.

| Provider | Operação | Ambiente | Situação |
|---|---|---|---|
| SEF_SC_NFE | NSU ou chave; filtros ator/XML | Produção | Implementado a partir de BT 2.02-1, XSD fornecidos e WSDL obtido |
| NFE_NACIONAL | distNSU ou consChNFe; resumos/docZip | Produção e homologação | Implementado a partir da NT 2014.002 v1.40; conferência WSDL nacional e homologação pendentes |
| Acervo local | Chave/ambiente + verificação física | Ambos | Implementado na Fase 5 |
| CTE — CT-e / CT-e OS | distNSU / consNSU; fluxo compartilhado | Produção e homologação | Implementado com NT 2015.002 v1.05 e XSD oficiais; WSDL e homologação nativa pendentes |
| SEF_SC_NFCE | NSU ou chave modelo 65; ator/XML | Produção (serviço em testes) | Implementado com BT SC-2026/003 v1.00-1 e WSDL real; XSD completo e homologação A1 pendentes |
| NFCOM — NFCom SC | NSU ou chave 62; ator/XML | Produção (serviço em testes) | BT SC-2026/003 v1.00-1 e WSDL real; implementação com homologação A1 pendente |
| Histórico externo | Submit / status / download por adapter | Conforme contrato | Pipeline implementado; nenhum adapter comercial conectado no pacote |

Não há emissão, manifestação automática, serviço pago, scraping de portal ou promessa de acesso histórico ilimitado. Capabilities do provider não garantem autorização ao usuário. DEFAULT não substitui associação por fonte. Empresas nacionais exclusivamente transportador/autXML não são promovidas a emitente/destinatário: o recibo fica pendente para tratamento desse papel. Detalhes, endpoints, formatos, waits, limitação de armazenamento, fontes e homologação: docs/NFE.md.


Na fonte CTE, não há filtro de modelo/ator/data na requisição nem consulta por chave no contrato consultado. Capabilities declara key lookup=false. Recebimentos fora do escopo são preservados com motivo, não descartados ao avançar o cursor de recebimento. Para 57/67 reconhecidos, os selecionados/habilitados são arquivados. O reprocessamento local trata modelos antes desmarcados sem nova chamada. Ver docs/CTE.md.

NFC-e usa INfeSyncService com fonte SEF_SC_NFCE e modelo 65. Não usar NFE_NACIONAL para NFC-e. Contrato próprio com nfceDownloadContab (minúsculo no elemento e SOAPAction), pedido distNFCeSC direto, sem pXml, namespace distribuiçao-v1 e versão 1.00. Ver docs/NFCE.md.

NFCom SC reutiliza o coordenador INfeSyncService com fonte NFCOM e modelo 62. NfeWireCodec possui perfis explícitos para os três contratos SC; NFCom tem namespace/raízes/payloads próprios. Associações antigas com código NFCOM passam a atender essa fonte SC; não representam um provider nacional. Seis eventos permitidos conforme boletim. Ver docs/NFCOM.md.

Na fase 10, o registry de adapters históricos é separado das cinco fontes oficiais. Sem adapter correspondente, retorna NO_HISTORICAL_PROVIDER, sem HTTP ou XML fictício. Adapters sintéticos existem apenas nos testes. A rota oficial de recuperação é NF-e SC em SC/produção (Nacional nas demais combinações), NFC-e SC e NFCom SC em SC/produção. CT-e/OS por chave retorna sem fonte oficial aplicável; não transforma a distribuição por NSU em busca histórica por chave. Conferência local precede qualquer fonte. Espera/erro de credencial/envelope oficial não dispara fallback pago. Ver docs/HISTORICAL.md.

## Exportação (fase 11)

Exportação é inteiramente local: não chama providers oficiais/comerciais, não consome NSU e não acessa A1. A estrutura CNPJ/ano/mês/modelo organiza os XMLs para uso posterior; não constitui integração homologada com Domínio ou outro sistema contábil.

## Sincronização automática (fase 12)

Cinco fontes: NF-e SC, NF-e Nacional, CT-e/OS, NFC-e SC e NFCom SC. Scheduler usa coordenadores existentes com uma empresa por horário e até 100 lotes configuráveis; padrão 10. SC permanece produção; Nacional e CT-e aceitam os ambientes suportados por seus contratos. Nacional exige qualquer participação e XML completo; CT-e/OS usa seu cursor compartilhado e somente os modelos selecionados. Cada coordenador mantém o cooldown e as regras oficiais já documentadas. Não há nova API remota nem alteração de endpoint.
