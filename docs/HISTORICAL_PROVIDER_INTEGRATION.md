# Integração de provider histórico

O pacote não contém endpoints comerciais ou credenciais. Não implemente chamadas por tentativa de adivinhar API de fornecedor. Para integrar, obter contrato/documentação, ambientes, modelos recuperáveis, autorização, limites, semântica de custos, idempotência e payloads reais de teste.

## Ponto de extensão

IHistoricalDocumentProvider já define Capabilities, SubmitAsync(HistoricalRequest), GetStatusAsync(externalJobId) e DownloadAsync(externalJobId). Registrar HistoricalProviderRegistration via callback configureHistorical de FoundationRuntime, usando AddScoped quando o adapter depende de serviço scoped. Não registrar um fake no aplicativo. O registry oficial continua com as cinco fontes fiscais; históricos são registros separados.

Registration exige IsHistorical/IsImplemented, Code ASCII alfanumérico/underscore (1–64) único e PollSeconds 1–86400. Models deve conter somente os modelos de fato recuperáveis. IsPaid deve ser true para qualquer contrato que possa cobrar; o pipeline reserva cada método remoto. Não usar IsPaid=false para pular orçamento. IdempotentSubmission só pode ser true com garantia documentada de reenvio pelo identificador estável.

HistoricalRequest inclui organização, empresa, modelo, ambiente, uma chave e CorrelationId estável igual ao ID da linha. O adapter deve enviar esse ID como idempotency key quando suportado. Não confundir sua correlação local com um ID retornado pelo fornecedor. Submit precisa devolver ID não vazio, até 200 caracteres sem controles. GetStatus precisa devolver o mesmo ID. Completed permite Download; Running/Pending aguardam nova retomada; Failed/Cancelled/CompletedWithWarnings encerram como pedido sem recuperação. Adapter deve mapear resultado parcial de forma consciente ou baixar documentos via Completed, sem ocultar falhas.

Download retorna ProviderResult com RetrievedPayloads. AccessKey pode ser omitida, pois o parser lê o XML; se fornecida, precisa coincidir. Resumo/evento isolado não resolve a chave. Payloads fiscais precisam de chave/modelo/ambiente consistentes, participação da empresa e limites documentados em HISTORICAL.md. Retorno sem XML só encerra como não disponível quando Availability é NotFoundInSource, OutsideWindow ou NotRecovered; demais casos permanecem recuperáveis. NextNsu não é usado e nenhum cursor oficial é alterado.

## Rede e segredos

Injetar credenciais por serviço seguro, sem API key/token/senha no código, opções do lote, logs, URI ou appsettings. Há ISecretStore com implementação DPAPI CurrentUser disponível; usar referência não secreta e preferir diretório próprio de segredos históricos. Limpar buffers ao terminar, sem fallback em texto puro. Não reutilizar dados do certificado como se fossem credencial de API comercial.

Implementar cliente HTTPS com allowlist de hosts/paths contratados, sem redirect de credenciais, timeout finito, CancellationToken, limites antes/despois da descompressão e política de rate limit. O transporte SEFAZ não aceita hosts comerciais. O pipeline não introduz um HttpClient genérico sem restrições nem polling em loop. Opções de rede da sessão e políticas pagas são verificadas pelo coordenador antes dos métodos remotos; o adapter deve respeitar também seus próprios limites.

Não incluir corpo de erro do servidor ou secrets em exceções/ResultCode persistido. O coordenador usa códigos locais e ignora mensagens arbitrárias do provider. IDs externos ficam no banco, sem serem publicados no relatório CSV. Testar com mock/homologação do fornecedor antes de habilitar produção.

## Ensaios mínimos

Cache íntegro sem chamadas; arquivo corrompido/ausente; custo reservado antes do request; teto entre status/download; submit incerto com/sem idempotência; ID estável; status aguardando; cancelamento após submit/download; retorno errado por chave/ambiente/empresa; payload misto/eventos; hash; falha SQL/rollback; retomada offline; isolamento tenant e rotação segura da credencial. Testes sintéticos existentes em HistoricalTests não homologam nenhuma API comercial.
