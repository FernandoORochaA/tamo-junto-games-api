# 🎮 Tamo Junto Games — API

Projeto de estudos de desenvolvimento de software com evolução de longo prazo. A visão do **Tamo Junto Games** é uma plataforma gamer para organizar jogos, registrar progresso e avaliações e interagir com outros jogadores.

**Este repositório implementa atualmente apenas a API de usuários e autenticação.** Jogos, listas, avaliações e recursos sociais ainda não estão implementados. A integração com o frontend não está validada neste módulo.

## Funcionalidades implementadas

- Cadastro público e login com verificação de hash de senha BCrypt.
- Emissão e validação de JWT, com configuração tipada e validada na inicialização.
- Consulta dos próprios dados, atualização de nome, apelido e e-mail e exclusão da própria conta.
- Validações por DTO, normalização centralizada de e-mail e índice UNIQUE de identidade no banco.
- Rate limiting exclusivo do login e logging estruturado nos callbacks JWT.
- Respostas de erro com Problem Details e testes automatizados com dados sintéticos.
- Ferramenta independente de diagnóstico de identidades de e-mail legadas, sem reparo automático.

## Tecnologias

- C#, .NET 9 e ASP.NET Core Web API 9, com controllers, DTOs e injeção de dependência.
- Entity Framework Core **9.0.0** e SQLite, com provider `Microsoft.EntityFrameworkCore.Sqlite` **9.0.0**.
- JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer` **9.0.0**) e BCrypt.Net-Next **4.0.3**.
- xUnit **2.9.2** e `Microsoft.AspNetCore.Mvc.Testing` **9.0.0** para testes.

As versões de pacotes estão nos [projetos da API](TamoJuntoGames.API/TamoJuntoGames.API.csproj) e de [testes](TamoJuntoGames.API.Tests/TamoJuntoGames.API.Tests.csproj).

## Configuração local

### Pré-requisitos

- SDK .NET 9, incluindo o runtime ASP.NET Core 9, e acesso ao NuGet para restauração.
- Terminal e editor de sua preferência; Visual Studio é opcional.
- Certificado HTTPS de desenvolvimento confiável.
- Ferramenta `dotnet-ef` compatível com EF Core 9 para preparar o banco por migrations.

Não é necessário instalar um servidor SQL Server ou PostgreSQL. Os comandos abaixo partem da **raiz do repositório**, salvo indicação contrária.

### Chave JWT via User Secrets

O projeto da API já possui `UserSecretsId`; não execute uma nova inicialização de User Secrets. Registre `Jwt:Key` localmente:

```shell
dotnet user-secrets set "Jwt:Key" "<SUA_CHAVE>" --project TamoJuntoGames.API/TamoJuntoGames.API.csproj
```

`<SUA_CHAVE>` é apenas um marcador, não uma chave válida. Substitua-o por uma chave forte, gerada aleatoriamente no seu ambiente, com **pelo menos 32 bytes UTF-8**. Não use uma senha pessoal nem publique a chave em arquivos, mensagens ou no Git.

User Secrets é carregado em **Development**, mantém os valores fora do repositório e não os criptografa. Não é um mecanismo de gestão de segredos para produção.

Os arquivos `appsettings` versionados contêm `Jwt:Issuer`, `Jwt:Audience` e `Jwt:ExpireMinutes` (atualmente **60**), mas não `Jwt:Key`. A API utiliza `JwtOptions`, `IOptions<JwtOptions>` e `ValidateOnStart()`: chave ausente/curta, emissor ou audiência vazios e duração fora de 1–1440 minutos impedem a inicialização.

### HTTPS de desenvolvimento

```shell
dotnet dev-certs https --trust
```

Confirme a solicitação de confiança quando suportada pelo sistema operacional; em outros ambientes, siga o procedimento local de confiança do certificado. Prefira o perfil `https`, pois a API possui redirecionamento HTTPS.

### SQLite: banco novo e banco existente

A conexão padrão é `Data Source=tamojuntogames.db`. O caminho é **relativo ao diretório de trabalho do processo**, não um caminho absoluto fixo. Os comandos de preparação abaixo entram no diretório da API para manter o destino explícito. Não versione o banco local nem seus arquivos auxiliares.

**Somente para um ambiente novo, sem banco existente no destino**, configure a chave JWT e prepare o esquema com as migrations versionadas. Se `dotnet-ef` ainda não estiver instalado, uma versão alinhada aos pacotes atuais é:

```shell
dotnet tool install --global dotnet-ef --version 9.0.0
```

Se já estiver instalado, confira sua compatibilidade com EF Core 9. Para o banco novo:

```shell
cd TamoJuntoGames.API
dotnet ef database update -- --environment Development
cd ..
```

A API **não aplica migrations automaticamente ao iniciar**. As três migrations existentes criam a tabela de usuários, tornam campos de perfil opcionais e adicionam `EmailNormalizado` com índice UNIQUE.

**Banco existente ou legado:** não aplique esses comandos indiscriminadamente. Confirme o arquivo de destino, obtenha backup consistente e avalie o histórico antes de qualquer operação. O backfill histórico de e-mails tem diferenças de normalização que podem exigir análise manual; veja [Diagnóstico de e-mails legados](#diagnóstico-de-e-mails-legados). Não apague o banco ou journals para contornar falhas.

## Compilar e executar

Após configurar o ambiente e preparar um banco novo ou validar o banco existente, execute na raiz:

```shell
dotnet restore TamoJuntoGames.API.sln
dotnet build TamoJuntoGames.API.sln --no-restore
dotnet run --project TamoJuntoGames.API/TamoJuntoGames.API.csproj --launch-profile https
```

O perfil `https` define o ambiente **Development**, com HTTPS em `https://localhost:7246` e HTTP em `http://localhost:5197`. Não há abertura automática do navegador nem interface Swagger publicada.

## Endpoints

| Método | Rota | Acesso | Sucesso |
|---|---|---|---|
| POST | `/api/usuarios` | Público: cadastro | `201 Created` |
| POST | `/api/usuarios/login` | Público, sujeito ao rate limiting | `200 OK` |
| GET | `/api/usuarios/{id}` | Bearer JWT, somente próprio usuário | `200 OK` |
| PUT | `/api/usuarios/{id}` | Bearer JWT, somente próprio usuário | `200 OK` |
| DELETE | `/api/usuarios/{id}` | Bearer JWT, somente próprio usuário | `204 No Content` |

O `{id}` deve ser inteiro. Nas rotas protegidas, envie o JWT obtido no login no cabeçalho `Authorization`, usando o esquema `Bearer`. O ID solicitado deve corresponder à identidade autenticada (`sub`/`NameIdentifier`); um token válido não permite operar sobre outra conta. Não há listagem geral ou endpoint administrativo de usuários.

- **Cadastro:** `nomeCompleto`, `apelido`, `email`, `confirmarEmail`, `senha` e `confirmarSenha`.
- **Login:** `email` e `senha`; retorna `usuario`, `token` e `expiraEm`.
- **Atualização:** `nomeCompleto`, `apelido` e `email`; não altera senha nem campos adicionais de perfil.
- **Resposta de usuário:** `id`, `nomeCompleto`, `apelido` e `email`, sem senha/hash.

## Validações e segurança

- Nome, apelido e e-mail são obrigatórios no cadastro e na atualização, com máximos de **100, 50 e 254 unidades UTF-16**, respectivamente. E-mail também possui validação de formato.
- No cadastro, a senha tem mínimo de oito unidades UTF-16 e as confirmações devem coincidir exatamente com os valores informados, antes da normalização. O login não recebeu esses novos limites de comprimento e não há máximo de senha definido nos DTOs.
- O e-mail de apresentação usa `Trim()`; sua identidade usa `Trim().ToUpperInvariant()`. Cadastro, login e atualização consultam `EmailNormalizado`, protegido por índice UNIQUE. Isso não comprova a propriedade do endereço de e-mail.
- Senhas são armazenadas como hash BCrypt. O comportamento atual mantém a limitação prática de **72 bytes** do BCrypt; não há uma política adicional de tratamento de senhas longas.
- `ITokenService`/`TokenService` emitem JWT assinado com **HS256**, chave em UTF-8 e claims `sub` (ID), `email` e `apelido`. A validação verifica assinatura, emissor, audiência e expiração, sem tolerância adicional de relógio.
- O login permite **dez requisições por minuto por IP**, em sliding window com seis segmentos e sem fila. Requisições válidas e inválidas consomem a cota. A identificação usa `RemoteIpAddress`, normaliza IPv4 mapeado em IPv6 e não confia diretamente em `X-Forwarded-For`.
- O limitador mantém estado **em memória por instância**; pessoas sob o mesmo NAT/proxy podem compartilhar a cota. `Retry-After` é enviado apenas quando há metadado disponível.

Principais respostas de erro: `400` para entradas inválidas, `401` para credenciais ou autenticação inválidas/ausentes, `403` para operação não autorizada sobre uma identidade, `404` para recurso não encontrado, `405` para método indisponível, `409` para conflito de e-mail, `429` para limite excedido e `500` sanitizado para falha inesperada.

Problem Details é o padrão de erros, com detalhes de validação quando aplicável. A negociação de conteúdo pode resultar em corpo vazio para um `Accept` incompatível; não se deve presumir JSON em toda resposta. O sucesso `204` não possui corpo.

## Testes

Na raiz do repositório:

```shell
dotnet test TamoJuntoGames.API.sln
```

A **última execução completa apresentou 166 testes aprovados, 0 falhas e 0 ignorados**. É o registro dessa execução, não uma garantia permanente para alterações futuras.

Os testes usam configurações próprias, dados sintéticos e SQLite em memória ou em arquivos temporários. Não exigem a chave JWT de desenvolvimento nem o banco real. Cobrem autenticação, autorização, validações, persistência, Problem Details, rate limiting, projeção de consultas, logging JWT e diagnóstico/migrations históricas.

Chamadas manuais podem ser feitas com Postman, Insomnia, Thunder Client ou com o [arquivo HTTP existente](TamoJuntoGames.API/Testes.http). Esse arquivo contém cenários com IDs e dados de exemplo específicos: adapte-os a contas sintéticas em banco autorizado, especialmente antes de atualizar ou excluir registros. Não cole JWTs reais nos arquivos versionados.

## Diagnóstico de e-mails legados

A migration histórica de `EmailNormalizado` utilizou `upper(trim(Email))` do SQLite, que não equivale à normalização C# para determinados caracteres Unicode e whitespaces. Os testes históricos documentam essa diferença; **não significam que o backfill foi corrigido**.

A ferramenta [tools/EmailIdentityDiagnostic](tools/EmailIdentityDiagnostic) identifica divergências e colisões de identidade, retornando contagens e IDs técnicos. É exclusivamente de diagnóstico: não repara, funde ou exclui registros, não inicia a API e não precisa de JWT/User Secrets.

Use apenas uma **cópia autorizada, consistente e offline**, respeitando a recusa de WAL/journals. O diagnóstico não foi executado no banco real do desenvolvedor; não se sabe se há registros reais afetados. Achados exigem avaliação manual e backup antes de qualquer reparo futuro.

Consulte as instruções, limitações e códigos de saída em [Diagnóstico de identidades de e-mail legadas](docs/diagnostico-identidade-email.md).

## Limitações e publicação futura

**A API ainda não está homologada para produção.** Uma publicação exigirá decisões e validação de infraestrutura, gestão de segredos, TLS/certificados, proxy confiável, CORS quando aplicável, backups/restauração, migrations controladas e observabilidade. A adequação do SQLite depende da carga e da operação; ele não é inadequado para produção por definição.

Ainda não existem troca/recuperação de senha, confirmação de propriedade de e-mail, MFA, refresh tokens, revogação de JWT ou rotação coordenada de chaves. A exclusão de conta não revoga criptograficamente tokens já emitidos. Data de nascimento e gênero são opcionais no modelo, mas não há edição desses campos nos endpoints atuais.

O **.NET 9 encerra suporte em 10/11/2026**, exigindo atualização planejada, conforme a [política oficial de suporte](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core). O planejamento detalhado permanece no [Roadmap Técnico](Roadmap%20T%C3%A9cnico%20do%20Tamo%20Junto%20Games.docx).

## Repositórios do projeto

- [Frontend](https://github.com/FernandoORochaA/tamo-junto-games-web) — repositório relacionado, sem afirmação de integração validada com esta API.
- [Backend / API](https://github.com/FernandoORochaA/tamo-junto-games-api) — este repositório.

## Autor

**Fernando Rocha** — [GitHub](https://github.com/FernandoORochaA)

