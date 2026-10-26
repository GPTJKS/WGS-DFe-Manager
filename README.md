# WGS DF-e Manager — 0.17.5

Aplicativo para Windows em C#/.NET 10, WPF, SQLite e EF Core para cadastro de empresas, consultas e organização de documentos fiscais eletrônicos.

Esta é a versão 0.17.5 aprovada. O repositório contém o código-fonte, testes, modelos de importação, referências técnicas e o script do instalador.

## Funcionalidades

- Cadastro, importação e pesquisa de empresas.
- Certificados A1 protegidos pelo Windows, com vínculos por empresa e fonte de consulta.
- Consultas de NF-e, NFC-e (SC), NFCom (SC), CT-e e CT-e OS.
- Acervo de XMLs, eventos, versões, filtros, inspeção, visualização e exportação.
- Recuperação histórica e automações opcionais, respeitando os controles das fontes.
- Operações normais da interface em Produção.
- Zoom persistente, atalhos de teclado e visualização somente das empresas selecionadas.
- Diagnóstico em linguagem simples, com detalhes técnicos disponíveis para suporte.

As chamadas fiscais dependem de autorização explícita na interface e de um certificado compatível. A configuração distribuída mantém solicitações externas desabilitadas. A disponibilidade dos documentos depende de cada fonte; o período de emissão filtra o acervo e a exportação, sem reiniciar o cursor de distribuição.

A leitura local de um XML não comprova sua autenticidade nem substitui a verificação fiscal junto à fonte autorizadora.

## Executar a partir do código-fonte

Requisitos: Windows 64 bits e SDK .NET 10 compatível com [global.json](global.json).

Abra `WgsDfeManager.sln` no ambiente de desenvolvimento ou execute:

```powershell
dotnet restore WgsDfeManager.sln --locked-mode
dotnet run --project src/WgsDfe.Desktop/WgsDfe.Desktop.csproj
```

Também é possível executar [INICIAR_WGS.bat](INICIAR_WGS.bat). O iniciador utiliza um SDK disponível ou uma compilação de desenvolvimento existente.

Para desenvolver com dados isolados, configure `Application.DataDirectory` com uma pasta absoluta de teste e utilize outro `Application.OrganizationId`. A configuração padrão pode apontar para a mesma pasta de dados da instalação do programa no usuário atual do Windows. Não execute experimentos sobre um acervo real.

## Compilar e testar

```powershell
dotnet restore WgsDfeManager.sln --locked-mode
dotnet build WgsDfeManager.sln -c Release --no-restore
dotnet test tests/WgsDfe.Tests/WgsDfe.Tests.csproj -c Release --no-build
dotnet run --project tests/WgsDfe.Desktop.Smoke -c Release --no-build -- --v0175-checks
```

O cenário `--v0175-checks` usa dados sintéticos e temporários, sem capturas de tela ou solicitações fiscais reais. A execução padrão do projeto de smoke tests é um roteiro visual legado e gera capturas.

Na preparação da 0.17.5 foram aprovados 905 testes da suíte e 153 verificações de interface. Dois testes exclusivos de Unix foram ignorados no Windows. Esses resultados não substituem a homologação com certificados e fontes reais.

## Gerar o instalador Windows

Publique uma versão autossuficiente, com o runtime .NET incluído:

```powershell
dotnet publish src/WgsDfe.Desktop/WgsDfe.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o publish/win-x64
./scripts/TestarPublicacao.ps1 -PastaPublicada ./publish/win-x64
```

Compile [scripts/WgsDfeManager_Setup.iss](scripts/WgsDfeManager_Setup.iss) com Inno Setup compatível. O pacote aprovado da 0.17.5 foi gerado com Inno Setup 6.4.3:

```powershell
ISCC.exe scripts/WgsDfeManager_Setup.iss
```

A saída fica em `Instalador/`. O instalador e os diretórios de compilação não fazem parte do código versionado. O script preserva o `appsettings.json` existente em atualizações.

## Dados locais e backup

Empresas, metadados, XMLs, certificados e preferências são mantidos na pasta de dados configurada. O cofre de certificados utiliza DPAPI, vinculado ao perfil do Windows.

O comando **Criar backup** salva o banco de dados; XMLs e cofre de certificados devem ser preservados separadamente. Para uma cópia completa, feche o aplicativo e copie toda a pasta de dados, além dos certificados originais guardados em local seguro.

O repositório não deve receber acervos de clientes, certificados, senhas, bancos de dados, backups ou configurações locais. As amostras fiscais e os dados dos testes são sintéticos.

## Organização do projeto

| Diretório | Conteúdo |
| --- | --- |
| `src/WgsDfe.Desktop` | Interface WPF |
| `src/WgsDfe.Domain` | Entidades e validações de domínio |
| `src/WgsDfe.Application` | Contratos e fluxos da aplicação |
| `src/WgsDfe.Infrastructure` | Persistência, serviços, arquivos e certificados |
| `src/WgsDfe.Database` | Contexto EF Core e migrations |
| `src/WgsDfe.Providers` | Integrações com fontes fiscais |
| `src/WgsDfe.Tools` | Ferramentas de linha de comando |
| `tests` | Testes automatizados e verificações de interface |
| `assets` | Modelos de importação e exemplo sintético |
| `docs` | Documentação e referências técnicas |
| `scripts` | Validação, teste de publicação e instalador |

## Documentação técnica

- [Arquitetura](ARCHITECTURE.md)
- [Desenvolvimento](DEVELOPMENT.md)
- [Banco de dados](DATABASE.md)
- [Segurança](SECURITY.md)
- [Fontes e provedores](PROVIDERS.md)
- [Certificados](docs/CERTIFICATES.md)
- [Exportação](docs/EXPORT.md)
- [Recuperação histórica](docs/HISTORICAL.md)
- [Automação](docs/AUTOMATION.md)

Parte da documentação técnica registra fases anteriores e suas validações. Para identificar a versão atual e os comandos de execução, utilize este README.
