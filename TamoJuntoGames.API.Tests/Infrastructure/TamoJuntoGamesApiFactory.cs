using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
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
    private readonly string? _originalJwtKey;
    private readonly string? _originalJwtIssuer;
    private readonly string? _originalJwtAudience;
    private readonly string? _originalJwtExpireMinutes;

    public TamoJuntoGamesApiFactory()
    {
        _originalJwtKey = Environment.GetEnvironmentVariable("Jwt__Key");
        _originalJwtIssuer = Environment.GetEnvironmentVariable("Jwt__Issuer");
        _originalJwtAudience = Environment.GetEnvironmentVariable("Jwt__Audience");
        _originalJwtExpireMinutes = Environment.GetEnvironmentVariable("Jwt__ExpireMinutes");

        Environment.SetEnvironmentVariable("Jwt__Key", JwtKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", JwtAudience);
        Environment.SetEnvironmentVariable("Jwt__ExpireMinutes", "60");

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
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = JwtKey,
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["Jwt:ExpireMinutes"] = "60"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(_connection));

            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = JwtIssuer,
                        ValidAudience = JwtAudience,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(JwtKey)),
                        ClockSkew = TimeSpan.Zero
                    };
                });
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
            Environment.SetEnvironmentVariable("Jwt__Key", _originalJwtKey);
            Environment.SetEnvironmentVariable("Jwt__Issuer", _originalJwtIssuer);
            Environment.SetEnvironmentVariable("Jwt__Audience", _originalJwtAudience);
            Environment.SetEnvironmentVariable("Jwt__ExpireMinutes", _originalJwtExpireMinutes);

            _connection.Dispose();
        }
    }
}
