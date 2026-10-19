using System.Data.Common;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TamoJuntoGames.API.Data;
using TamoJuntoGames.API.Models;
using TamoJuntoGames.API.Tests.Infrastructure;
using Xunit;
using static TamoJuntoGames.API.Tests.UsuariosValidationAndErrorsTests;

namespace TamoJuntoGames.API.Tests;

public class UsuariosQueryProjectionTests
{
    [Fact]
    public async Task ConsultaPropriaSelecionaSomenteColunasDoDtoSemMaterializarUsuario()
    {
        var interceptor = new ProjecaoInterceptor();
        using var baseFactory = new TamoJuntoGamesApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = CriarCliente(factory);
        var usuario = new Usuario
        {
            NomeCompleto = "Pessoa Fictícia", Apelido = "JogadorTeste",
            Email = "jogador@example.com", EmailNormalizado = "JOGADOR@EXAMPLE.COM",
            DataNascimento = new DateTime(2000, 1, 1), Genero = "Não informado",
            Senha = "hash-ficticio-nao-utilizado-no-login"
        };
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Usuarios.Add(usuario);
            await context.SaveChangesAsync();
        }
        Autenticar(factory, client, usuario.Id);

        // A preparação terminou: capturar somente os comandos da requisição GET real.
        interceptor.Ativo = true;
        using var response = await client.GetAsync($"/api/usuarios/{usuario.Id}");
        interceptor.Ativo = false;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var consulta = Assert.Single(interceptor.Consultas);
        Assert.StartsWith("SELECT", consulta.Sql.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            new[] { "Apelido", "Email", "Id", "NomeCompleto" },
            consulta.Colunas.OrderBy(coluna => coluna, StringComparer.Ordinal));
        foreach (var coluna in new[] { "Senha", "EmailNormalizado", "DataNascimento", "Genero" })
            Assert.DoesNotContain(coluna, consulta.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, interceptor.UsuariosMaterializados);
    }

    private sealed class ProjecaoInterceptor : DbCommandInterceptor, IMaterializationInterceptor
    {
        public bool Ativo { get; set; }
        public List<(string Sql, string[] Colunas)> Consultas { get; } = [];
        public int UsuariosMaterializados { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Ativo)
                Consultas.Add((command.CommandText,
                    Enumerable.Range(0, result.FieldCount).Select(result.GetName).ToArray()));
            return ValueTask.FromResult(result);
        }

        public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
        {
            if (Ativo && entity is Usuario)
                UsuariosMaterializados++;
            return entity;
        }
    }
}
