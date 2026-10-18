using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TamoJuntoGames.API.DTOs;
using TamoJuntoGames.API.Options;
using TamoJuntoGames.API.Tests.Infrastructure;
using Xunit;

namespace TamoJuntoGames.API.Tests;

public class JwtConfigurationTests
{
    public static IEnumerable<object?[]> ConfiguracoesInvalidas()
    {
        yield return ["Jwt:Key", null];
        yield return ["Jwt:Key", ""];
        yield return ["Jwt:Key", new string(' ', 32)];
        yield return ["Jwt:Key", new string('x', 31)];
        yield return ["Jwt:Key", new string('é', 15)];

        foreach (var property in new[] { "Jwt:Issuer", "Jwt:Audience" })
        {
            yield return [property, null];
            yield return [property, ""];
            yield return [property, "   "];
        }

        foreach (var duration in new string?[]
                 { null, "", "nao-numerico", "1.5", "0", "-1", "1441", "2147483648" })
        {
            yield return ["Jwt:ExpireMinutes", duration];
        }
    }

    [Theory]
    [MemberData(nameof(ConfiguracoesInvalidas))]
    public void ConfiguracaoInvalidaImpedeInicializacaoAntesDeQualquerRequisicao(
        string property, string? value)
    {
        using var factory = new TamoJuntoGamesApiFactory(new Dictionary<string, string?>
        {
            [property] = value
        });

        // Services inicia o host; nenhum cliente ou requisição HTTP é criado.
        var exception = Assert.ThrowsAny<Exception>(() => { _ = factory.Services; });
        var diagnostic = exception.ToString();

        Assert.Contains(property, diagnostic);
        Assert.DoesNotContain(TamoJuntoGamesApiFactory.JwtKey, diagnostic);
        if (property == "Jwt:Key" && !string.IsNullOrWhiteSpace(value))
            Assert.DoesNotContain(value, diagnostic);

        // Conversões inválidas falham no binder; as demais regras falham na validação.
        Assert.True(HasConfigurationFailure(exception));
    }

    [Theory]
    [InlineData("12345678901234567890123456789012", "1")]
    [InlineData("éééééééééééééééé", "60")]
    [InlineData(TamoJuntoGamesApiFactory.JwtKey, "1440")]
    public void ConfiguracaoValidaPermiteIniciarHost(string key, string duration)
    {
        using var factory = new TamoJuntoGamesApiFactory(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = key,
            ["Jwt:ExpireMinutes"] = duration
        });

        var services = factory.Services;
        Assert.True(services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStarted.IsCancellationRequested);
    }

    [Theory]
    [InlineData("12345678901234567890123456789012", 1)]
    [InlineData("éééééééééééééééé", 17)]
    [InlineData(TamoJuntoGamesApiFactory.JwtKey, 1440)]
    public async Task LoginPreservaAssinaturaClaimsEDuracaoDaConfiguracao(
        string key, int duration)
    {
        const string issuer = "Issuer.Controlado.Etapa7";
        const string audience = "Audience.Controlada.Etapa7";
        using var factory = new TamoJuntoGamesApiFactory(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = key,
            ["Jwt:Issuer"] = issuer,
            ["Jwt:Audience"] = audience,
            ["Jwt:ExpireMinutes"] = duration.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        var usuario = await factory.AdicionarUsuarioAsync();
        using var client = factory.CreateHttpsClient();
        var beforeLogin = DateTime.UtcNow;

        var response = await client.PostAsJsonAsync("/api/usuarios/login", new LoginUsuarioDTO
        {
            Email = usuario.Email,
            Senha = "SenhaForte123"
        });
        var afterLogin = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginRespostaDTO>();
        Assert.NotNull(login);
        Assert.InRange(login.ExpiraEm,
            beforeLogin.AddMinutes(duration), afterLogin.AddMinutes(duration));

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        handler.ValidateToken(login.Token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.Zero
        }, out var validatedToken);

        var jwt = Assert.IsType<JwtSecurityToken>(validatedToken);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg);
        Assert.Contains(jwt.Claims, claim => claim.Type == JwtRegisteredClaimNames.Sub &&
            claim.Value == usuario.Id.ToString());
        Assert.Contains(jwt.Claims, claim => claim.Type == JwtRegisteredClaimNames.Email &&
            claim.Value == usuario.Email);
        Assert.Contains(jwt.Claims, claim => claim.Type == "apelido" && claim.Value == usuario.Apelido);
        Assert.InRange((login.ExpiraEm - jwt.ValidTo).TotalSeconds, 0, 1);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var consulta = await client.GetAsync($"/api/usuarios/{usuario.Id}");
        Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);
    }

    [Theory]
    [InlineData("assinatura")]
    [InlineData("emissor")]
    [InlineData("publico")]
    public async Task AutenticacaoRealRejeitaTokenComConfiguracaoDiferente(string invalidField)
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync();
        using var client = factory.CreateHttpsClient();
        var key = invalidField == "assinatura"
            ? "another-test-only-signing-key-for-rejection-tests"
            : TamoJuntoGamesApiFactory.JwtKey;
        var token = new JwtSecurityToken(
            issuer: invalidField == "emissor" ? "outro-emissor" : TamoJuntoGamesApiFactory.JwtIssuer,
            audience: invalidField == "publico" ? "outro-publico" : TamoJuntoGamesApiFactory.JwtAudience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString())],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));

        var response = await client.GetAsync($"/api/usuarios/{usuario.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static bool HasConfigurationFailure(Exception exception)
    {
        if (exception is OptionsValidationException ||
            exception is InvalidOperationException &&
            exception.Message.Contains("Jwt:ExpireMinutes", StringComparison.Ordinal))
            return true;

        if (exception is AggregateException aggregate)
            return aggregate.InnerExceptions.Any(HasConfigurationFailure);

        return exception.InnerException is not null && HasConfigurationFailure(exception.InnerException);
    }
}
