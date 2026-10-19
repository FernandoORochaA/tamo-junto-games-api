using System.Data.Common;
using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TamoJuntoGames.API.Data;
using TamoJuntoGames.API.DTOs;
using TamoJuntoGames.API.Exceptions;
using TamoJuntoGames.API.Models;
using TamoJuntoGames.API.Services;
using TamoJuntoGames.API.Tests.Infrastructure;
using Xunit;
using static TamoJuntoGames.API.Tests.UsuariosValidationAndErrorsTests;

namespace TamoJuntoGames.API.Tests;

public class UsuarioPersistenceErrorsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UniqueDeEmailAposConsultaPreviaRetorna409(bool atualizar)
    {
        var interceptor = new FalhaNaPersistenciaInterceptor(inserirConcorrente: true);
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = CriarCliente(factory);
        var id = 0;
        if (atualizar)
        {
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var usuario = Usuario("proprio@example.com", "Proprio");
            context.Usuarios.Add(usuario);
            await context.SaveChangesAsync();
            id = usuario.Id;
            Autenticar(factory, client, id);
        }

        interceptor.Ativo = true;
        var response = await EnviarAsync(client, Cadastro(), atualizar, id);

        var problem = await ProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal(EmailDuplicadoException.MensagemPublica, problem.Detail);
        var falha = Assert.IsType<DbUpdateException>(interceptor.FalhaObservada);
        var sqlite = Assert.IsType<SqliteException>(falha.InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Equal(2067, sqlite.SqliteExtendedErrorCode);
        using var verificacao = factory.Services.CreateScope();
        var banco = verificacao.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await banco.Usuarios.CountAsync(u => u.EmailNormalizado == "TESTE@EXAMPLE.COM"));
        if (atualizar)
            Assert.Equal("proprio@example.com", (await banco.Usuarios.AsNoTracking().SingleAsync(u => u.Id == id)).Email);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutroUniqueSemConflitoDeEmailPermanece500InclusiveComEmailProprio(bool atualizar)
    {
        var interceptor = new FalhaNaPersistenciaInterceptor();
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = CriarCliente(factory);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proprio = Usuario("teste@example.com", "Proprio");
        context.Usuarios.Add(Usuario("outro@example.com", "Jogador"));
        if (atualizar) context.Usuarios.Add(proprio);
        await context.SaveChangesAsync();
        // Restrição extra existe SOMENTE neste banco em memória, não no modelo da API.
        await context.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IX_Teste_Apelido ON Usuarios (Apelido)");
        if (atualizar) Autenticar(factory, client, proprio.Id);

        var response = await EnviarAsync(client, Cadastro(), atualizar, proprio.Id);

        await ProblemAsync(response, HttpStatusCode.InternalServerError);
        var falha = Assert.IsType<DbUpdateException>(interceptor.FalhaObservada);
        Assert.Equal(2067, Assert.IsType<SqliteException>(falha.InnerException).SqliteExtendedErrorCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Apelido", body);
        Assert.DoesNotContain("SQLite", body);
        Assert.DoesNotContain("IX_Teste", body);
        if (atualizar)
        {
            using var verificacao = factory.Services.CreateScope();
            var persistido = await verificacao.ServiceProvider.GetRequiredService<AppDbContext>().Usuarios.AsNoTracking().SingleAsync(u => u.Id == proprio.Id);
            Assert.Equal("Proprio", persistido.Apelido);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotNullNaoSeTransformaEmConflitoDeEmail(bool atualizar)
    {
        var interceptor = new FalhaNaPersistenciaInterceptor(violarNotNull: true);
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = CriarCliente(factory);
        var id = 0;
        if (atualizar)
        {
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var usuario = Usuario("proprio@example.com", "Proprio");
            context.Usuarios.Add(usuario);
            await context.SaveChangesAsync();
            id = usuario.Id;
            Autenticar(factory, client, id);
        }
        interceptor.Ativo = true;
        await ProblemAsync(await EnviarAsync(client, Cadastro(), atualizar, id), HttpStatusCode.InternalServerError);
        var falha = Assert.IsType<DbUpdateException>(interceptor.FalhaObservada);
        Assert.Equal(1299, Assert.IsType<SqliteException>(falha.InnerException).SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task FalhaUniqueNaoConfirmadaPreservaInstanciaDaExcecaoOriginal()
    {
        var original = new DbUpdateException("mensagem-ficticia-nao-classificavel", new SqliteException("ficticio", 19, 2067));
        var interceptor = new LancarExcecaoInterceptor(original);
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = CriarCliente(factory);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUsuarioService>();
        var excecao = await Assert.ThrowsAsync<DbUpdateException>(() => service.CriarAsync(new CriarUsuarioDTO
        {
            NomeCompleto = "Teste", Apelido = "Teste", Email = "teste@example.com", Senha = "SenhaFicticia123!"
        }));
        Assert.Same(original, excecao);
    }

    [Fact]
    public async Task FalhaNaConfirmacaoPreservaExcecaoOriginalRegistraMetadadosSegurosERetorna500()
    {
        const string detalheSensivelFicticio = "senha-token-email-ficticios-nao-devem-ser-registrados";
        var secundaria = new SqliteException(detalheSensivelFicticio, 5);
        var consulta = new FalhaNaConsultaInterceptor(secundaria);
        var persistencia = new FalhaNaPersistenciaInterceptor(
            inserirConcorrente: true, aoFalhar: _ => consulta.Ativo = true);
        var logger = new LoggerRegistro();
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(persistencia, consulta));
            services.AddSingleton<ILogger<UsuarioService>>(logger);
        }));
        using var client = CriarCliente(factory);

        persistencia.Ativo = true;
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IUsuarioService>();
            var original = await Assert.ThrowsAsync<DbUpdateException>(() => service.CriarAsync(new CriarUsuarioDTO
            {
                NomeCompleto = "Pessoa Fictícia", Apelido = "Teste",
                Email = "teste@example.com", Senha = "SenhaFicticia123!"
            }));
            Assert.Same(persistencia.FalhaObservada, original);
            var causa = Assert.IsType<SqliteException>(original.InnerException);
            Assert.Equal(19, causa.SqliteErrorCode);
            Assert.Equal(2067, causa.SqliteExtendedErrorCode);
            Assert.NotSame(secundaria, causa);
            Assert.Contains(nameof(DbContext.SaveChangesAsync), original.StackTrace);
        }

        var registro = Assert.Single(logger.Registros);
        Assert.Equal(LogLevel.Warning, registro.Nivel);
        Assert.Equal(1001, registro.Evento.Id);
        Assert.Equal(typeof(SqliteException).FullName, registro.Estado["TipoExcecao"]);
        Assert.Null(registro.Excecao);
        Assert.DoesNotContain(detalheSensivelFicticio, registro.Texto);
        Assert.DoesNotContain(detalheSensivelFicticio, string.Join(";", registro.Estado.Values));

        // Repete o mesmo intercalamento por HTTP, com outro e-mail para passar pela consulta prévia.
        persistencia.Ativo = true;
        var dto = Cadastro();
        dto["Email"] = dto["ConfirmarEmail"] = "http@example.com";
        var response = await EnviarAsync(client, dto, atualizar: false, id: 0);
        await ProblemAsync(response, HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(detalheSensivelFicticio, body);
        Assert.DoesNotContain(nameof(SqliteException), body);
        Assert.DoesNotContain("UNIQUE", body);
        Assert.Equal(2, logger.Registros.Count);
    }

    private static Usuario Usuario(string email, string apelido) => new()
    {
        NomeCompleto = "Pessoa Fictícia", Apelido = apelido,
        Email = email, EmailNormalizado = EmailNormalizer.ParaIdentidade(email), Senha = "hash-ficticio-nao-utilizado-no-login"
    };

    private sealed class FalhaNaPersistenciaInterceptor(
        bool inserirConcorrente = false, bool violarNotNull = false, Action<Exception>? aoFalhar = null) : SaveChangesInterceptor
    {
        public bool Ativo { get; set; }
        public Exception? FalhaObservada { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Ativo) return result;
            Ativo = false;
            var context = Assert.IsType<AppDbContext>(eventData.Context);
            var entrada = Assert.Single(context.ChangeTracker.Entries<Usuario>(),
                e => e.State is EntityState.Added or EntityState.Modified);
            if (violarNotNull) entrada.Property(u => u.NomeCompleto).CurrentValue = null!;
            if (inserirConcorrente)
            {
                // Determinístico: outra gravação após AnyAsync e antes do INSERT/UPDATE real.
                // ADO.NET direto evita recursão do interceptor e não altera o estado rastreado.
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = "INSERT INTO Usuarios (NomeCompleto, Apelido, Email, EmailNormalizado, Senha) VALUES ('Concorrente', 'Concorrente', $email, $normalizado, 'hash-ficticio')";
                Parametro(command, "$email", entrada.Entity.Email);
                Parametro(command, "$normalizado", entrada.Entity.EmailNormalizado);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            FalhaObservada = eventData.Exception;
            aoFalhar?.Invoke(eventData.Exception);
            return Task.CompletedTask;
        }

        private static void Parametro(DbCommand command, string nome, string valor)
        {
            var parametro = command.CreateParameter();
            parametro.ParameterName = nome;
            parametro.Value = valor;
            command.Parameters.Add(parametro);
        }
    }

    private sealed class FalhaNaConsultaInterceptor(Exception excecao) : DbCommandInterceptor
    {
        public bool Ativo { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!Ativo) return ValueTask.FromResult(result);
            Ativo = false;
            throw excecao;
        }
    }

    private sealed class LoggerRegistro : ILogger<UsuarioService>
    {
        public List<(LogLevel Nivel, EventId Evento, Dictionary<string, object?> Estado,
            Exception? Excecao, string Texto)> Registros { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var propriedades = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state);
            Registros.Add((logLevel, eventId, propriedades.ToDictionary(p => p.Key, p => p.Value),
                exception, formatter(state, exception)));
        }
    }

    private sealed class LancarExcecaoInterceptor(Exception excecao) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw excecao;
    }
}
