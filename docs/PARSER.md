# Parser DF-e — Fase 4 (0.4.0)

## Usar

Na janela principal, abra **Inspecionar DF-e**. Cole uma chave e clique **Validar chave**, ou escolha **Abrir XML**. A inspeção mostra identificação, modelo, emitente/destinatário quando presentes, valores, datas, protocolo/status declarados, evento e SHA-256 do arquivo original.

O arquivo `assets/EXEMPLO_SINTETICO_NAO_AUTORIZADO.xml` serve para testar a tela: é apenas um exemplo de metadados, sem assinatura, sem protocolo e sem validade fiscal; não deve ser transmitido.

O arquivo não é alterado nem copiado para o acervo. Banco, certificados e NSU não são modificados. Nenhuma consulta externa ocorre. A inspeção não exige certificado A1. Erros limpam o resultado anterior para não deixar um sucesso antigo associado ao arquivo recusado.

O resultado indica consistência dos metadados lidos. **Não comprova autenticidade, assinatura digital, validação XSD integral, existência do documento ou situação atual na SEFAZ.** Protocolo/cStat/cSitNFe são declarações do XML. O conteúdo tributário detalhado não é apurado nem validado. Documento sem protocolo fica com situação não confirmada. Resumo e evento nunca se tornam XML integral por terem chave/protocolo.

## Chave

Entrada admite apenas espaços ASCII, tabulação e quebras de linha como separadores, além de letras minúsculas convertidas para maiúsculas. Não retira pontuação ou caracteres arbitrários. XML deve conter chave canônica sem essa formatação de colagem.

Valida 44 posições, alfabeto/posição de letras, UF do IBGE, mês 01–12, modelo, documento do emitente, número maior que zero, tipo de emissão não zero e DV. A validação estrutural não aplica calendário de implantação/regimes de contingência por UF nem confirma inscrição na Receita. O ano da chave é interpretado como 2000+AA; não faz consulta ou bloqueio pelo relógio atual.

| Campo | NF-e/NFC-e/CT-e/CT-e OS | NFCom |
|---|---|---|
| UF | posições 1–2 | posições 1–2 |
| AAMM | 3–6 | 3–6 |
| CNPJ/CPF preenchido à esquerda | 7–20 | 7–20 |
| Modelo | 21–22 | 21–22 |
| Série | 23–25 | 23–25 |
| Número | 26–34 | 26–34 |
| Tipo de emissão | 35 | 35 |
| Site autorizador | Não se aplica | 36 |
| Código numérico | 36–43 (8 posições) | 37–43 (7 posições) |
| DV | 44 | 44 |

NT Conjunta DF-e 2025.001: letras somente nas primeiras 12 posições do CNPJ. DV da chave usa os 43 caracteres anteriores ao DV, valor ASCII−48 e pesos 2 a 9 da direita para a esquerda. O resto 0/1 produz DV 0; demais, 11−resto. CNPJ tem os próprios dois DVs conferidos.

CPF com três zeros à esquerda é reconhecido na NF-e com série 910–969 e NFC-e com série 920–969. Outros regimes especiais de CPF/NFF não são inferidos fora dessas séries nesta entrega. Série, número e código ficam como strings para preservar zeros. **A chave não informa produção/homologação.**

## XMLs suportados

| Família | Namespace (http://www.portalfiscal.inf.br/...) | Documento individual | Processado | Versão |
|---|---|---|---|---|
| NF-e 55 / NFC-e 65 | nfe | NFe / infNFe | nfeProc | 4.00; 3.10 com dhEmi |
| CT-e 57 | cte | CTe / infCte | cteProc | 4.00; 3.00 com dhEmi |
| CT-e OS 67 | cte | CTeOS / infCte | cteOSProc | 4.00; 3.00 com dhEmi |
| NFCom 62 | nfcom | NFCom / infNFCom | nfcomProc / NFComProc (SC) | 1.00 |

Aceita prefixos XML ou namespace padrão, mas não troca URI, caixa das tags ou versão silenciosamente. Nos documentos, confere Id/chave versus modelo, UF, CNPJ/CPF do emitente, série, número, tpEmis, código numérico, cDV e mês/ano da emissão. Na NFCom também nSiteAutoriz. Protocolo do processado deve referir-se à mesma chave/ambiente. Nas respostas declaradas de autorização, nProt e dhRecbto são exigidos.

Totais: NFe/total/ICMSTot/vNF; CT-e/CT-e OS/vPrest/vTPrest; NFCom/total/vNF. São valores declarados, sem recalcular tributos. Decimal sem perda de precisão; formato aceito usa ponto, duas casas e até 15 posições inteiras. Datas exigem horário e offset explícito (Z ou ±hh:mm), preservado em DateTimeOffset. Não atribui fuso artificial a dEmi sem horário. NF-e admite nProt de 15 ou 17 dígitos, CT-e/OS de 15, NFCom de 16 conforme fontes consultadas.

| Outros tipos | Raízes | Versões / comportamento |
|---|---|---|
| Resumo NF-e | resNFe | 1.00/1.01; ambiente null pois não vem no resumo; cSitNFe declarado |
| Resumo evento NF-e | resEvento | 1.00/1.01; ambiente null e situação principal não confirmada |
| Evento NF-e/NFC-e | evento / procEventoNFe | 1.00 |
| Evento CT-e/OS | eventoCTe / procEventoCTe | 3.00/4.00 |
| Evento NFCom | eventoNFCom / procEventoNFCom | 1.00 |

Eventos: lê tipo, sequência, chave e data; confere Id e, quando processado, chave/tipo/sequência/ambiente da resposta. Extrai protocolo/cStat/dhRegEvento. Não valida todo o detEvento. A nota principal permanece sem mudança: conciliar cancelamento autorizado, evento 136 não vinculado, correção, substituição e eventos de marcação é responsabilidade do acervo/provedor futuro. Os eventos com sequência de dois/três dígitos são conferidos segundo as formas de Id aceitas; isso não é validação completa XSD.

Não interpreta nesta fase SOAP/lotes/docZip, GZip/base64, ZIP, resCTe, CT-e Simplificado, XML NFS-e, NFC-e legado anterior aos leiautes listados, recibos soltos, consulta completa retConsSit ou inutilização. Inutilização é faixa/registro separado e não tem chave individual de nota; não gera documento fictício. Raiz/versão não reconhecida retorna erro explícito. A expansão de envelopes ficará nos adaptadores/importador.

## Segurança e limites

- XML individual de 1 byte a 10 MiB; SHA-256 dos bytes exatos, incluindo BOM/declaração.
- XmlReader com DTD proibido, XmlResolver=null e limite de caracteres; instruções de processamento recusadas.
- Até profundidade 64, 200.000 nós e 64 atributos por elemento no preflight.
- Campos extraídos até 1024 caracteres; atributos de identificação/versão até 128.
- Seleção por caminho direto/namespace; duplicatas de campos críticos ou identificação ambígua recusadas.
- Mensagens estáticas com código; não revelam trecho do XML, arquivo local referenciado ou exception.Message interno.
- Não resolve SchemaLocation, XInclude ou entidades externas, não executa transformações e não consulta URLs.

O parser não carrega todo o conjunto de XSDs oficiais para validar XML. Os materiais em sources/phase4 são evidência/referência dos formatos; não são schemas dinamicamente carregados pelo aplicativo. Homologação visual WPF continua pendente no Windows.

## Fontes consultadas em 16/10/2026

- [SVRS — Documentos CT-e](https://dfe-portal.svrs.rs.gov.br/Cte/Documentos): MOC 4.00 e pacote NT 2026.004 RTC. Arquivo preservado em `sources/phase4/CTE_Schemas_2026.zip`.
- [SVRS — Documentos NFCom](https://dfe-portal.svrs.rs.gov.br/Nfcom/Documentos): MOC 1.00a (Visão Geral e Anexo I), pacote NT 2026.004 e Nota Técnica Conjunta CNPJ. PDFs e ZIP preservados em `sources/phase4/`.
- [NT Conjunta DF-e 2025.001 — portal NF-e](https://www.nfe.fazenda.gov.br/Portal/exibirArquivo.aspx?conteudo=5ZkvIZt10mQ%3D): DV/chave alfanumérica, Anexo II. PDF obtido no repositório SVRS e preservado.
- [NF-e NT 2025.002 RTC — protocolo 15/17](https://hom.nfe.fazenda.gov.br/PORTAL/exibirArquivo.aspx?conteudo=ELLzu7b%2FYHU%3D).
- [NF-e NT 2023.002 — emitente CPF na NFC-e](https://www.nfe.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=4m9xb5EYa%2Fw%3D).
- [NF-e NT 2023.004 — validação da chave/emitente](https://www.nfe.fazenda.gov.br/Portal/exibirArquivo.aspx?conteudo=%2FBmhvTh1gRI%3D).

Alguns PDFs NFe redirecionam no acesso automatizado; os trechos de leiaute foram conferidos na indexação oficial. Os pacotes CT-e/NFCom e a NT Conjunta foram baixados e inspecionados localmente. As fixtures dos testes são sintéticas, sem assinatura/autorização, e não devem ser usadas para transmissão.

## Conferência visual no Windows

1. Abrir a nova tela em diferentes escalas de exibição e conferir navegação/rolagem.
2. Colar chave com espaços ou CNPJ em minúsculas; conferir campos/zeros. Alterar DV e verificar erro sem resultado antigo.
3. Abrir `assets/EXEMPLO_SINTETICO_NAO_AUTORIZADO.xml`; conferir total zero, homologação, situação não confirmada e hash.
4. Testar XML processado, resumo e evento sintéticos; conferir rótulos de ambiente ausente e situação declarada.
5. Tentar XML com DTD/raiz errada; conferir mensagem segura e responsividade. Fechar a janela durante leitura deve aguardar a operação.

A interface de inspeção não exige acesso ao cofre; os dois WindowsFact da Fase 3 devem ser homologados separadamente antes de usar credenciais reais.

Na fase 9 foi incluída a raiz processada NFComProc, grafia publicada no boletim de distribuição SC, como perfil explícito. O XSD fiscal nacional declara nfcomProc. Ambos exigem NFCom/infNFCom/protNFCom e os mesmos metadados/modelo/ambiente, preservando bytes/hash. Isso não equivale a validar NFComProc no XSD nacional nem permite outras mudanças arbitrárias de caixa.
