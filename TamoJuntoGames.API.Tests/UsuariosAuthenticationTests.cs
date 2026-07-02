using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TamoJuntoGames.API.DTOs;
using TamoJuntoGames.API.Tests.Infrastructure;
using Xunit;

namespace TamoJuntoGames.API.Tests;

public class UsuariosAuthenticationTests
{
    private const string SenhaValida = "SenhaForte123";

    [Fact]
    public async Task LoginComCredenciaisValidasRetornaUsuarioTokenEExpiracao()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync(senha: SenhaValida);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/usuarios/login", new LoginUsuarioDTO
        {
            Email = usuario.Email,
            Senha = SenhaValida
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var login = await response.Content.ReadFromJsonAsync<LoginRespostaDTO>();
        Assert.NotNull(login);
        Assert.Equal(usuario.Id, login.Usuario.Id);
        Assert.Equal(usuario.Email, login.Usuario.Email);
        Assert.False(string.IsNullOrWhiteSpace(login.Token));
        Assert.True(login.ExpiraEm > DateTime.UtcNow);
    }

    [Fact]
    public async Task LoginComSenhaInvalidaRetornaUnauthorized()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync(senha: SenhaValida);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/usuarios/login", new LoginUsuarioDTO
        {
            Email = usuario.Email,
            Senha = "SenhaIncorreta999"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginComEmailInvalidoRetornaBadRequest()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/usuarios/login", new LoginUsuarioDTO
        {
            Email = "email-invalido",
            Senha = SenhaValida
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListagemSemTokenRetornaUnauthorized()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/usuarios");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListagemComTokenValidoRetornaOk()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync(senha: SenhaValida);
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await FazerLoginAsync(client, usuario.Email));

        var response = await client.GetAsync("/api/usuarios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TokenExpiradoRetornaUnauthorized()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CriarTokenExpirado(usuario.Id, usuario.Email));

        var response = await client.GetAsync("/api/usuarios");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CadastroSemTokenRetornaCreated()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/usuarios", new CriarUsuarioDTO
        {
            NomeCompleto = "Nova Pessoa",
            Apelido = "Nova",
            Email = "nova@example.com",
            ConfirmarEmail = "nova@example.com",
            Senha = SenhaValida,
            ConfirmarSenha = SenhaValida
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var usuarioCriado = await response.Content.ReadFromJsonAsync<UsuarioRespostaDTO>();
        Assert.NotNull(usuarioCriado);
        Assert.Equal("nova@example.com", usuarioCriado.Email);
    }

    [Fact]
    public async Task UsuarioAutenticadoPodeAtualizarOutroUsuarioNoComportamentoAtual()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuarioAutenticado = await factory.AdicionarUsuarioAsync(
            email: "autenticado@example.com",
            senha: SenhaValida,
            apelido: "Autenticado");
        var outroUsuario = await factory.AdicionarUsuarioAsync(
            email: "outro@example.com",
            senha: SenhaValida,
            apelido: "Outro");
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                await FazerLoginAsync(client, usuarioAutenticado.Email));

        var response = await client.PutAsJsonAsync(
            $"/api/usuarios/{outroUsuario.Id}",
            new AtualizarUsuarioDTO
            {
                NomeCompleto = "Outro Usuário Atualizado",
                Apelido = "Atualizado",
                Email = "outro-atualizado@example.com"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UsuarioAutenticadoPodeExcluirOutroUsuarioNoComportamentoAtual()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuarioAutenticado = await factory.AdicionarUsuarioAsync(
            email: "autenticado@example.com",
            senha: SenhaValida,
            apelido: "Autenticado");
        var outroUsuario = await factory.AdicionarUsuarioAsync(
            email: "outro@example.com",
            senha: SenhaValida,
            apelido: "Outro");
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                await FazerLoginAsync(client, usuarioAutenticado.Email));

        var response = await client.DeleteAsync($"/api/usuarios/{outroUsuario.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<string> FazerLoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/usuarios/login", new LoginUsuarioDTO
        {
            Email = email,
            Senha = SenhaValida
        });

        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginRespostaDTO>();
        return Assert.IsType<string>(login?.Token);
    }

    private static string CriarTokenExpirado(int usuarioId, string email)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(TamoJuntoGamesApiFactory.JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TamoJuntoGamesApiFactory.JwtIssuer,
            audience: TamoJuntoGamesApiFactory.JwtAudience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, usuarioId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email)
            ],
            expires: DateTime.UtcNow.AddMinutes(-1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
