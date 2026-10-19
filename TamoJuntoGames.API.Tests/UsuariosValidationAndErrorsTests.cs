using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TamoJuntoGames.API.Data;
using TamoJuntoGames.API.DTOs;
using TamoJuntoGames.API.Exceptions;
using TamoJuntoGames.API.Services;
using TamoJuntoGames.API.Tests.Infrastructure;
using Xunit;

namespace TamoJuntoGames.API.Tests;

public class UsuariosValidationAndErrorsTests
{
    private const string Senha = "SenhaFicticia123!";
    private const string DetalheInterno = "Já existe um usuário cadastrado; SELECT segredo; senha-hash-token-ficticios";

    [Theory]
    [InlineData("NomeCompleto", 100, false)]
    [InlineData("Apelido", 50, false)]
    [InlineData("Email", 254, false)]
    [InlineData("NomeCompleto", 100, true)]
    [InlineData("Apelido", 50, true)]
    [InlineData("Email", 254, true)]
    public async Task LimiteExatoAceitoEUnidadeSeguinteRejeitada(string campo, int limite, bool atualizar)
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();
        var id = 0;
        if (atualizar)
        {
            var usuario = await factory.AdicionarUsuarioAsync();
            id = usuario.Id;
            Autenticar(factory, client, id);
        }

        var dto = Cadastro();
        dto[campo] = Valor(campo, limite);
        dto["ConfirmarEmail"] = dto["Email"];
        var aceita = await EnviarAsync(client, dto, atualizar, id);
        Assert.Equal(atualizar ? HttpStatusCode.OK : HttpStatusCode.Created, aceita.StatusCode);
        var resultado = await aceita.Content.ReadFromJsonAsync<UsuarioRespostaDTO>();
        Assert.NotNull(resultado);
        Assert.Equal(dto[campo], campo switch
        {
            "NomeCompleto" => resultado.NomeCompleto,
            "Apelido" => resultado.Apelido,
            _ => resultado.Email
        });
        if (!atualizar)
            Assert.EndsWith($"/api/Usuarios/{resultado.Id}", aceita.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);

        dto[campo] = Valor(campo, limite + 1);
        dto["ConfirmarEmail"] = dto["Email"];
        var rejeitada = await EnviarAsync(client, dto, atualizar, id);
        await ValidacaoAsync(rejeitada, campo);
    }

    [Theory]
    [InlineData("NomeCompleto", 50)]
    [InlineData("Apelido", 25)]
    public async Task LimitesContamUnidadesUtf16InclusiveEmoji(string campo, int quantidade)
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();
        var dto = Cadastro();
        dto[campo] = string.Concat(Enumerable.Repeat("😀", quantidade));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/usuarios", dto)).StatusCode);
        dto[campo] += "a";
        await ValidacaoAsync(await client.PostAsJsonAsync("/api/usuarios", dto), campo);
    }

    [Fact]
    public async Task LimiteEmailContaEspacosAntesDoTrimEConfirmacaoTambemTemLimite()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();
        var dto = Cadastro();
        dto["Email"] = dto["ConfirmarEmail"] = " " + Valor("Email", 254) + " ";
        await ValidacaoAsync(await client.PostAsJsonAsync("/api/usuarios", dto), "Email", "ConfirmarEmail");
        dto["Email"] = "teste@example.com";
        dto["ConfirmarEmail"] = Valor("Email", 255);
        await ValidacaoAsync(await client.PostAsJsonAsync("/api/usuarios", dto), "ConfirmarEmail");
    }

    [Theory]
    [InlineData("NomeCompleto", "   ")]
    [InlineData("Apelido", "")]
    [InlineData("Email", "invalido")]
    [InlineData("ConfirmarEmail", "")]
    [InlineData("ConfirmarEmail", "TESTE@example.com")]
    [InlineData("ConfirmarEmail", " teste@example.com ")]
    [InlineData("Senha", "")]
    [InlineData("Senha", "1234567")]
    [InlineData("ConfirmarSenha", "")]
    [InlineData("ConfirmarSenha", "senhaDiferente123!")]
    public async Task ValidacoesExistentesPermanecem(string campo, string valor)
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();
        var dto = Cadastro();
        dto[campo] = valor;
        await ValidacaoAsync(await client.PostAsJsonAsync("/api/usuarios", dto), campo);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("null")]
    public async Task JsonInvalidoOuCorpoAusenteRetornaValidationProblemDetails(string json)
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        await ValidacaoAsync(await client.PostAsync("/api/usuarios", content));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmailDuplicadoRetornaProblemDetailsControlado(bool atualizar)
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();
        await factory.AdicionarUsuarioAsync(email: "teste@example.com");
        var id = 0;
        if (atualizar)
        {
            id = (await factory.AdicionarUsuarioAsync(email: "proprio@example.com")).Id;
            Autenticar(factory, client, id);
        }
        var response = await EnviarAsync(client, Cadastro(), atualizar, id);
        var problem = await ProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal(EmailDuplicadoException.MensagemPublica, problem.Detail);
    }

    [Theory]
    [InlineData("teste@example.com", "SenhaIncorreta123!")]
    [InlineData("inexistente@example.com", Senha)]
    public async Task CredenciaisInvalidasTemMensagemGenericaSemChallenge(string email, string senha)
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();
        await factory.AdicionarUsuarioAsync(email: "teste@example.com", senha: Senha);
        var response = await client.PostAsJsonAsync("/api/usuarios/login", new { email, senha });
        var problem = await ProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal("E-mail ou senha inválidos.", problem.Detail);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.DoesNotContain(email, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JwtAusenteOuInvalidoPreservaChallengeComProblemDetails(bool tokenInvalido)
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();
        if (tokenInvalido)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-ficticio-invalido");
        var response = await client.GetAsync("/api/usuarios/1");
        await ProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.DoesNotContain("token-ficticio-invalido", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task OperarSobreOutroUsuarioContinuaForbiddenSemAlterarDados(string metodo)
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();
        var proprio = await factory.AdicionarUsuarioAsync(email: "proprio@example.com");
        var outro = await factory.AdicionarUsuarioAsync(email: "outro@example.com");
        Autenticar(factory, client, proprio.Id);
        using var request = new HttpRequestMessage(new HttpMethod(metodo), $"/api/usuarios/{outro.Id}");
        if (metodo == "PUT") request.Content = JsonContent.Create(Cadastro());
        var response = await client.SendAsync(request);
        await ProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Empty(response.Headers.WwwAuthenticate);
        using var scope = factory.Services.CreateScope();
        var persistido = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Usuarios.AsNoTracking().SingleAsync(u => u.Id == outro.Id);
        Assert.Equal(outro.NomeCompleto, persistido.NomeCompleto);
        Assert.Equal(outro.Apelido, persistido.Apelido);
        Assert.Equal(outro.Email, persistido.Email);
        Assert.Equal(outro.EmailNormalizado, persistido.EmailNormalizado);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ProprioUsuarioInexistenteRetorna404(string metodo)
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();
        Autenticar(factory, client, 999);
        using var request = new HttpRequestMessage(new HttpMethod(metodo), "/api/usuarios/999");
        if (metodo == "PUT") request.Content = JsonContent.Create(Cadastro());
        var problem = await ProblemAsync(await client.SendAsync(request), HttpStatusCode.NotFound);
        Assert.Equal("Usuário não encontrado.", problem.Detail);
    }

    [Fact]
    public async Task RotaInexistenteEMetodoNaoPermitidoPreservam404E405EAllow()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();
        await ProblemAsync(await client.GetAsync("/rota-inexistente"), HttpStatusCode.NotFound);
        var response = await client.GetAsync("/api/usuarios");
        await ProblemAsync(response, HttpStatusCode.MethodNotAllowed);
        Assert.Contains("POST", response.Content.Headers.Allow);
    }

    [Theory]
    [InlineData("Testing", false, false)]
    [InlineData("Testing", true, false)]
    [InlineData("Development", false, false)]
    [InlineData("Development", true, false)]
    [InlineData("Development", false, true)]
    public async Task ExcecaoNaoClassificadaPorMensagemRetorna500Sanitizado(string ambiente, bool atualizar, bool acceptIncompativel)
    {
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(ambiente);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUsuarioService>();
                services.AddScoped<IUsuarioService, UsuarioServiceComFalha>();
            });
        });
        using var client = CriarCliente(factory);
        if (atualizar) Autenticar(factory, client, 1);
        if (acceptIncompativel) client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        var response = await EnviarAsync(client, Cadastro(), atualizar, 1);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (acceptIncompativel) Assert.Empty(body);
        else await ProblemAsync(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain(DetalheInterno, body);
        Assert.DoesNotContain("SELECT", body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
        Assert.DoesNotContain(nameof(UsuarioServiceComFalha), body);
    }

    [Fact]
    public async Task LoginPreservaEmailESenhaLongosECadastroNaoLimitaSenha()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();
        var dto = Cadastro();
        dto["Senha"] = dto["ConfirmarSenha"] = new string('a', 72) + "😀cauda-ficticia";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/usuarios", dto)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/usuarios/login", new { Email = dto["Email"], Senha = dto["Senha"] })).StatusCode);
        // Registra o comportamento BCrypt existente: o sufixo após 72 bytes não distingue a senha.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/usuarios/login", new { Email = dto["Email"], Senha = new string('a', 72) + "outra-cauda" })).StatusCode);
        var emailAntigo = Valor("Email", 255);
        await factory.AdicionarUsuarioAsync(email: emailAntigo, senha: dto["Senha"]);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/usuarios/login", new { Email = emailAntigo, Senha = dto["Senha"] })).StatusCode);
    }

    [Fact]
    public async Task ExclusaoContinua204SemCorpo()
    {
        using var factory = new TamoJuntoGamesApiFactory();
        using var client = factory.CreateHttpsClient();
        var usuario = await factory.AdicionarUsuarioAsync();
        Autenticar(factory, client, usuario.Id);
        var response = await client.DeleteAsync($"/api/usuarios/{usuario.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    internal static Dictionary<string, string> Cadastro() => new()
    {
        ["NomeCompleto"] = "Pessoa Fictícia", ["Apelido"] = "Jogador",
        ["Email"] = "teste@example.com", ["ConfirmarEmail"] = "teste@example.com",
        ["Senha"] = Senha, ["ConfirmarSenha"] = Senha
    };

    private static string Valor(string campo, int comprimento) => campo == "Email"
        ? new string('a', comprimento - "@example.com".Length) + "@example.com"
        : new string('a', comprimento);

    internal static Task<HttpResponseMessage> EnviarAsync(HttpClient client, Dictionary<string, string> dto, bool atualizar, int id) => atualizar
        ? client.PutAsJsonAsync($"/api/usuarios/{id}", dto)
        : client.PostAsJsonAsync("/api/usuarios", dto);

    internal static void Autenticar(WebApplicationFactory<Program> factory, HttpClient client, int id)
    {
        using var scope = factory.Services.CreateScope();
        var (token, _) = scope.ServiceProvider.GetRequiredService<ITokenService>().GerarToken(id, "teste@example.com", "Teste");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    internal static HttpClient CriarCliente(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        return client;
    }

    internal static async Task<ProblemDetails> ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(problem);
        Assert.Equal((int)status, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Type));
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TamoJuntoGamesApiFactory.JwtKey, body);
        return problem;
    }

    private static async Task ValidacaoAsync(HttpResponseMessage response, params string[] campos)
    {
        await ProblemAsync(response, HttpStatusCode.BadRequest);
        var problem = JsonSerializer.Deserialize<ValidationProblemDetails>(
            await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
        foreach (var campo in campos) Assert.Contains(campo, problem.Errors.Keys);
        Assert.DoesNotContain(Senha, await response.Content.ReadAsStringAsync());
    }

    private sealed class UsuarioServiceComFalha : IUsuarioService
    {
        public Task<UsuarioRespostaDTO> CriarAsync(CriarUsuarioDTO dto) => throw new InvalidOperationException(DetalheInterno);
        public Task<UsuarioRespostaDTO?> AtualizarAsync(int id, AtualizarUsuarioDTO dto) => throw new InvalidOperationException(DetalheInterno);
        public Task<UsuarioRespostaDTO?> ObterPorIdAsync(int id) => throw new InvalidOperationException(DetalheInterno);
        public Task<bool> DeletarAsync(int id) => throw new InvalidOperationException(DetalheInterno);
        public Task<LoginRespostaDTO?> LoginAsync(LoginUsuarioDTO dto) => throw new InvalidOperationException(DetalheInterno);
    }
}
