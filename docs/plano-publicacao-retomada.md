# Plano de publicação e retomada do Tamo Junto Games

## 1. Objetivo e contexto

Este plano registra o estado técnico da API, as decisões pendentes e os requisitos para avaliar uma publicação futura. O projeto será colocado em stand-by após o encerramento do ciclo técnico atual; as informações deverão ser reavaliadas na retomada.

**A API não está homologada para produção.** Concluir a Etapa 12 ou esta documentação não autoriza publicar serviços, modificar infraestrutura, aplicar migrations ou reparar dados. Essas ações exigem aprovação específica.

Referência histórica do código consultado: commit `bace152c42d77b8f67feb4044c759fd0e44f5b1a` — atualização do README. Este documento é posterior a essa referência; o hash não pressupõe qual será o HEAD em uma retomada futura.

## 2. Estado atual verificado

- API de usuários e autenticação em **.NET 9 / ASP.NET Core 9**, com controllers, DTOs e serviços injetados.
- **EF Core 9.0.0 e SQLite**, com três migrations versionadas e índice UNIQUE em `EmailNormalizado`.
- Cadastro e login públicos; consulta, atualização de nome/apelido/e-mail e exclusão restritas ao próprio usuário autenticado. Não há listagem geral.
- Senhas com **BCrypt.Net-Next 4.0.3**; emissão JWT **HS256** por `ITokenService`/`TokenService`. Configuração tipada e validada na inicialização; validade configurada atualmente em 60 minutos.
- Rate limiting nativo somente no login: dez requisições por minuto por IP, sliding window de seis segmentos, sem fila e com estado por instância.
- Problem Details e tratamento sanitizado de erros inesperados; logs estruturados nos callbacks JWT. Isso não comprova sanitização de todos os logs internos do framework.
- Ferramenta independente de diagnóstico de identidades legadas, sem reparos e sem dependência de configuração JWT.
- **Última suíte completa registrada: 166 testes aprovados, 0 falhas e 0 ignorados.** Esse número descreve a execução anterior, não uma garantia permanente nem uma nova homologação.

User Secrets está configurado para desenvolvimento. Os arquivos versionados não contêm a chave JWT; emissor, audiência, duração e conexão possuem valores locais padrão. HTTPS está configurado para desenvolvimento. Não há configuração explícita de publicação, proxy confiável ou CORS, nem pipeline CI/CD versionado.

Os testes utilizam configurações e dados sintéticos. Não comprovam, isoladamente, capacidade, TLS, durabilidade, recuperação ou funcionamento na hospedagem futura. Para comandos e contratos da API, consulte o [README](../README.md).

## 3. Decisões pendentes na retomada

Antes de escolher ferramentas ou implementar mudanças, registrar:

- **Hospedagem:** sistema operacional, runtime, restrições do provedor, armazenamento e forma de implantação.
- **Domínio e exposição:** endereço público, acesso restrito ou aberto e terminação TLS.
- **Frontend:** existência de integração, origens permitidas e necessidade de CORS.
- **Instâncias e carga:** quantidade inicial, requisições e escritas esperadas, concorrência e margem de capacidade.
- **Persistência:** manter SQLite ou adotar outra solução mediante necessidade demonstrada; localização e permissões dos dados.
- **Backup e restauração:** retenção, destino protegido, perda máxima aceitável de dados e tempo aceitável de recuperação.
- **Segredos:** provisionamento, controle de acesso, separação entre ambientes e resposta a comprometimento de chave.
- **Contas e sessões:** validade dos tokens, necessidade de invalidação imediata e tratamento de comprometimento ou recuperação de contas.
- **Monitoramento:** sinais mínimos de saúde, erros, saturação, disponibilidade e alertas.
- **Operação:** responsável por publicação, migrations, certificados, incidentes, restauração e aprovações.

Nenhuma opção está escolhida por este plano. Docker, PostgreSQL, Kubernetes e múltiplas instâncias **não são obrigatórios por definição**.

## 4. Checklist contextual de publicação

As caixas abaixo representam verificações futuras, não entregas concluídas. Para cada item, registrar responsável, evidência e resultado: atendido, não aplicável com justificativa ou pendente. Um item P1/P2 pode tornar-se P0 se for requisito do cenário escolhido.

### P0 — Obrigatório antes de publicar, quando aplicável

- [ ] Utilizar framework/runtime com suporte vigente e revisar dependências diretas, transitivas e nativas, seus patches e avisos de segurança.
- [ ] Definir configuração de **Production**, separada do desenvolvimento, com emissor, audiência, duração JWT e conexão corretos.
- [ ] Provisionar chave JWT forte, exclusiva do ambiente, com armazenamento/acesso protegidos e responsável por resposta a comprometimento. A validação de tamanho não comprova aleatoriedade. Não usar User Secrets como solução de produção.
- [ ] Validar TLS, domínio, certificados e renovação; certificados de desenvolvimento não são a configuração de publicação.
- [ ] Se houver proxy, configurar confiança restrita e validar esquema original e IP antes do redirecionamento HTTPS e do rate limiting. Testar cabeçalhos forjados; não confiar irrestritamente em `X-Forwarded-For`.
- [ ] Se houver frontend em outra origem, definir CORS restrito às origens necessárias. CORS não substitui autenticação ou autorização.
- [ ] Garantir persistência durável, destino explícito do banco, permissões mínimas e preservação após reinício/redeploy.
- [ ] Produzir backups consistentes e protegidos e demonstrar restauração em ambiente separado.
- [ ] Revisar e testar migrations, identidade responsável pela execução e plano de recuperação, evitando aplicação concorrente descoordenada.
- [ ] Se dados existentes forem reutilizados, avaliar histórico e identidades legadas em cópia autorizada; resolver impedimentos antes da migração/publicação correspondente.
- [ ] Verificar a biblioteca SQLite efetivamente distribuída e atender à condição sobre WAL descrita na seção 6, caso esse cenário seja utilizado.
- [ ] Validar privacidade, permissões, destino e retenção dos logs, incluindo categorias do framework e da hospedagem.
- [ ] Aprovar testes e homologação no ambiente escolhido, com responsáveis, monitoramento mínimo e plano de retorno definidos.

O **.NET 9 encerra suporte em 10/11/2026**. Uma publicação após essa data exige atualização planejada para versão suportada e nova validação; antes dela, também é necessário avaliar patches. Consulte a [política oficial](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) na retomada.

### P1 — Recomendado conforme cenário

- [ ] Avaliar proteção adicional do cadastro contra abuso e limites de trabalho/concorrência, pois cadastro também calcula BCrypt.
- [ ] Executar testes de capacidade adequados à exposição, incluindo login, cadastro, escritas e saturação do SQLite.
- [ ] Definir política para senhas acima de 72 bytes, preservando compatibilidade com hashes e credenciais existentes.
- [ ] Definir política de revogação de sessões e validade dos tokens; promover a P0 se invalidação imediata for necessária.
- [ ] Ampliar observabilidade, alertas e health checks conforme as necessidades da operação e da hospedagem.
- [ ] Automatizar testes e implantação quando frequência, equipe ou risco justificarem. Uma publicação manual pode ser aceita se for reproduzível e recuperável.
- [ ] Definir estratégia operacional de rotação de chaves, incluindo coordenação entre instâncias quando existirem.

### P2 — Evolução opcional

- [ ] Refresh tokens por conveniência de sessão, se necessários; sua existência não resolve revogação automaticamente.
- [ ] MFA conforme risco e requisitos das contas.
- [ ] Rotação de chaves sem interrupção, quando houver exigência de continuidade.
- [ ] Infraestrutura distribuída, coordenação global de limites e novos componentes somente mediante necessidade demonstrada.
- [ ] Outras melhorias justificadas por requisitos reais, sem antecipar arquitetura para uma escala inexistente.

## 5. Riscos técnicos conhecidos

Fatos observados no código são distintos de incidentes. O defeito histórico de normalização foi reproduzido com dados sintéticos; não há comprovação de contas reais afetadas. Os demais impactos abaixo dependem das condições indicadas, não representam exploração ou falha em produção demonstrada.

| Risco | Evidência no código ou investigação | Condição de ocorrência | Impacto possível | Mitigação futura | Prioridade contextual |
|---|---|---|---|---|---|
| JWT sem refresh/revogação | [TokenService](../TamoJuntoGames.API/Services/TokenService.cs) e [validação JWT](../TamoJuntoGames.API/Program.cs), sem estado de sessão | Token comprometido antes da expiração; política exige encerramento imediato | Autenticação pode permanecer válida até expirar; exclusão não revoga assinatura | Definir validade e resposta a comprometimento; adicionar revogação se exigida. Refresh é decisão separada | P1; P0 se invalidação imediata for requisito |
| Rotação não coordenada | Uma chave configurada, sem transição multichave implementada | Troca de chave ou instâncias com configurações diferentes | Tokens rejeitados e necessidade de novo login | Procedimento de troca e emergência; coordenar instâncias. Reautenticação pode ser aceitável | Estratégia P1; continuidade sem interrupção P2, conforme requisito |
| Limite prático do BCrypt | [Teste de compatibilidade](../TamoJuntoGames.API.Tests/UsuariosValidationAndErrorsTests.cs) registra sufixos após 72 bytes que não distinguem senhas | Senhas longas, inclusive Unicode | Semântica inesperada; novo limite no login pode bloquear contas | Investigar política e migração compatível antes de mudar cadastro/login | P1; P0 se a política exigir processamento integral |
| Cadastro fora do rate limiting | [CriarAsync](../TamoJuntoGames.API/Services/UsuarioService.cs) calcula hash; [controller](../TamoJuntoGames.API/Controllers/UsuariosController.cs) limita somente login | Cadastro automatizado ou tentativas distribuídas | Consumo de CPU e armazenamento | Medir capacidade e proteger aplicativo/borda proporcionalmente | P1; P0 se proteção/capacidade forem insuficientes para a exposição |
| Cota local e IP compartilhado/incorreto | [LoginRateLimiting](../TamoJuntoGames.API/RateLimiting/LoginRateLimiting.cs) usa `RemoteIpAddress`, memória local e partição estável sem IP | NAT/proxy, reinício ou múltiplas instâncias | Bloqueios compartilhados, perda da cota ou limite global diferente do esperado | Validar proxy/IP e decidir política por instância ou coordenada | IP P0 atrás de proxy; coordenação P1 conforme escala |
| SQLite e caminho relativo | [Conexão padrão](../TamoJuntoGames.API/appsettings.json): `Data Source=tamojuntogames.db` | Diretório diferente/efêmero, armazenamento inadequado ou muitas escritas | Banco errado, perda de dados, contenção ou indisponibilidade | Caminho durável explícito, permissões, backup e capacidade testados; avaliar banco servidor se necessário | Durabilidade/destino P0; troca de banco condicional |
| Migrations e recuperação | [InitialCreate](../TamoJuntoGames.API/Migrations/20251215194706_InitialCreate.cs) remove tabela no `Down`; [perfil opcional](../TamoJuntoGames.API/Migrations/20260701000000_ProfileFieldsOptional.cs) substitui nulos no downgrade | Migração incompatível, falha ou retorno de versão | Perda/transformação de dados ou esquema incompatível com o código | Revisar, testar em cópia, executar controladamente e preparar recuperação. `Down` não equivale a restauração | P0 |
| Identidades legadas divergentes | [Backfill SQLite](../TamoJuntoGames.API/Migrations/20260818172938_EmailNormalizadoUniqueIndex.cs) versus [normalizador C#](../TamoJuntoGames.API/Services/EmailNormalizer.cs); [testes históricos](../TamoJuntoGames.API.Tests/HistoricalEmailMigrationTests.cs) | Registros afetados ou colisões anteriores ao índice | Falha de localização, duplicidade lógica ou impedimento da migration | Diagnóstico de cópia e resolução manual aprovada, preservando contas e dados | P0 se os dados afetados forem reutilizados |
| Logs nativos além dos callbacks | [Program.cs](../TamoJuntoGames.API/Program.cs) sanitiza callbacks; [testes JWT](../TamoJuntoGames.API.Tests/JwtLoggingTests.cs) capturam a categoria personalizada | Categorias/sinks do framework habilitados, erros ou abuso | Detalhes internos, retenção indevida ou volume excessivo | Avaliar todas as categorias e destinos, filtros, conteúdo, acesso e retenção | Privacidade/acesso P0; refinamentos P1 |
| Versões antigas ou sem suporte | `net9.0` e versões fixadas no [projeto](../TamoJuntoGames.API/TamoJuntoGames.API.csproj) | Retomada após fim de suporte ou dependência com aviso aplicável | Ausência de correções ou incompatibilidades | Inventariar e atualizar versões aprovadas, com testes e homologação | P0 para suporte e problemas aplicáveis |
| SQLite nativo e WAL-reset | Biblioteca local identificada como 3.46.1 na investigação; ver seção 6 | WAL e concorrência nas condições oficiais | Corrupção rara possível, não observada no banco real | Conferir artefato final e utilizar biblioteca corrigida nesse cenário | P0 antes de publicar com o cenário afetado |

## 6. Atenção ao SQLite nativo

A investigação identificou **SQLite 3.46.1 na biblioteca nativa do artefato local** consultando sua função de versão, sem abrir banco. Isso não determina a versão de um artefato que será compilado/distribuído futuramente nem o modo de operação de um banco.

Identificação do artefato examinado:

- Biblioteca examinada: `e_sqlite3.dll`.
- Arquitetura do artefato: `win-x64`.
- Função consultada: `sqlite3_libversion`.
- Versão retornada: `3.46.1`.
- Dependência resolvida nos assets locais: `SQLitePCLRaw.lib.e_sqlite3/2.1.10`.

Segundo o [registro oficial de WAL-reset](https://www.sqlite.org/wal.html#walresetbug), o problema raro envolve WAL e duas ou mais conexões, em threads ou processos, com escrita/checkpoint concorrentes em condições específicas. Uma única instância da API não exclui essa condição se houver várias conexões. A versão identificada está na faixa descrita como afetada; a correção consta em 3.51.3 e em backports específicos.

**Não foi identificada corrupção no banco real: ele não foi aberto ou analisado, e seu modo de operação não foi investigado.** O achado não comprova um incidente. Antes de uma publicação que use esse cenário, verificar o binário nativo efetivamente distribuído e utilizar uma versão corrigida, validada com o provider e os testes. Este plano não atualiza pacotes nem autoriza essa alteração.

## 7. Persistência e dados legados

O backfill histórico usou `upper(trim(Email))` do SQLite; a aplicação usa `Trim().ToUpperInvariant()`. A diferença foi reproduzida com dados sintéticos, inclusive com Unicode e whitespace. **Não sabemos se há contas reais afetadas.**

A [documentação do diagnóstico](diagnostico-identidade-email.md) é a referência para executar a [ferramenta](../tools/EmailIdentityDiagnostic) em **cópia autorizada, consistente e offline**, no formato aceito por ela. A ferramenta não repara registros e recusa WAL/journals; abrir uma conexão somente leitura, isoladamente, não oferece garantia absoluta contra todo efeito físico de journals ou do sistema operacional.

Antes de qualquer operação futura, definir backup consistente e demonstrar restauração. Não copiar apenas o arquivo principal de um banco ativo sem avaliar WAL; não apagar arquivos laterais para forçar o diagnóstico. A obtenção/preparação da cópia deve seguir procedimento aprovado e preservar o original.

Colisões exigem análise e decisão manual sobre identidade/propriedade. Não escolher conta vencedora, fundir, excluir ou mudar e-mails automaticamente. **Nenhum reparo automático está autorizado.** Não editar migrations históricas já aplicadas; qualquer correção futura exige desenho separado, preservação de IDs/dados e testes. Uma migration posterior não resolve automaticamente um banco bloqueado ao aplicar a migration histórica.

A ausência de migrations automáticas na API é compatível com uma publicação controlada. Definir quem as executa, quando, com quais permissões e como confirmar código/esquema compatíveis. Planejar separadamente retorno do aplicativo e recuperação do banco: um downgrade pode perder informação, e restaurar backup pode descartar gravações posteriores. Registrar a janela de recuperação e a estratégia escolhida.

## 8. Sequência prática de retomada

1. **Definir o cenário de hospedagem/publicação** e responder às decisões da seção 3, incluindo dados que serão preservados.
2. **Revisar suporte e dependências**, inclusive ferramentas, runtime e biblioteca SQLite nativa do artefato final. Aprovar atualizações antes de executá-las.
3. **Reexecutar os testes existentes**, registrando commit, ambiente e resultados; repetir após cada mudança aprovada. Não assumir que o registro histórico de 166 testes continua suficiente.
4. **Validar persistência e dados legados**, começando por backup/restauração e cópias autorizadas. Confirmar destino e migrations; resolver impedimentos mediante aprovação específica.
5. **Definir segurança, infraestrutura e operação:** segredos, sessões, TLS/proxy, CORS se aplicável, abuso, logs, monitoramento e responsáveis.
6. **Implementar apenas os requisitos aplicáveis**, em mudanças pequenas, com critérios de aceitação, testes e revisão. Não antecipar infraestrutura distribuída.
7. **Homologar em ambiente separado**, exercitando cadastro/login, autorização, erros, rate limiting, TLS/proxy/IP, persistência após reinício/redeploy e restauração. Acrescentar carga e falhas conforme o risco do cenário.
8. **Publicar somente mediante aprovação explícita**, com artefato identificado, evidências, responsável disponível, monitoramento e plano de recuperação testado.

## 9. Critérios para autorizar uma publicação futura

Nenhum critério abaixo está declarado cumprido por esta documentação. A decisão exige evidências registradas para o ambiente e o artefato finais:

- [ ] Cenário, domínio, exposição, instâncias, carga e responsabilidades definidos e documentados.
- [ ] Framework e dependências suportados; avisos aplicáveis avaliados e condições de SQLite/WAL atendidas.
- [ ] Configuração Production validada, segredos protegidos e resposta a comprometimento definida.
- [ ] TLS/certificados validados; proxy/IP e CORS comprovados quando aplicáveis.
- [ ] Persistência durável demonstrada e restauração de backup executada com sucesso em ambiente separado.
- [ ] Migrations avaliadas/testadas, responsável por execução definido e recuperação de código/dados planejada.
- [ ] Riscos aplicáveis tratados ou formalmente aceitos pelo responsável, com justificativa e limites explícitos; nenhum P0 aplicável deixado pendente.
- [ ] Suíte atual e homologação aprovadas, incluindo os comportamentos de segurança e operação do cenário.
- [ ] Monitoramento, alertas mínimos, acesso aos logs e responsáveis por incidentes definidos.
- [ ] Autorização explícita de publicação registrada para o artefato e ambiente identificados.

## 10. Referências e atualização deste plano

- [README](../README.md): preparação local, execução, contratos e testes; não substitui homologação de produção.
- [Roadmap Técnico](../Roadmap%20T%C3%A9cnico%20do%20Tamo%20Junto%20Games.docx): histórico e planejamento. Na referência consultada, ainda registra 127 testes e a Etapa 11 pendente, com escopo original de paginação. O código já contém a projeção aprovada da consulta; não reintroduzir listagem geral por causa desse texto. A consolidação do Roadmap é uma tarefa separada.
- [Diagnóstico de identidades de e-mail](diagnostico-identidade-email.md): operação controlada, privacidade e limitações.
- [Política de suporte .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core): conferir novamente na retomada.
- [User Secrets em desenvolvimento](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-9.0): limites do mecanismo, não uma escolha de cofre para produção.
- [Proxies e load balancers no ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-9.0): confiança e forwarded headers; selecionar a versão correspondente ao framework futuro.
- [Aplicação de migrations EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying): avaliar procedimento adequado ao provider e à versão, sem executar exemplos indiscriminadamente.
- [Backup SQLite](https://www.sqlite.org/backup.html) e [WAL-reset](https://www.sqlite.org/wal.html#walresetbug): consistência e condição do risco nativo.
- [Logging nativo JWT Bearer 9.0.0](https://raw.githubusercontent.com/dotnet/aspnetcore/v9.0.0/src/Security/Authentication/JwtBearer/src/LoggingExtensions.cs): caminhos que recebem exceções, além dos callbacks da aplicação. Reavaliar após atualização do framework.

Este é um plano de retomada, não um histórico duplicado das etapas nem um procedimento de implantação já homologado. Na retomada, registrar a nova referência de código, revisar evidências e prioridades e obter aprovação antes de alterar produção ou dados.
