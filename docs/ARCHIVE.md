# Acervo — Fase 5 (0.5.0)

## Importar e consultar

Abra **Acervo de documentos**. Use **Importar XMLs / ZIPs** (seleção múltipla) ou **Importar pasta e subpastas**. O aplicativo identifica o modelo pelo conteúdo XML, não por prefixo do nome. Diretórios e ZIPs consideram arquivos com extensão `.xml`; ZIPs externos selecionados ou encontrados também são lidos. PDFs/outros arquivos são ignorados. ZIP dentro de ZIP não é expandido.

A importação copia os bytes originais para a pasta de dados e indexa o conteúdo; não altera o arquivo de origem. Não usa certificado, não consulta SEFAZ e não avança NSU. A configuração e a identidade do escritório permanecem as mesmas das fases anteriores.

Na aba Documentos, pesquise por chave parcial/completa, empresa, emitente/destinatário, fornecedor/nome, número, série, período, valor, modelo, ambiente e situação declarada. Nome/fornecedor ignora acentos e caixa. CNPJ/CPF aceita máscara nos filtros próprios. Período usa a data local da emissão, preservada do XML; dinheiro usa centavos inteiros exatos, sem float. Na tela digite valores no formato brasileiro (ex.: `1.234,56`). A lista é paginada em 50 registros.

O papel se refere à empresa selecionada (emitente/destinatária/ambos). **Sem vínculo com empresa** lista documentos importados sem participante cadastrado. O importador não cria empresas automaticamente: ele vincula CNPJ do emitente/destinatário às empresas existentes, inclusive inativas. Se as duas participarem, compartilham o mesmo documento físico/indexado; se emitente e destinatário forem a mesma empresa, há um vínculo com papel Ambos. Depois de cadastrar novas empresas, use **Atualizar vínculos com empresas** ou reimporte o mesmo XML.

Na aba Eventos, os filtros aplicáveis são busca por chave/tipo, modelo e ambiente; eventos ainda sem nota também aparecem. Os demais filtros da aba Documentos não são aplicados à lista de eventos.

Selecione um documento e **Detalhes / versões / XML** para ver arquivos preservados e eventos vinculados. Cada versão tem hash, caminho, tamanho, indicador de conflito e marcação da versão principal. **Ver XML** confere SHA-256 e mostra texto formatado, sem abrir programa externo. A prévia limita-se a 200.000 caracteres; os bytes integrais continuam no disco. Para evento, selecione-o e carregue suas versões na primeira aba dos detalhes.

## Identidade, versões e conflitos

| Entrada | Resultado |
|---|---|
| Nova chave + ambiente | Novo documento principal |
| Mesmo SHA-256 | XML idêntico; não duplica cadastro/arquivo |
| Mesma chave com outra formatação, mesmos dados | Versão preservada; principal mantido |
| Principal sem protocolo + nova versão autorizada declarada com mesmo núcleo | Enriquece principal; preserva os dois XMLs |
| Versão sem protocolo depois de uma com protocolo | Preserva variante; não rebaixa principal |
| Mesma chave com emitente/destinatário/data/total divergentes | Versão conflitante preservada, sem sobrescrever principal |
| Dois protocolos/status declarados divergentes | Conflito preservado; sem escolha automática |
| Mesmo XML indexado, mas arquivo físico ausente | Reimportação restaura bytes e mantém cadastro |
| Arquivo existente adulterado | Falha de integridade; nunca sobrescreve o arquivo |
| Resumo de documento/evento | Relatado e ignorado como acervo integral |

Chave+ambiente+organização identificam a nota. Eventos usam também tipo+sequência; hash identifica versão física. Uma alteração de conteúdo não gera outra nota principal. Não há resolução manual de conflitos nesta versão: comparar as versões é possível, mas excluir/promover arbitrariamente um conflito fica fora desta fase. Hash não é validação de assinatura digital.

## Eventos e cancelamentos

Eventos são guardados separadamente. Se chegarem antes da nota, permanecem sem vínculo até importar documento com a mesma chave/ambiente. Carta de correção e demais eventos não mudam os dados originais da nota.

Situação Cancelado é uma **derivação declarada local**: evento 110111 (ou 110112 para NFC-e), retorno 135/155, protocolo presente, data de evento não anterior à emissão e recebimento não anterior ao evento. Evento simples sem retorno ou retorno 136 não cancela a nota principal. A situação/protocolo original da nota é preservada em DeclaredStatus e no XML. Versões divergentes do mesmo evento não substituem o evento canônico nem alteram a nota.

Esses critérios não validam assinatura, autenticidade do protocolo ou situação atual na SEFAZ. Documentos e eventos forjados mas estruturalmente consistentes podem ser lidos. Todos os status da tela são declarados pelo conteúdo, não homologação fiscal. A cobertura de raízes/leiautes continua a da Fase 4, em PARSER.md; CT-e Simplificado, inutilização e demais formatos não listados continuam recusados explicitamente. Resumo nunca substitui XML integral.

## Arquivos físicos e integridade

O armazenamento fica em:

`<pasta de dados>/XML/<OrganizationId>/<Producao ou Homologacao>/<ano>/<mês>/<CPF ou CNPJ emitente>/<modelo>/<SHA-256>.xml`

Ano/mês/emitente/modelo vêm da chave validada; nunca de caminhos internos do ZIP. Nome pelo hash permite preservar variantes sem sobrescrever. SQLite indexa os caminhos, mas os XMLs ficam no disco. Não apague/recrie o banco para atualizar. A migração é automática e preserva os dados anteriores.

**Conferir integridade** verifica presença e hash de cada arquivo indexado e aponta arquivos órfãos sob a pasta própria do escritório. Não apaga, não sobrescreve e não adota órfãos automaticamente. Arquivos `.part` de crash não são tratados como XMLs; devem ser revisados em manutenção com o aplicativo fechado. Antes de qualquer futura limpeza, confirmar referências e backup.

Banco e sistema de arquivos não compartilham transação: um crash/falha SQL após publicar XML pode deixar órfão. A publicação é imutável e reutilizável: reimportar a mesma origem normalmente permite indexá-lo, desde que íntegro. Se um arquivo existente estiver adulterado, preserve-o para revisão/quarentena manual antes de restaurar o original. O aplicativo não faz essa remoção por conta própria.

CheckAvailabilityAsync prepara os provedores futuros: só retorna Available após conferir arquivo, hash, tipo, chave e ambiente. Ausência é Pending; adulteração/inconsistência é TemporaryError. Available significa presença local não autenticada. Ter apenas registro no banco não basta.

## Lotes, cancelamento e relatórios

Progresso informa itens processados e registros novos (documentos e eventos). **Cancelar operação** preserva itens já confirmados. Reimportar as mesmas origens ignora arquivos idênticos e processa o restante. Não há retomada automática pela posição da pasta/ZIP. Cada item concluído grava checkpoint e alterações de índice na mesma transação; auditoria recebe somente ações/códigos/IDs seguros.

Após crash, o bootstrap marca jobs do acervo que ficaram Running como Failed/ARCHIVE_INTERRUPTED, mantendo progresso. Ele não reinicia consultas ou importações sozinho. Erro de armazenamento/banco interrompe o lote; erro de XML individual ou ZIP inválido permite seguir outras origens quando possível.

Na aba Última importação, veja resultado e motivo por arquivo. **Salvar relatório da importação** gera CSV desta sessão, com nomes/resultado/chave/código e neutralização de fórmulas; não inclui conteúdo XML. O histórico persistido de jobs/checkpoints não tem tela própria nesta fase.

## Limites

- Até 500 origens selecionadas; até 10.000 visitas/itens/entradas e fila de diretórios limitada.
- Profundidade de pastas até 32; própria pasta de dados ignorada; links simbólicos/reparse points não seguidos.
- ZIP até 250 MiB; até 10.000 entradas; soma declarada expandida até 512 MiB.
- XML até 10 MiB, tanto anunciado quanto realmente lido; razão de compressão acima de 200:1 é recusada para XML maior que 1 MiB.
- Até 1 GiB de bytes XML lidos por lote; demais limites de XML da Fase 4 permanecem.
- CRC-32 de entrada ZIP é conferido; caminhos absolutos, `..`, unidades/ADS, controles e links/dispositivos são recusados no preflight, antes de importar qualquer entrada desse ZIP.
- ZIPs com PDF/outros arquivos contam nos limites, mesmo que essas entradas não sejam expandidas.

Esses limites protegem a importação; divida acervos maiores em lotes. Falha de permissão de origem é relatada sem expor exceção interna. As verificações de links não são defesa contra malware operando na mesma conta. O acervo não tem cifra própria nem backup automatizado nesta fase; respeitar permissões/política de disco do escritório.

## Migração legada

A quarta migration acrescenta tabelas/campos sem apagar registros. O bootstrap preenche índices antigos em C#, usando decimal exato, sem cast de dinheiro a float SQLite. Para dados antigos sem offset, a data disponível em UTC é usada no filtro de período; não se inventa o offset original. Valores antigos fora de centavos/long permanecem sem índice de centavos e precisam de revisão. Caminhos antigos incompatíveis com o novo escopo seguro são apontados pela integridade; reimportar fonte íntegra é a rota de recuperação, sem apagar originais.

## Homologação Windows pendente

1. Compilar e executar a suite no Windows; homologar os dois WindowsFact DPAPI e reparse points/links em ambiente seguro.
2. Abrir o acervo em escalas de tela diferentes; conferir filtros, paginação, seleção e rolagem.
3. Importar a amostra sintética dos assets, pasta com subpastas e ZIPs mistos; repetir importação e conferir ausência de duplicação.
4. Conferir notas/eventos antes/depois, variantes/conflitos, relatório CSV, cancelamento e reimportação.
5. Renomear provisoriamente uma cópia de arquivo em ambiente de teste: integridade deve apontar ausência; reimportação deve restaurar. Arquivo adulterado deve ser preservado e recusado.

Usar apenas dados sintéticos nessa homologação. A UI foi compilada por cross-targeting no Linux; testes automatizados do acervo usam SQLite/arquivos reais isolados. A amostra dos assets não é documento fiscal autorizado.

Referências de formato: PARSER.md e os materiais oficiais em sources/phase4. APIs ZIP consultadas: [CRC32](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchiveentry.crc32?view=net-10.0) e [ExternalAttributes](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchiveentry.externalattributes?view=net-10.0). Nenhuma integração fiscal foi habilitada.
