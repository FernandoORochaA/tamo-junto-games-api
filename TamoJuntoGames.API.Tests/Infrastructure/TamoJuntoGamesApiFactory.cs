using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TamoJuntoGames.API.Data;
using TamoJuntoGames.API.Models;
using TamoJuntoGames.API.Services;

namespace TamoJuntoGames.API.Tests.Infrastructure;

public sealed class TamoJuntoGamesApiFactory : WebApplicationFactory<Program>
{
    public const string JwtKey =
        "test-only-signing-key-for-tamo-junto-games-integration-tests";

    public const string JwtIssuer = "TamoJuntoGames.API.Tests";
    public const string JwtAudience = "TamoJuntoGames.API.Tests.Client";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Dictionary<string, string?> _jwtConfiguration = new()
    {
        ["Jwt:Key"] = JwtKey,
        ["Jwt:Issuer"] = JwtIssuer,
        ["Jwt:Audience"] = JwtAudience,
        ["Jwt:ExpireMinutes"] = "60"
    };

    public TamoJuntoGamesApiFactory(IDictionary<string, string?>? jwtOverrides = null)
    {
        if (jwtOverrides is not null)
        {
            foreach (var entry in jwtOverrides)
                _jwtConfiguration[entry.Key] = entry.Value;
        }

        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
        });

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            // Valores nulos também sobrescrevem fontes anteriores nos testes de ausência.
            configuration.AddInMemoryCollection(_jwtConfiguration);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(_connection));
        });
    }

    public HttpClient CreateHttpsClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Database.EnsureCreated();

        return client;
    }

    public async Task<Usuario> AdicionarUsuarioAsync(
        string email = "fernando@example.com",
        string senha = "SenhaForte123",
        string apelido = "Fernas")
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.EnsureCreatedAsync();

        var usuario = new Usuario
        {
            NomeCompleto = "Fernando Rocha",
            Apelido = apelido,
            Email = EmailNormalizer.ParaApresentacao(email),
            EmailNormalizado = EmailNormalizer.ParaIdentidade(email),
            DataNascimento = new DateTime(2000, 1, 1),
            Genero = "Não informado",
            Senha = BCrypt.Net.BCrypt.HashPassword(senha)
        };

        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();

        return usuario;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
