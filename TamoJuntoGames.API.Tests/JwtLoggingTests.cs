using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TamoJuntoGames.API.DTOs;
using TamoJuntoGames.API.Tests.Infrastructure;
using Xunit;
using static TamoJuntoGames.API.Tests.UsuariosValidationAndErrorsTests;

namespace TamoJuntoGames.API.Tests;

public class JwtLoggingTests
{
    private const string Categoria = "TamoJuntoGames.API.Authentication.JwtBearer";
    private const string Marcador = "conteudo-ficticio-que-nao-deve-ser-registrado";
    private const string EmailFicticio = "privado@example.com";
    private const string TokenInvalido = "token-invalido-ficticio-confidencial";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TokenInvalidoOuExpiradoGeraUmWarningSeguroEPreservaChallengeNativo(bool expirado)
    {
        var logs = new LogsJwt();
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = ComLogs(baseFactory, logs);
        using var client = CriarCliente(factory);
        // Controle somente de testes: os callbacks vazios deixam o Bearer responder nativamente.
        using var referencia = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.Events = new JwtBearerEvents())));
        using var clientReferencia = CriarCliente(referencia);
        var token = expirado ? CriarTokenExpirado() : TokenInvalido;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        clientReferencia.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/api/usuarios/123");
        using var controle = await clientReferencia.GetAsync("/api/usuarios/123");

        var problem = await ProblemAsync(response, HttpStatusCode.Unauthorized);
        var problemControle = await ProblemAsync(controle, HttpStatusCode.Unauthorized);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Equal(Assert.Single(controle.Headers.WwwAuthenticate).ToString(), challenge.ToString());
        Assert.Equal(problemControle.Type, problem.Type);
        Assert.Equal(problemControle.Title, problem.Title);
        Assert.Equal(problemControle.Detail, problem.Detail);
        VerificarRegistro(Assert.Single(logs.Registros), LogLevel.Warning, 1003,
            "JwtAuthenticationFailed", expirado ? "TokenExpirado" : "TokenRejeitado", token);
    }

    [Fact]
    public async Task SemTokenGeraSomenteDebugControladoEPreserva401ComChallenge()
    {
        var logs = new LogsJwt();
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = ComLogs(baseFactory, logs);
        using var client = CriarCliente(factory);

        using var response = await client.GetAsync("/api/usuarios/123");

        await ProblemAsync(response, HttpStatusCode.Unauthorized);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Null(challenge.Parameter);
        VerificarRegistro(Assert.Single(logs.Registros), LogLevel.Debug, 1004, "JwtChallenge");
    }

    [Fact]
    public async Task NiveisDesabilitadosNaoRegistramDiagnosticosNemAlteram401()
    {
        var logs = new LogsJwt();
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = ComLogs(baseFactory, logs, LogLevel.Error);
        using var client = CriarCliente(factory);

        using var semToken = await client.GetAsync("/api/usuarios/123");
        await ProblemAsync(semToken, HttpStatusCode.Unauthorized);
        Assert.Equal("Bearer", Assert.Single(semToken.Headers.WwwAuthenticate).Scheme);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenInvalido);
        using var invalido = await client.GetAsync("/api/usuarios/123");
        await ProblemAsync(invalido, HttpStatusCode.Unauthorized);
        Assert.Equal("Bearer", Assert.Single(invalido.Headers.WwwAuthenticate).Scheme);
        Assert.Empty(logs.Registros);
    }

    [Fact]
    public async Task LoginValidoConsultaPropriaEForbiddenNaoGeramDiagnosticosJwt()
    {
        var logs = new LogsJwt();
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = ComLogs(baseFactory, logs);
        using var client = CriarCliente(factory);
        var cadastro = Cadastro();
        using var criada = await client.PostAsJsonAsync("/api/usuarios", cadastro);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var usuario = await criada.Content.ReadFromJsonAsync<UsuarioRespostaDTO>();
        Assert.NotNull(usuario);
        using var loginResponse = await client.PostAsJsonAsync("/api/usuarios/login",
            new { Email = cadastro["Email"], Senha = cadastro["Senha"] });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginRespostaDTO>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        using var propria = await client.GetAsync($"/api/usuarios/{usuario.Id}");
        Assert.Equal(HttpStatusCode.OK, propria.StatusCode);
        using var outro = await client.GetAsync($"/api/usuarios/{usuario.Id + 1}");
        await ProblemAsync(outro, HttpStatusCode.Forbidden);
        Assert.Empty(outro.Headers.WwwAuthenticate);
        Assert.Empty(logs.Registros);
    }

    [Fact]
    public async Task CallbacksNaoReproduzemExcecaoClaimsHeadersCorpoOuDescricaoDoChallenge()
    {
        var logs = new LogsJwt();
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = ComLogs(baseFactory, logs);
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        using var body = new MemoryStream(Encoding.UTF8.GetBytes(Marcador));
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("email", EmailFicticio), new Claim("apelido", Marcador)]))
        };
        httpContext.Request.Headers.Authorization = $"Bearer {TokenInvalido}";
        httpContext.Request.Body = body;
        var excecao = new SecurityTokenExpiredException($"{Marcador}; {EmailFicticio}; {TokenInvalido}; {TamoJuntoGamesApiFactory.JwtKey}");

        await options.Events.AuthenticationFailed(new AuthenticationFailedContext(httpContext, scheme, options)
        {
            Exception = excecao
        });
        await options.Events.Challenge(new JwtBearerChallengeContext(httpContext, scheme, options, new())
        {
            AuthenticateFailure = excecao, Error = Marcador, ErrorDescription = Marcador
        });
        await options.Events.Challenge(new JwtBearerChallengeContext(httpContext, scheme, options, new())
        {
            Error = Marcador, ErrorDescription = Marcador
        });

        Assert.Equal(2, logs.Registros.Count);
        VerificarRegistro(Assert.Single(logs.Registros, r => r.Evento.Id == 1003),
            LogLevel.Warning, 1003, "JwtAuthenticationFailed", "TokenExpirado");
        VerificarRegistro(Assert.Single(logs.Registros, r => r.Evento.Id == 1004),
            LogLevel.Debug, 1004, "JwtChallenge");
    }

    private static WebApplicationFactory<Program> ComLogs(
        TamoJuntoGamesApiFactory baseFactory, LogsJwt logs, LogLevel minimo = LogLevel.Debug)
        => baseFactory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.AddFilter(Categoria, minimo);
            logging.AddProvider(logs);
        }));

    private static string CriarTokenExpirado()
    {
        var token = new JwtSecurityToken(
            issuer: TamoJuntoGamesApiFactory.JwtIssuer,
            audience: TamoJuntoGamesApiFactory.JwtAudience,
            claims: [new Claim("sub", "123"), new Claim("email", EmailFicticio), new Claim("apelido", Marcador)],
            expires: DateTime.UtcNow.AddMinutes(-5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TamoJuntoGamesApiFactory.JwtKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void VerificarRegistro(Registro registro, LogLevel nivel, int id, string nome,
        string? motivo = null, string? token = null)
    {
        Assert.Equal(nivel, registro.Nivel);
        Assert.Equal(id, registro.Evento.Id);
        Assert.Equal(nome, registro.Evento.Name);
        Assert.Null(registro.Excecao);
        Assert.Equal(motivo is null ? "Autenticação JWT requerida." : "Falha na autenticação JWT. Motivo: {Motivo}.",
            registro.Estado["{OriginalFormat}"]);
        Assert.Equal(motivo is null ? new[] { "{OriginalFormat}" } : new[] { "Motivo", "{OriginalFormat}" },
            registro.Estado.Keys.OrderBy(chave => chave, StringComparer.Ordinal));
        if (motivo is not null) Assert.Equal(motivo, registro.Estado["Motivo"]);
        var texto = registro.Texto + string.Join(";", registro.Estado.Values);
        foreach (var sensivel in new[] { Marcador, EmailFicticio, TokenInvalido, TamoJuntoGamesApiFactory.JwtKey, "Authorization" })
            Assert.DoesNotContain(sensivel, texto, StringComparison.OrdinalIgnoreCase);
        if (token is not null) Assert.DoesNotContain(token, texto);
    }

    private sealed record Registro(LogLevel Nivel, EventId Evento, Dictionary<string, object?> Estado,
        Exception? Excecao, string Texto);

    private sealed class LogsJwt : ILoggerProvider
    {
        public ConcurrentQueue<Registro> Registros { get; } = new();
        public ILogger CreateLogger(string categoryName) => categoryName == Categoria ? new Logger(this) : NullLogger.Instance;
        public void Dispose() { }

        private sealed class Logger(LogsJwt owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var campos = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state);
                owner.Registros.Enqueue(new Registro(logLevel, eventId,
                    campos.ToDictionary(p => p.Key, p => p.Value), exception, formatter(state, exception)));
            }
        }
    }
}
