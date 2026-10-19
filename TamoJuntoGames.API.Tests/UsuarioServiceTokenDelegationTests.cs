using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TamoJuntoGames.API.Data;
using TamoJuntoGames.API.DTOs;
using TamoJuntoGames.API.Services;
using TamoJuntoGames.API.Tests.Infrastructure;
using Xunit;

namespace TamoJuntoGames.API.Tests;

public class UsuarioServiceTokenDelegationTests
{
    private const string SenhaValida = "SenhaFicticia123!";

    [Fact]
    public async Task LoginValidoDelegaDadosCorretosERepassaTokenEExpiracaoDoEmissor()
    {
        var emissor = new TokenServiceSubstituto();
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = ComEmissorSubstituto(baseFactory, emissor);
        using var client = CriarCliente(factory);
        var usuario = await CadastrarUsuarioAsync(client);

        var response = await client.PostAsJsonAsync("/api/usuarios/login", new LoginUsuarioDTO
        {
            Email = "  jogador@EXAMPLE.COM  ",
            Senha = SenhaValida
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginRespostaDTO>();
        Assert.NotNull(login);
        Assert.Equal(usuario.Id, login.Usuario.Id);
        Assert.Equal(usuario.Email, login.Usuario.Email);
        Assert.Equal(emissor.Resultado.Token, login.Token);
        Assert.Equal(emissor.Resultado.ExpiraEm, login.ExpiraEm);
        Assert.Equal(1, emissor.Chamadas);
        Assert.True(emissor.UltimaChamada.HasValue);
        var chamada = emissor.UltimaChamada.Value;
        Assert.Equal(usuario.Id, chamada.UsuarioId);
        Assert.Equal("Jogador@Example.com", chamada.Email);
        Assert.Equal(usuario.Apelido, chamada.Apelido);
    }

    [Theory]
    [InlineData("jogador@example.com", "SenhaIncorreta123!")]
    [InlineData("inexistente@example.com", SenhaValida)]
    public async Task CredenciaisRejeitadasRetornamNullEUnauthorizedSemAcionarEmissor(
        string email, string senha)
    {
        var emissor = new TokenServiceSubstituto();
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = ComEmissorSubstituto(baseFactory, emissor);
        using var client = CriarCliente(factory);
        await CadastrarUsuarioAsync(client);
        var dto = new LoginUsuarioDTO { Email = email, Senha = senha };

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUsuarioService>();
        Assert.Null(await service.LoginAsync(dto));

        var response = await client.PostAsJsonAsync("/api/usuarios/login", dto);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, emissor.Chamadas);
        Assert.Null(emissor.UltimaChamada);
    }

    private static WebApplicationFactory<Program> ComEmissorSubstituto(
        TamoJuntoGamesApiFactory factory, TokenServiceSubstituto emissor)
    {
        return factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITokenService>();
            services.AddScoped<ITokenService>(_ => emissor);
        }));
    }

    private static HttpClient CriarCliente(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    private static async Task<UsuarioRespostaDTO> CadastrarUsuarioAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/usuarios", new CriarUsuarioDTO
        {
            NomeCompleto = "Jogador Ficticio",
            Apelido = "JogadorTeste",
            Email = "Jogador@Example.com",
            ConfirmarEmail = "Jogador@Example.com",
            Senha = SenhaValida,
            ConfirmarSenha = SenhaValida
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var usuario = await response.Content.ReadFromJsonAsync<UsuarioRespostaDTO>();
        Assert.NotNull(usuario);
        return usuario;
    }

    private sealed class TokenServiceSubstituto : ITokenService
    {
        public int Chamadas { get; private set; }
        public (int UsuarioId, string Email, string Apelido)? UltimaChamada { get; private set; }
        public (string Token, DateTime ExpiraEm) Resultado { get; } =
            ("token-ficticio-de-teste", new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        public (string Token, DateTime ExpiraEm) GerarToken(int usuarioId, string email, string apelido)
        {
            Chamadas++;
            UltimaChamada = (usuarioId, email, apelido);
            return Resultado;
        }
    }
}
