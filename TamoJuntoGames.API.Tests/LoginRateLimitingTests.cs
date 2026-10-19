using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TamoJuntoGames.API.Data;
using TamoJuntoGames.API.DTOs;
using TamoJuntoGames.API.RateLimiting;
using TamoJuntoGames.API.Services;
using TamoJuntoGames.API.Tests.Infrastructure;
using Xunit;
using static TamoJuntoGames.API.Tests.UsuariosValidationAndErrorsTests;

namespace TamoJuntoGames.API.Tests;

public class LoginRateLimitingTests
{
    private const string Rota = "/api/usuarios/login";
    private const string Email = "login.ficticio@example.com";
    private const string Senha = "SenhaFicticia123!";

    [Fact]
    public void ConfiguracaoCentralUsaJanelaDeslizanteAprovadaSemFila()
    {
        var options = LoginRateLimiting.CriarOpcoes();
        Assert.Equal(10, options.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), options.Window);
        Assert.Equal(6, options.SegmentsPerWindow);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Window / options.SegmentsPerWindow);
        Assert.Equal(0, options.QueueLimit);
        Assert.True(options.AutoReplenishment);
    }

    [Fact]
    public async Task DezLoginsValidosSaoProcessadosEDecimoPrimeiroNaoExecutaServicoNemEmissor()
    {
        var chamadas = new Contadores();
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddScoped<UsuarioService>();
            services.RemoveAll<IUsuarioService>();
            services.AddScoped<IUsuarioService>(sp => new UsuarioServiceContador(
                sp.GetRequiredService<UsuarioService>(), chamadas));
            services.AddScoped<TokenService>();
            services.RemoveAll<ITokenService>();
            services.AddScoped<ITokenService>(sp => new EmissorContador(
                sp.GetRequiredService<TokenService>(), chamadas));
        }));
        using var client = Cliente(factory, "198.51.100.10");
        await CadastrarAsync(client);

        for (var i = 0; i < 10; i++)
        {
            using var response = await client.PostAsJsonAsync(Rota, new { Email, Senha });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var login = await response.Content.ReadFromJsonAsync<LoginRespostaDTO>();
            Assert.NotNull(login);
            Assert.False(string.IsNullOrWhiteSpace(login.Token));
        }
        Assert.Equal(10, chamadas.Logins);
        Assert.Equal(10, chamadas.Tokens);

        using var rejeitada = await client.PostAsJsonAsync(Rota, new { Email, Senha });
        var problem = await ProblemAsync(rejeitada, HttpStatusCode.TooManyRequests);
        Assert.Equal("Muitas tentativas de login. Tente novamente mais tarde.", problem.Detail);
        Assert.Empty(rejeitada.Headers.WwwAuthenticate);
        Assert.Equal(10, chamadas.Logins);
        Assert.Equal(10, chamadas.Tokens);
        VerificarRetryAfterSePresente(rejeitada);
    }

    [Theory]
    [InlineData("valido", HttpStatusCode.OK)]
    [InlineData("credenciais-invalidas", HttpStatusCode.Unauthorized)]
    [InlineData("dto-invalido", HttpStatusCode.BadRequest)]
    [InlineData("json-invalido", HttpStatusCode.BadRequest)]
    [InlineData("corpo-ausente", HttpStatusCode.BadRequest)]
    public async Task TodosOsTiposDeTentativaConsomemCotaAntesDaValidacao(string tipo, HttpStatusCode esperado)
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = Cliente(factory, "198.51.100.11");
        await CadastrarAsync(client);
        await ConsumirAsync(client, 9);

        using var decima = tipo switch
        {
            "valido" => await client.PostAsJsonAsync(Rota, new { Email, Senha }),
            "credenciais-invalidas" => await client.PostAsJsonAsync(Rota, new { Email, Senha = "SenhaIncorreta123!" }),
            "dto-invalido" => await client.PostAsJsonAsync(Rota, new { Email = "invalido", Senha = "" }),
            "json-invalido" => await client.PostAsync(Rota, new StringContent("{", System.Text.Encoding.UTF8, "application/json")),
            _ => await client.PostAsync(Rota, new StringContent("", System.Text.Encoding.UTF8, "application/json"))
        };
        Assert.Equal(esperado, decima.StatusCode);

        // Até mesmo um JSON inválido passa a receber 429 depois da saturação.
        using var rejeitada = await client.PostAsync(Rota,
            new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
        await ProblemAsync(rejeitada, HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task IpsDiferentesPossuemCotasIndependentes()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var primeiro = Cliente(factory, "198.51.100.12");
        using var segundo = Cliente(factory, "198.51.100.13");
        await ConsumirAsync(primeiro, 10);
        using var rejeitada = await TentativaAsync(primeiro);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejeitada.StatusCode);
        await ConsumirAsync(segundo, 10);
        using var segundaRejeitada = await TentativaAsync(segundo);
        Assert.Equal(HttpStatusCode.TooManyRequests, segundaRejeitada.StatusCode);
    }

    [Fact]
    public async Task Ipv4EIpv4MapeadoEmIpv6CompartilhamCota()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var ipv4 = Cliente(factory, "198.51.100.14");
        using var mapeado = Cliente(factory, "::ffff:198.51.100.14");
        await ConsumirAsync(ipv4, 10);
        using var response = await TentativaAsync(mapeado);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task ClientesSemIpCompartilhamParticaoEstavel()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var primeiro = Cliente(factory, null);
        using var segundo = Cliente(factory, null);
        await ConsumirAsync(primeiro, 10);
        using var response = await TentativaAsync(segundo);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task CabecalhosDeIpNaoTrocamParticao()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = Cliente(factory, "198.51.100.15");
        for (var i = 1; i <= 11; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Rota)
            {
                Content = JsonContent.Create(new { Email = "inexistente@example.com", Senha })
            };
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{i}");
            request.Headers.Add("X-Real-IP", $"203.0.113.{i}");
            request.Headers.Add("Forwarded", $"for=203.0.113.{i}");
            using var response = await client.SendAsync(request);
            Assert.Equal(i <= 10 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("/API/USUARIOS/LOGIN")]
    [InlineData("/api/usuarios/login/")]
    [InlineData("/api/usuarios/login?origem=ficticia")]
    public async Task VariacoesAceitasPeloRoteamentoContinuamProtegidas(string rota)
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = Cliente(factory, "198.51.100.16");
        await ConsumirAsync(client, 10);
        using var response = await client.PostAsJsonAsync(rota, new { Email, Senha });
        await ProblemAsync(response, HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task CotaEsgotadaNaoLimitaCadastroCrudNemAlteraAutenticacaoAutorizacaoECabecalhos()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = Cliente(factory, "198.51.100.17");
        var proprio = await factory.AdicionarUsuarioAsync(email: Email, senha: Senha);
        var outro = await factory.AdicionarUsuarioAsync(email: "outro@example.com");
        await ConsumirAsync(client, 10);

        using var cadastro = await client.PostAsJsonAsync("/api/usuarios", Cadastro());
        Assert.Equal(HttpStatusCode.Created, cadastro.StatusCode);
        Assert.NotNull(cadastro.Headers.Location);
        await ProblemAsync(await client.GetAsync($"/api/usuarios/{proprio.Id}"), HttpStatusCode.Unauthorized);
        using var semToken = await client.GetAsync($"/api/usuarios/{proprio.Id}");
        Assert.Contains(semToken.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");

        Autenticar(factory, client, proprio.Id);
        using var consulta = await client.GetAsync($"/api/usuarios/{proprio.Id}");
        Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);
        await ProblemAsync(await client.GetAsync($"/api/usuarios/{outro.Id}"), HttpStatusCode.Forbidden);
        await ProblemAsync(await client.PutAsJsonAsync($"/api/usuarios/{outro.Id}", Cadastro()), HttpStatusCode.Forbidden);
        await ProblemAsync(await client.DeleteAsync($"/api/usuarios/{outro.Id}"), HttpStatusCode.Forbidden);

        using var atualizacao = await client.PutAsJsonAsync($"/api/usuarios/{proprio.Id}",
            new AtualizarUsuarioDTO { NomeCompleto = "Pessoa Atualizada", Apelido = "Atualizado", Email = Email });
        Assert.Equal(HttpStatusCode.OK, atualizacao.StatusCode);
        var atualizado = await atualizacao.Content.ReadFromJsonAsync<UsuarioRespostaDTO>();
        Assert.NotNull(atualizado);
        Assert.Equal(proprio.Id, atualizado.Id);
        using var listagem = await client.GetAsync("/api/usuarios");
        await ProblemAsync(listagem, HttpStatusCode.MethodNotAllowed);
        Assert.Contains("POST", listagem.Content.Headers.Allow);
        using var exclusao = await client.DeleteAsync($"/api/usuarios/{proprio.Id}");
        Assert.Equal(HttpStatusCode.NoContent, exclusao.StatusCode);
        Assert.Empty(await exclusao.Content.ReadAsStringAsync());
        using var continuaLimitado = await TentativaAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, continuaLimitado.StatusCode);
    }

    [Fact]
    public async Task HostsIndependentesNaoCompartilhamEstado()
    {
        using var primeiraFactory = new TamoJuntoGamesApiFactory();
        using var segundaFactory = new TamoJuntoGamesApiFactory();
        using var primeiro = Cliente(primeiraFactory, "198.51.100.18");
        using var segundo = Cliente(segundaFactory, "198.51.100.18");
        await ConsumirAsync(primeiro, 10);
        using var rejeitada = await TentativaAsync(primeiro);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejeitada.StatusCode);
        using var permitida = await TentativaAsync(segundo);
        Assert.Equal(HttpStatusCode.Unauthorized, permitida.StatusCode);
    }

    [Fact]
    public async Task DezRequisicoesConcorrentesSaoAdmitidasEDecimaPrimeiraRejeitadaSemFila()
    {
        var service = new LoginControlado();
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUsuarioService>();
            services.AddSingleton<IUsuarioService>(service);
        }));
        using var client = Cliente(factory, "198.51.100.19");
        var pendentes = Enumerable.Range(0, 10).Select(_ => TentativaAsync(client)).ToArray();
        try
        {
            await service.DezEntradas.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using var rejeitada = await TentativaAsync(client).WaitAsync(TimeSpan.FromSeconds(15));
            await ProblemAsync(rejeitada, HttpStatusCode.TooManyRequests);
            Assert.Equal(10, service.Chamadas);
            Assert.All(pendentes, task => Assert.False(task.IsCompleted));
        }
        finally
        {
            service.Liberar.TrySetResult();
            foreach (var response in await Task.WhenAll(pendentes).WaitAsync(TimeSpan.FromSeconds(15)))
            {
                using (response) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1200, "2")]
    [InlineData(0, "0")]
    [InlineData(-1000, null)]
    public async Task RetryAfterDependeDoMetadadoEArredondaParaCima(int? esperaMs, string? esperado)
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var options = factory.Services.GetRequiredService<IOptions<RateLimiterOptions>>().Value;
        var context = new DefaultHttpContext { RequestServices = factory.Services };
        await using var body = new MemoryStream();
        context.Response.Body = body;
        context.Request.Headers.Accept = "application/json";
        using var lease = new LeaseEspera(esperaMs.HasValue ? TimeSpan.FromMilliseconds(esperaMs.Value) : null);

        await options.OnRejected!(new OnRejectedContext { HttpContext = context, Lease = lease }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        if (esperado is null) Assert.False(context.Response.Headers.ContainsKey("Retry-After"));
        else Assert.Equal(esperado, context.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task CorpoELogDeRejeicaoNaoExpoemDadosSensiveis()
    {
        var logs = new LogsRejeicao();
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = Cliente(factory, "198.51.100.20");
        await ConsumirAsync(client, 10);
        using var request = new HttpRequestMessage(HttpMethod.Post, Rota)
        {
            Content = JsonContent.Create(new { Email, Senha, Hash = "hash-ficticio-confidencial" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token-ficticio-confidencial");
        request.Headers.Add("X-Forwarded-For", "203.0.113.20");
        using var response = await client.SendAsync(request);
        await ProblemAsync(response, HttpStatusCode.TooManyRequests);
        Assert.Empty(response.Headers.WwwAuthenticate);

        var registro = Assert.Single(logs.Registros);
        Assert.Equal(LogLevel.Warning, registro.Nivel);
        Assert.Equal(1002, registro.Evento.Id);
        Assert.Equal("LoginRateLimitRejected", registro.Evento.Name);
        Assert.Null(registro.Excecao);
        Assert.Equal(LoginRateLimiting.PolicyName, registro.Estado["Politica"]);
        var texto = registro.Texto + string.Join(";", registro.Estado.Values);
        var corpo = await response.Content.ReadAsStringAsync();
        foreach (var sensivel in new[] { Email, Senha, "hash-ficticio-confidencial", "token-ficticio-confidencial",
            "198.51.100.20", "203.0.113.20", TamoJuntoGamesApiFactory.JwtKey })
        {
            Assert.DoesNotContain(sensivel, texto);
            Assert.DoesNotContain(sensivel, corpo);
        }
    }

    [Fact]
    public async Task AcceptIncompativelPreserva429ENegociacaoNativaDoMvc()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = Cliente(factory, "198.51.100.21");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        // O MVC já usa seu fallback nativo para JSON quando não há formatter XML.
        using var dentroDaCota = await TentativaAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized, dentroDaCota.StatusCode);
        await ConsumirAsync(client, 9);
        using var response = await TentativaAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(dentroDaCota.Content.Headers.ContentType?.MediaType, response.Content.Headers.ContentType?.MediaType);
        await ProblemAsync(response, HttpStatusCode.TooManyRequests);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.DoesNotContain(Senha, await response.Content.ReadAsStringAsync());
    }

    private static HttpClient Cliente(WebApplicationFactory<Program> factory, string? ip)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        // Configura a conexão no TestServer, não um cabeçalho controlado pelo cliente.
        return new HttpClient(factory.Server.CreateHandler(context =>
            context.Connection.RemoteIpAddress = ip is null ? null : IPAddress.Parse(ip)))
        {
            BaseAddress = new Uri("https://localhost")
        };
    }

    private static async Task CadastrarAsync(HttpClient client)
    {
        var dto = Cadastro();
        dto["Email"] = dto["ConfirmarEmail"] = Email;
        dto["Senha"] = dto["ConfirmarSenha"] = Senha;
        using var response = await client.PostAsJsonAsync("/api/usuarios", dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static Task<HttpResponseMessage> TentativaAsync(HttpClient client) =>
        client.PostAsJsonAsync(Rota, new { Email = "inexistente@example.com", Senha });

    private static async Task ConsumirAsync(HttpClient client, int quantidade)
    {
        for (var i = 0; i < quantidade; i++)
        {
            using var response = await TentativaAsync(client);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    private static void VerificarRetryAfterSePresente(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values)) return;
        Assert.True(long.TryParse(Assert.Single(values), NumberStyles.None, CultureInfo.InvariantCulture, out var segundos));
        Assert.InRange(segundos, 0, 60);
    }

    private sealed class Contadores
    {
        public int Logins;
        public int Tokens;
    }

    private sealed class UsuarioServiceContador(IUsuarioService inner, Contadores chamadas) : IUsuarioService
    {
        public Task<LoginRespostaDTO?> LoginAsync(LoginUsuarioDTO dto)
        {
            Interlocked.Increment(ref chamadas.Logins);
            return inner.LoginAsync(dto);
        }
        public Task<UsuarioRespostaDTO> CriarAsync(CriarUsuarioDTO dto) => inner.CriarAsync(dto);
        public Task<UsuarioRespostaDTO?> AtualizarAsync(int id, AtualizarUsuarioDTO dto) => inner.AtualizarAsync(id, dto);
        public Task<UsuarioRespostaDTO?> ObterPorIdAsync(int id) => inner.ObterPorIdAsync(id);
        public Task<bool> DeletarAsync(int id) => inner.DeletarAsync(id);
    }

    private sealed class EmissorContador(ITokenService inner, Contadores chamadas) : ITokenService
    {
        public (string Token, DateTime ExpiraEm) GerarToken(int usuarioId, string email, string apelido)
        {
            Interlocked.Increment(ref chamadas.Tokens);
            return inner.GerarToken(usuarioId, email, apelido);
        }
    }

    private sealed class LoginControlado : IUsuarioService
    {
        public TaskCompletionSource DezEntradas { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _chamadas;
        public int Chamadas => Volatile.Read(ref _chamadas);

        public async Task<LoginRespostaDTO?> LoginAsync(LoginUsuarioDTO dto)
        {
            if (Interlocked.Increment(ref _chamadas) == 10) DezEntradas.TrySetResult();
            await Liberar.Task;
            return null;
        }
        public Task<UsuarioRespostaDTO> CriarAsync(CriarUsuarioDTO dto) => throw new NotSupportedException();
        public Task<UsuarioRespostaDTO?> AtualizarAsync(int id, AtualizarUsuarioDTO dto) => throw new NotSupportedException();
        public Task<UsuarioRespostaDTO?> ObterPorIdAsync(int id) => throw new NotSupportedException();
        public Task<bool> DeletarAsync(int id) => throw new NotSupportedException();
    }

    private sealed class LeaseEspera(TimeSpan? espera) : RateLimitLease
    {
        public override bool IsAcquired => false;
        public override IEnumerable<string> MetadataNames => espera.HasValue ? [MetadataName.RetryAfter.Name] : [];
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = metadataName == MetadataName.RetryAfter.Name && espera.HasValue ? espera.Value : null;
            return metadata is not null;
        }
    }

    private sealed class LogsRejeicao : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Nivel, EventId Evento, Dictionary<string, object?> Estado,
            Exception? Excecao, string Texto)> Registros { get; } = new();

        public ILogger CreateLogger(string categoryName) => categoryName == typeof(LoginRateLimiting).FullName
            ? new Logger(this) : NullLogger.Instance;
        public void Dispose() { }

        private sealed class Logger(LogsRejeicao owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var campos = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state);
                owner.Registros.Enqueue((logLevel, eventId, campos.ToDictionary(p => p.Key, p => p.Value),
                    exception, formatter(state, exception)));
            }
        }
    }
}
