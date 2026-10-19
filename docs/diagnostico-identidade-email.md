# Diagnóstico de identidades de e-mail legadas

## Finalidade e escopo

O backfill de `20260818172938_EmailNormalizadoUniqueIndex` usou `upper(trim(Email))`
do SQLite. Essa regra não equivale a `Trim().ToUpperInvariant()` para determinados
caracteres Unicode e whitespaces. Os testes históricos registram o comportamento
existente; não afirmam que essa migration tenha sido corrigida.

O console em `tools/EmailIdentityDiagnostic` usa o **mesmo `EmailNormalizer` da API**.
Não inicia o host, não lê `appsettings`, User Secrets ou configurações JWT e não
depende de chave/token. Não possui endpoint HTTP nem integração ao cadastro/login.
Não usa EF, migrations ou `EnsureCreated` durante o diagnóstico.

**O banco real do desenvolvedor não foi analisado nesta etapa.** Os resultados dos
testes são exclusivamente sintéticos e não demonstram contas reais afetadas.

## Preparação e execução explícita

Use apenas uma **cópia autorizada, consistente, offline e independente**. Guarde o
original e seus backups. Não copie somente o `.db` de uma API ativa em WAL: dados
recentes podem estar em arquivos laterais. A obtenção de um snapshot consistente
deve seguir um procedimento SQLite aprovado (backup apropriado/ambiente parado).
Esta ferramenta não produz backups nem faz checkpoint ou conversão de journal.

São aceitos arquivos SQLite em modo rollback-journal, sem `-wal`, `-shm` ou
`-journal`. WAL é recusado mesmo sem arquivos laterais. Caso o backup esteja em
WAL, prepare uma cópia standalone por procedimento separado, autorizado e validado;
**não apague journals nem altere bytes do cabeçalho para contornar essa proteção**.

Na raiz da solução, com SDK .NET 9:

```powershell
dotnet run --project tools/EmailIdentityDiagnostic/EmailIdentityDiagnostic.csproj -- --database-copy "C:\BackupsAutorizados\copia-consistente.db"
$LASTEXITCODE
```

O caminho deve ser absoluto. A ferramenta não procura automaticamente o banco da
API. O exemplo não deve ser substituído pelo banco ativo do desenvolvedor.
Em execuções futuras, prefira também permissões de sistema de arquivos somente
leitura sobre a cópia e seu diretório. A compilação pode gerar `bin/obj`, mas não
modifica o arquivo de dados.

## Garantias e limitações da leitura

- `FileMode.Open`/`FileAccess.Read`: banco inexistente falha, sem ser criado.
- SQLite `Mode=ReadOnly`, pooling desabilitado: não permite DML/DDL nem mantém a
  conexão no pool após a execução. A única instrução do diagnóstico é um `SELECT`.
- Verifica formato, modo de journal e arquivos laterais antes de abrir SQLite.
  Não tenta recuperação automática; erros são apresentados com mensagem fixa.
- Mantém um handle com `FileShare.Read` durante a leitura, que impede gravações
  convencionais concorrentes em Windows. **Não é uma garantia universal contra
  processos externos, sistemas de arquivos distintos, links ou corridas de troca
  de arquivos.** Exige cópia offline; não é ferramenta para um banco em uso.
- Não usa `immutable=1`, que poderia ignorar um WAL necessário e gerar resultado
  incorreto. `Mode=ReadOnly` isoladamente não garante ausência de efeitos físicos
  em toda combinação de SQLite/journals; daí a recusa de WAL/journals e a exigência
  de cópia offline. Políticas absolutas de não escrita exigem proteção pelo SO.
- Um único SELECT lê somente `Id`, `Email`, `EmailNormalizado`. Não lê hashes de
  senha, nomes ou dados de perfil. E-mails são processados apenas em memória.
- O agrupamento usa comparação ordinal e ocupa memória proporcional ao conjunto
  de identidades/IDs. É um utilitário pontual, não uma infraestrutura para escala.
- Não valida credenciais, propriedade do e-mail, integridade geral do banco ou
  todas as constraints. Esquema sem as três colunas necessárias é incompatível.

## Resultados e privacidade

Saída JSON local, sem chamadas externas, e-mails, hashes, tokens ou caminhos:

- `RegistrosExaminados`: total lido.
- `QuantidadeDivergencias` e `IdsDivergentes`: identidade persistida diferente da
  identidade atual calculada a partir de `Email`.
- `QuantidadeGruposColisao` e `GruposColisao`: duas ou mais contas que convergem
  para a mesma identidade atual, mesmo que suas identidades persistidas difiram.
- `NecessitaAnaliseManual`: verdadeiro se houver qualquer divergência ou colisão.

IDs técnicos também são informação interna: restrinja acesso ao relatório e evite
publicá-lo. A ferramenta não oferece opção para exibir e-mails completos.

Códigos de saída: **0** sem achados; **2** achados exigem análise manual;
**1** diagnóstico não realizado (uso, acesso, formato, journal ou esquema).
Código 1 nunca significa banco saudável. Código 0 descreve apenas o snapshot
examinado, não garante ausência de outros problemas ou de alterações posteriores.

Uma divergência individual não é o mesmo que colisão. Uma colisão exige resolução
manual de identidade/propriedade; nenhuma conta é escolhida como vencedora.
O console **não repara, funde, exclui ou modifica registros**. Antes de qualquer
reparo futuro: backup consistente, diagnóstico, aprovação da estratégia e testes.
Uma migration posterior também não resolve automaticamente um banco bloqueado na
migration histórica por colisões ASCII. Esse cenário exige avaliação separada.

## Testes sintéticos

```powershell
dotnet test TamoJuntoGames.API.sln --filter FullyQualifiedName~HistoricalEmailMigrationTests
dotnet test TamoJuntoGames.API.sln --filter FullyQualifiedName~EmailIdentityDiagnosticTests
dotnet test TamoJuntoGames.API.sln
```

Os históricos usam SQLite em memória e as migrations reais, nunca `EnsureCreated`.
Os testes do diagnóstico preparam arquivos temporários sintéticos, verificam
registros/esquema/hash do arquivo e removem suas pastas ao terminar. Gravações de
preparação ocorrem somente nos testes, não no leitor/console. Nenhum teste abre o
banco local da API ou requer User Secrets.
