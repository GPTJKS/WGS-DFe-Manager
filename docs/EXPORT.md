# Exportação de XMLs

## Operação

1. Em Consultas, use **Exportar XMLs do período**; no acervo, use **Exportar XMLs locais** ou **Exportar selecionados**.
2. Confira empresas, período de emissão, modelos e ambiente. A exportação por filtros inclui todas as páginas; a seleção explícita inclui somente os itens escolhidos.
3. Escolha uma pasta existente fora do armazenamento da aplicação. **Pasta** é o padrão; ZIP é uma alternativa explícita.
4. Use **Conferir seleção** para estimar o volume e **Exportar para o destino** para copiar. O resumo fica dentro do aplicativo e pode ser copiado por **Copiar resumo**.

## Estrutura

```text
Empresa / Situação / Ano / MM - Mês / Intervalo / Tipo / Ambiente / arquivo.xml
```

Exemplo: `Sorvetes São João/Autorizados/2026/10 - Outubro/01-10-2026 a 02-10-2026/NFC-e/Produção/<chave>.xml`.

O primeiro nível é o nome legível da empresa, sem prefixos técnicos, data de execução, GUID, hash ou CNPJ. Nomes iguais ou que colidam após sanitização recebem o final necessário do CNPJ. Acentos são preservados; espaços, caracteres inválidos e nomes reservados do Windows são tratados.

Ano e mês seguem a emissão de cada documento. O intervalo completo solicitado é mantido em cada mês; um único dia não repete a mesma data. Sem duas datas, os rótulos são explícitos: `Todo o período`, `A partir de ...` ou `Até ...`.

Autorizados e cancelados têm pastas separadas. Denegados e não confirmados continuam corretamente identificados quando presentes; ausência de confirmação não vira autorização. Tipos e ambientes permanecem separados. Só são criadas pastas com arquivos válidos concluídos.

Uma nota vinculada a duas empresas selecionadas pode gerar uma cópia para cada empresa. Documentos realmente sem vínculo podem ser incluídos explicitamente na pasta `Sem vínculo`. Filtrar por papel não transforma um vínculo excluído em ausência de vínculo.

## Eventos e versões

Eventos vinculados acompanham a nota por padrão, mesmo quando registrados depois do período de emissão. Ficam na mesma folha, com nome fiscal por chave, tipo e sequência. Eventos sem nota são optativos e conservam seu próprio filtro de data. Selecionar um evento não acrescenta automaticamente a nota.

Versões alternativas continuam optativas; divergências exigem também habilitar versões. Usam sufixos legíveis de versão/divergência, sem hash. A classificação conservadora das versões é preservada. Os bytes dos XMLs não são reserializados e não são produzidos DANFE/PDF.

## Reexportação, conflito e cancelamento

A exportação em pastas escreve diretamente na estrutura acima. Cada XML passa por temporário na pasta final, conferência de tamanho/SHA-256 e movimentação atômica sem sobrescrita.

- Idêntico existente: preservado e contabilizado como ignorado, sem nova cópia nem alteração de data de modificação.
- Conteúdo divergente com o mesmo nome: preservado, com conflito e caminho apresentados no resumo.
- Cancelamento: mantém arquivos anteriores e os novos XMLs já concluídos; limpa temporários próprios e somente diretórios novos que continuem vazios.

A operação não apaga nem reorganiza pacotes antigos ou arquivos de terceiros existentes no destino. O destino não pode coincidir com, ficar dentro de ou conter a pasta de dados. Caminhos próximos do limite conservador de 259 caracteres recebem redução legível do nome da empresa; se não couber com segurança, escolha um destino mais curto.

ZIP mantém a mesma hierarquia interna e nome legível por empresa/período, sem relatórios. É preparado e verificado antes da publicação atômica. Repetição idêntica reutiliza o ZIP; conteúdo divergente não é sobrescrito.

## Resumo e auditoria

Nenhum JSON, CSV, XLS/XLSX, HTML, TXT ou outro relatório auxiliar é gerado junto aos XMLs. Não há opções de manifesto, planilha ou salvar CSV na exportação. O resumo interno informa empresa, período, gravados, idênticos ignorados, substituídos, conflitos, falhas e não processados. A política atual não substitui arquivos divergentes; por isso, substituídos é zero.

Jobs, itens, histórico e comprovantes de integridade permanecem no armazenamento interno da aplicação. Eles não são arquivos ocultos no destino. A cópia em disco e a auditoria no SQLite não são uma transação única; se a gravação do histórico falhar após cópia válida, os XMLs são preservados e a aplicação informa a pendência. No reinício não existe exclusão ou retomada automática de uma exportação interrompida.

## Limites e preservação fiscal

São mantidos os limites de 100.000 cópias, 10 GiB acumulados, 10 MiB por XML e até 10.000 IDs de seleção explícita. Cópias por participação contam para os limites. A prévia usa metadados; o tamanho efetivo e a integridade são conferidos na execução. Ignorar inválidos permite continuar com os demais, mostrando o motivo de cada falha.

A exportação usa o acervo e seus comprovantes locais, sem rede, uso de A1 ou alteração de NSU. Não valida assinatura digital, autenticidade SEFAZ ou XSD fiscal completo e não representa integração homologada com sistema contábil. Os testes atuais e as evidências estão em [validação da terceira rodada](validation/redesign/rodada3/RELATORIO.md).
