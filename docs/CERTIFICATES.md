# Certificados A1 — Fase 3

## Operação

1. Abra **Certificados A1** na janela principal. Informe nome, finalidade declarada (empresa participante ou escritório/contador) e senha.
2. Clique **Escolher PFX/P12 e adicionar**. Limite de 5 MiB e senha até 4096 caracteres. Só um certificado de usuário com chave privada por arquivo; certificados CA são rejeitados como credencial. Cadeia sem outras chaves privadas é permitida.
3. Selecione a linha para consultar Subject, CPF/CNPJ reconhecido no SAN, SHA-256 e vigência. Dados de identidade não são validados junto à Receita. Não se infere documento do texto livre do Subject. Lista em UTC; detalhe em horário local.
4. **Testar chave privada** reabre o cofre, confere SHA-256 e assina/verifica desafio RSA/ECDSA. Não testa conexão fiscal, cadeia ICP-Brasil, revogação, procuração ou autorização.
5. **Associar às empresas** oferece pesquisa, páginas de 50, seleção múltipla Ctrl/Shift e fonte. Associe uma página por vez. Mesma credencial pode atender várias empresas; fontes diferentes podem usar outra credencial. DEFAULT é associação geral de cadastro. Vínculo existente a outro certificado exige remoção explícita.
6. **Substituir selecionado** usa novo nome/finalidade/senha/arquivo, exige confirmação e preserva ID e associações. Confira o titular: preservar vínculos não comprova autorização do novo titular.
7. **Remover** exige confirmação e retira cadastro/vínculos. Preserva empresas, acervo e arquivo original. **Remover associação** preserva certificado e outros vínculos.

Senha é limpa ao iniciar importação e ao fechar a janela. Certificados vencidos/ainda não vigentes podem ser importados para revisão, mas recebem alerta. Alertas de vencimento começam em 30 dias e aparecem também ao abrir a janela principal. “No prazo” descreve somente as datas.

## Cofre e recuperação

`Certificates/<UUID>.vault` recebe envelope PFX+senha cifrado por DPAPI CurrentUser. SQLite, configurações, logs e auditoria não recebem esses segredos. Nenhuma chave é persistida automaticamente no Windows Store. Arquivo original do usuário não é alterado. Não há exportação de credenciais.

Use o mesmo perfil Windows. Copiar cofre para outra conta ou reinstalar Windows pode impedir decifração. Guarde original e senha separadamente conforme política do escritório. Uma cópia ZIP do cofre não substitui recuperação. Em caso de perda de perfil, reimporte o original com **Substituir** para preservar vínculos.

Falhas na exclusão do blob antigo pós-commit ficam em fila persistente. Reabrir programa ou **Atualizar / limpar pendências** tenta novamente. Referência ativa nunca é excluída. Crash entre escrita do novo blob e commit pode deixar blob cifrado órfão; não há reconciliação automática desse intervalo. Não apague arquivos sem conferir referências ativas. Veja SECURITY.md.

## Homologação Windows pendente

- Rodar todos os testes: os dois WindowsFact devem passar, sem serem ignorados.
- Adicionar PFX/P12 sintético, reabrir e testar; senha errada deve preservar cadastro anterior.
- Conferir e-CPF/e-CNPJ sintéticos com SAN OCTET/PRINTABLE e validade.
- Substituir credencial ligada a duas empresas, remover e conferir limpeza.
- Conferir vencido, vencendo e início futuro nos alertas.
- Outra conta Windows deve falhar ao decifrar o mesmo cofre.
- Conferir escala da tela, seleção múltipla, modais, navegação e responsividade.

Linux compila WPF e testa serviços com cofre em memória exclusivo dos testes. Não executa DPAPI nem avaliação visual Windows. Homologar antes de adotar certificados reais. A3 possui contrato de chave para evolução futura, sem suporte operacional nesta fase.

## Fontes

- [Microsoft — DPAPI](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection).
- [Microsoft — LoadPkcs12Collection](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificateloader.loadpkcs12collection?view=net-10.0).
- [ITI — DOC-ICP-04](https://repositorio.iti.gov.br/resolucoes/Resolucao179_DOC-ICP-04.htm), item 7.1.2.3/7.1.2.4: CNPJ SAN .3.3; e-CPF titular .3.1 nas posições 9–19; OCTET/PRINTABLE. CPF do responsável .3.4 não é usado como titular. Extração não valida cadeia/identidade.
- Boletim SEF/SC v2.02-1 fornecido em `sources/sefsc-nfe/boletim-v202-1.pdf`, seções 02.4/02.5: identidade do contabilista e vínculo verificado pelo serviço.

Fontes consultadas em 07/10/2026.
