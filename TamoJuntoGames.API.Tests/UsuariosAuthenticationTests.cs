using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TamoJuntoGames.API.Data;
using TamoJuntoGames.API.DTOs;
using TamoJuntoGames.API.Models;
using TamoJuntoGames.API.Services;
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
    public async Task ConsultaPorIdSemTokenRetornaUnauthorized()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync($"/api/usuarios/{usuario.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListagemGeralDeUsuariosNaoEstaDisponivel()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync(senha: SenhaValida);
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await FazerLoginAsync(client, usuario.Email));

        var response = await client.GetAsync("/api/usuarios");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task TokenExpiradoRetornaUnauthorized()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CriarTokenExpirado(usuario.Id, usuario.Email));

        var response = await client.GetAsync($"/api/usuarios/{usuario.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UsuarioAutenticadoPodeConsultarProprioUsuarioPorId()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync(senha: SenhaValida);
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await FazerLoginAsync(client, usuario.Email));

        var response = await client.GetAsync($"/api/usuarios/{usuario.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usuarioRetornado = await response.Content.ReadFromJsonAsync<UsuarioRespostaDTO>();
        Assert.NotNull(usuarioRetornado);
        Assert.Equal(usuario.Id, usuarioRetornado.Id);
        Assert.Equal(usuario.Email, usuarioRetornado.Email);
    }

    [Fact]
    public async Task UsuarioAutenticadoRecebeForbiddenAoConsultarOutroUsuario()
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

        var response = await client.GetAsync($"/api/usuarios/{outroUsuario.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var usuarioPersistido = await context.Usuarios
            .SingleAsync(usuario => usuario.Id == outroUsuario.Id);

        Assert.Equal("Fernando Rocha", usuarioPersistido.NomeCompleto);
        Assert.Equal("Outro", usuarioPersistido.Apelido);
        Assert.Equal("outro@example.com", usuarioPersistido.Email);
        Assert.Equal("OUTRO@EXAMPLE.COM", usuarioPersistido.EmailNormalizado);
    }

    [Fact]
    public async Task TokenComSubAusenteRetornaForbiddenEmRotaProtegidaPorUsuario()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CriarTokenSemSub(usuario.Email));

        var response = await client.GetAsync($"/api/usuarios/{usuario.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TokenComSubInvalidoRetornaForbiddenEmRotaProtegidaPorUsuario()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CriarTokenComSub("valor-invalido", usuario.Email));

        var response = await client.GetAsync($"/api/usuarios/{usuario.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var usuarioPersistido = await context.Usuarios
            .SingleAsync(usuario => usuario.Email == "nova@example.com");

        Assert.Equal("NOVA@EXAMPLE.COM", usuarioPersistido.EmailNormalizado);
        Assert.Null(usuarioPersistido.DataNascimento);
        Assert.Null(usuarioPersistido.Genero);
    }

    [Fact]
    public async Task CadastroComEmailDuplicadoMudandoCaixaRetornaConflict()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        await factory.AdicionarUsuarioAsync(email: "ana@example.com");
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/usuarios", new CriarUsuarioDTO
        {
            NomeCompleto = "Ana Duplicada",
            Apelido = "Ana2",
            Email = "ANA@EXAMPLE.com",
            ConfirmarEmail = "ANA@EXAMPLE.com",
            Senha = SenhaValida,
            ConfirmarSenha = SenhaValida
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CadastroRemoveEspacosExternosEGravaEmailNormalizado()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/usuarios", new CriarUsuarioDTO
        {
            NomeCompleto = "Email Com Espaço",
            Apelido = "Espaco",
            Email = "  Espaco@Example.com  ",
            ConfirmarEmail = "  Espaco@Example.com  ",
            Senha = SenhaValida,
            ConfirmarSenha = SenhaValida
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var usuarioCriado = await response.Content.ReadFromJsonAsync<UsuarioRespostaDTO>();
        Assert.NotNull(usuarioCriado);
        Assert.Equal("Espaco@Example.com", usuarioCriado.Email);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var usuarioPersistido = await context.Usuarios
            .SingleAsync(usuario => usuario.EmailNormalizado == "ESPACO@EXAMPLE.COM");

        Assert.Equal("Espaco@Example.com", usuarioPersistido.Email);
        Assert.Equal("ESPACO@EXAMPLE.COM", usuarioPersistido.EmailNormalizado);
    }

    [Fact]
    public async Task LoginFuncionaComEmailEmCaixaDiferente()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        await factory.AdicionarUsuarioAsync(
            email: "Jogador@Example.com",
            senha: SenhaValida);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/usuarios/login", new LoginUsuarioDTO
        {
            Email = "jogador@example.COM",
            Senha = SenhaValida
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LoginFuncionaComEspacosExternosNoEmail()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        await factory.AdicionarUsuarioAsync(
            email: "jogador@example.com",
            senha: SenhaValida);
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/usuarios/login", new LoginUsuarioDTO
        {
            Email = "  jogador@example.com  ",
            Senha = SenhaValida
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UsuarioAutenticadoPodeAtualizarProprioUsuario()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync(
            email: "autenticado@example.com",
            senha: SenhaValida,
            apelido: "Autenticado");
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                await FazerLoginAsync(client, usuario.Email));

        var response = await client.PutAsJsonAsync(
            $"/api/usuarios/{usuario.Id}",
            new AtualizarUsuarioDTO
            {
                NomeCompleto = "Usuário Atualizado",
                Apelido = "Atualizado",
                Email = "autenticado-atualizado@example.com"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UsuarioAutenticadoRecebeForbiddenAoAtualizarOutroUsuario()
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
                NomeCompleto = "Outro Usuário",
                Apelido = "Outro",
                Email = "outro-atualizado@example.com"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AtualizacaoNaoPermiteEmailDeOutroUsuarioMesmoComDiferencaDeCaixa()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuarioAutenticado = await factory.AdicionarUsuarioAsync(
            email: "autenticado@example.com",
            senha: SenhaValida,
            apelido: "Autenticado");
        await factory.AdicionarUsuarioAsync(
            email: "usado@example.com",
            senha: SenhaValida,
            apelido: "Usado");
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                await FazerLoginAsync(client, usuarioAutenticado.Email));

        var response = await client.PutAsJsonAsync(
            $"/api/usuarios/{usuarioAutenticado.Id}",
            new AtualizarUsuarioDTO
            {
                NomeCompleto = "Usuário Autenticado",
                Apelido = "Autenticado",
                Email = "USADO@EXAMPLE.com"
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AtualizacaoRemoveEspacosExternosERecalculaEmailNormalizado()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync(
            email: "autenticado@example.com",
            senha: SenhaValida,
            apelido: "Autenticado");
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                await FazerLoginAsync(client, usuario.Email));

        var response = await client.PutAsJsonAsync(
            $"/api/usuarios/{usuario.Id}",
            new AtualizarUsuarioDTO
            {
                NomeCompleto = "Usuário Atualizado",
                Apelido = "Atualizado",
                Email = "  Outro.Novo@Example.com  "
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usuarioAtualizado = await response.Content.ReadFromJsonAsync<UsuarioRespostaDTO>();
        Assert.NotNull(usuarioAtualizado);
        Assert.Equal("Outro.Novo@Example.com", usuarioAtualizado.Email);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var usuarioPersistido = await context.Usuarios
            .SingleAsync(usuarioPersistido => usuarioPersistido.Id == usuario.Id);

        Assert.Equal("Outro.Novo@Example.com", usuarioPersistido.Email);
        Assert.Equal("OUTRO.NOVO@EXAMPLE.COM", usuarioPersistido.EmailNormalizado);
    }

    [Fact]
    public async Task BancoGaranteUnicidadeRealDeEmailNormalizado()
    {
        using var factory = new TamoJuntoGamesApiFactory();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.EnsureCreatedAsync();

        context.Usuarios.Add(new Usuario
        {
            NomeCompleto = "Primeiro Usuário",
            Apelido = "Primeiro",
            Email = "primeiro@example.com",
            EmailNormalizado = EmailNormalizer.ParaIdentidade("primeiro@example.com"),
            Senha = BCrypt.Net.BCrypt.HashPassword(SenhaValida)
        });

        await context.SaveChangesAsync();

        context.Usuarios.Add(new Usuario
        {
            NomeCompleto = "Segundo Usuário",
            Apelido = "Segundo",
            Email = "PRIMEIRO@example.com",
            EmailNormalizado = EmailNormalizer.ParaIdentidade("PRIMEIRO@example.com"),
            Senha = BCrypt.Net.BCrypt.HashPassword(SenhaValida)
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task UsuarioAutenticadoPodeExcluirProprioUsuario()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        var usuario = await factory.AdicionarUsuarioAsync(
            email: "autenticado@example.com",
            senha: SenhaValida,
            apelido: "Autenticado");
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                await FazerLoginAsync(client, usuario.Email));

        var response = await client.DeleteAsync($"/api/usuarios/{usuario.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(await context.Usuarios.FindAsync(usuario.Id));
    }

    [Fact]
    public async Task UsuarioAutenticadoRecebeForbiddenAoExcluirOutroUsuario()
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

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotNull(await context.Usuarios.FindAsync(outroUsuario.Id));
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
        return CriarToken(
            [
                new Claim(JwtRegisteredClaimNames.Sub, usuarioId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email)
            ],
            DateTime.UtcNow.AddMinutes(-1));
    }

    private static string CriarTokenSemSub(string email)
    {
        return CriarToken(
            [
                new Claim(JwtRegisteredClaimNames.Email, email)
            ],
            DateTime.UtcNow.AddMinutes(60));
    }

    private static string CriarTokenComSub(string sub, string email)
    {
        return CriarToken(
            [
                new Claim(JwtRegisteredClaimNames.Sub, sub),
                new Claim(JwtRegisteredClaimNames.Email, email)
            ],
            DateTime.UtcNow.AddMinutes(60));
    }

    private static string CriarToken(IEnumerable<Claim> claims, DateTime expires)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(TamoJuntoGamesApiFactory.JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TamoJuntoGamesApiFactory.JwtIssuer,
            audience: TamoJuntoGamesApiFactory.JwtAudience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
