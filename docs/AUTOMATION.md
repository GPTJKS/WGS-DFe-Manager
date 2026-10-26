# Automação — fase 12

## Configurar e habilitar

1. Cadastre empresas e associe certificados à fonte. Faça a conferência manual da fonte e do certificado no Windows antes de usar automação.
2. Abra Automação. Selecione empresa, fonte, ambiente, hora HH:mm, fuso, papel/conteúdo quando aplicáveis e limite de lotes.
3. Marque Agendamento ativo e salve. Novo horário cria outro registro: 06:00 e 18:00 são dois agendamentos. Agendamentos novos começam pausados e não disparam ao salvar.
4. Marque a autorização de consultas oficiais automáticas e clique Habilitar nesta sessão. A execução depende do aplicativo aberto. Nenhuma credencial ou autorização é enviada simplesmente ao abrir a janela.
5. Use Atualizar para ver próximas execuções e as últimas 200 tentativas. Datas do histórico são UTC; a próxima execução é exibida no fuso escolhido. Detalhes de lotes e NSU ficam nas telas de consulta da fonte.

Fechar a janela mantém a automação habilitada, como informado na tela. Fechar o aplicativo a encerra. Ao reabrir, habilite novamente a sessão. Não há serviço Windows, agendador do sistema operacional, execução com computador desligado nem instalação automática de tarefa.

## Seleção e fontes

Cada horário pertence a uma empresa/fonte/ambiente. A API admite os fusos America/Sao_Paulo e UTC, 00:00–23:59, de 1 a 100 lotes por execução (padrão 10) e até 1.000 agendamentos. Não são criados horários automaticamente para novas empresas. Um horário duplicado da mesma empresa/fonte/ambiente/fuso é recusado, mesmo com outro papel.

NF-e SC, NFC-e SC e NFCom SC exigem empresa de SC e produção; aceitam papel emitente/destinatário/qualquer e XML ou chaves. NF-e Nacional permite os ambientes existentes, mas somente qualquer participação/XML completo. CT-e/OS permite 57/67, usa XML completo e mantém o cursor comum dos modelos. Empresa inativa ou modelo desabilitado bloqueia a tentativa antes do dispatcher. Certificado/vínculo/autorização permanecem sujeitos à conferência do coordenador e da fonte.

Não há consulta automática por chave pontual, recuperação histórica paga, manifestação, exportação agendada ou backup automático nesta fase. O scheduler é de sincronização das fontes já implementadas, com seus limites de disponibilidade.

## Horários, atraso e cooldown

O timer verifica vencimentos a cada 30 segundos; esse intervalo não é frequência de chamadas fiscais. Um tick processa até 25 horários vencidos, sequencialmente. Operações longas, filas manuais e cooldowns podem adiar uma consulta. Não há garantia de execução no segundo exato.

Salvar/editar calcula o próximo horário estritamente futuro: salvar às 10:00 um horário de 06:00 programa o dia seguinte. Um agendamento que já estava salvo e venceu com o aplicativo fechado é consolidado em uma tentativa ao habilitar a sessão; não são repetidos todos os dias perdidos.

A próxima janela é registrada antes da chamada. Ao terminar, um cooldown posterior desloca para o primeiro horário diário igual ou posterior à liberação da fonte. Exemplo: horário 06:00, liberação 12:00 do dia 20 → próxima janela 06:00 do dia 21. Não há retry agressivo nem polling fiscal entre esses horários. Falha de certificado, rede ou rejeição fica registrada; a próxima tentativa é na janela diária seguinte, podendo exigir correção manual.

Horário de verão: minutos inexistentes avançam ao primeiro minuto válido; uma hora ambígua usa a ocorrência UTC posterior, uma vez no dia. Trocar o relógio ou o fuso do Windows não altera o fuso salvo. Recuo de relógio não antecipa uma próxima janela já persistida. Não se promete catch-up integral quando uma execução atravessa vários horários.

## Pausa, histórico e interrupções

Pausar selecionado desativa um horário, preservando histórico. Pausar e cancelar em andamento revoga a sessão inteira, desabilita rede oficial e solicita cancelamento; aguarda os pontos seguros do coordenador. Recebimentos duráveis, XMLs e cursores confirmados são preservados. Se houver edição esperando uma execução longa, o botão de pausa da sessão continua disponível.

Desligar a rede nas telas de consulta manual, inclusive ao fechá-las, revoga também o consentimento automático. Para retomar, habilite novamente em Automação. Consentimento não é gravado/restaurado com os agendamentos.

O banco registra claim/horário futuro, auditoria e tentativa antes da rede; cada resultado pode apontar para o job fiscal. Se houver crash entre chamada e gravação final, o bootstrap marca a tentativa como interrompida e mantém o inbox/estado fiscal. Confira a fonte manualmente; não há replay imediato daquela janela na reabertura. Isso prioriza evitar chamada repetida, sem garantir uma consulta em cada horário durante falhas.

Falha de banco/infraestrutura do tick pausa a sessão. Um registro ainda Running pode exigir reabertura para a recuperação de bootstrap. A publicação SQL de início não é atômica com uma resposta remota: as garantias duráveis de recebimento continuam pertencendo aos coordenadores fiscais.

Agendamentos sem histórico podem ser excluídos. Com histórico, devem ser pausados; empresa, fonte, ambiente e filtros não podem ser trocados no mesmo ID. Crie outro agendamento para mudar esse escopo. Horário, fuso, limite de lotes e situação podem ser editados com revisão concorrente conferida. O histórico completo fica no SQLite; a tela mostra os últimos 200 registros e não implementa expurgo.

## Homologação

Os testes cobrem SQLite/migration, relógio controlado, concorrência, cancelamento, reinício, isolamento tenant e coordenadores fiscais reais com respostas SOAP, certificados e transporte sintéticos. Não houve chamada autenticada real. Homologação WPF/Windows/A1 e observação de uma sessão prolongada de operação continuam pendentes. Não apresentar esta entrega de código-fonte como instalador ou serviço Windows.
