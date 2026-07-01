using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using TamoJuntoGames.API.Data;
using TamoJuntoGames.API.Models;

namespace TamoJuntoGames.API.Tests.Infrastructure;

public sealed class TamoJuntoGamesApiFactory : WebApplicationFactory<Program>
{
    public const string JwtKey =
        "test-only-signing-key-for-tamo-junto-games-integration-tests";

    public const string JwtIssuer = "TamoJuntoGames.API.Tests";
    public const string JwtAudience = "TamoJuntoGames.API.Tests.Client";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TamoJuntoGamesApiFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

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
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
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
            Email = email,
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
            _connection.Dispose();
    }
}
