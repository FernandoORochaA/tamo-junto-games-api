using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TamoJuntoGames.API.DTOs;
using TamoJuntoGames.API.Exceptions;
using TamoJuntoGames.API.Services;
using TamoJuntoGames.API.Tests.Infrastructure;
using Xunit;

namespace TamoJuntoGames.API.Tests;

public class HistoricalEmailMigrationTests
{
    [Theory]
    [InlineData("pessoa@example.invalid", false)]
    [InlineData("  pessoa@example.invalid  ", false)]
    [InlineData("josé@example.invalid", true)]
    [InlineData("\tpessoa@example.invalid\t", true)]
    [InlineData("\u00a0pessoa@example.invalid\u00a0", true)]
    [InlineData("\u2003pessoa@example.invalid\u2003", true)]
    public async Task BackfillHistoricoDocumentaDivergenciaLoginECadastro(string email, bool diverge)
    {
        await using var database = await HistoricalEmailDatabase.CreateAsync();
        await database.MigrateAsync(HistoricalEmailDatabase.ProfileMigration);
        const string oldPassword = "SenhaSinteticaLegada123!";
        await database.InsertLegacyAsync(email, BCrypt.Net.BCrypt.HashPassword(oldPassword));
        await database.MigrateAsync();
        Assert.Equal(HistoricalEmailDatabase.MigrationIds, await database.Context.Database.GetAppliedMigrationsAsync());
        var legacy = await database.Context.Usuarios.AsNoTracking().SingleAsync();
        var identity = EmailNormalizer.ParaIdentidade(email);
        Assert.Equal(diverge, !string.Equals(identity, legacy.EmailNormalizado, StringComparison.Ordinal));
        // A alteração de Email para apresentação também é parte do backfill histórico.
        Assert.Equal(email.Trim(' '), legacy.Email);
        var service = database.CreateService();
        var login = await service.LoginAsync(new LoginUsuarioDTO { Email = email, Senha = oldPassword });
        var originalInput = Signup(email);
        Assert.True(Validator.TryValidateObject(originalInput, new ValidationContext(originalInput), [], true));
        var signup = Signup(EmailNormalizer.ParaApresentacao(email));
        Assert.True(Validator.TryValidateObject(signup, new ValidationContext(signup), [], true));

        if (!diverge)
        {
            Assert.Equal(legacy.Id, Assert.IsType<LoginRespostaDTO>(login).Usuario.Id);
            await Assert.ThrowsAsync<EmailDuplicadoException>(() => service.CriarAsync(signup));
            Assert.Equal(1, await database.Context.Usuarios.CountAsync());
            return;
        }

        Assert.Null(login); // Conta existe, mas a identidade histórica não é encontrada.
        var created = await service.CriarAsync(signup);
        Assert.NotEqual(legacy.Id, created.Id);
        var rows = await database.Context.Usuarios.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows.GroupBy(u => EmailNormalizer.ParaIdentidade(u.Email), StringComparer.Ordinal));
        Assert.Equal(legacy.Senha, rows.Single(u => u.Id == legacy.Id).Senha);
        Assert.Null(await service.LoginAsync(new LoginUsuarioDTO { Email = email, Senha = oldPassword }));
        var newLogin = await service.LoginAsync(new LoginUsuarioDTO { Email = email, Senha = signup.Senha });
        Assert.Equal(created.Id, Assert.IsType<LoginRespostaDTO>(newLogin).Usuario.Id);
    }

    [Theory]
    [InlineData("pessoa@example.invalid", "PESSOA@example.invalid")]
    [InlineData("pessoa@example.invalid", "  pessoa@example.invalid  ")]
    public async Task ColisaoAsciiInterrompeMigrationEPreservaRegistros(string first, string second)
    {
        await using var database = await HistoricalEmailDatabase.CreateAsync();
        await database.MigrateAsync(HistoricalEmailDatabase.ProfileMigration);
        await database.InsertLegacyAsync(first);
        await database.InsertLegacyAsync(second);
        var exception = await Assert.ThrowsAsync<SqliteException>(() => database.MigrateAsync());
        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Equal(2067, exception.SqliteExtendedErrorCode);
        Assert.Equal(HistoricalEmailDatabase.MigrationIds.Take(2), await database.Context.Database.GetAppliedMigrationsAsync());
        await using var command = database.Connection.CreateCommand();
        command.CommandText = "SELECT Email FROM Usuarios ORDER BY Id";
        await using var reader = await command.ExecuteReaderAsync();
        var emails = new List<string>();
        while (await reader.ReadAsync()) emails.Add(reader.GetString(0));
        Assert.Equal(new[] { first, second }, emails);
    }

    [Theory]
    [InlineData("josé@example.invalid", "JOSÉ@example.invalid")]
    [InlineData("\tpessoa@example.invalid\t", "pessoa@example.invalid")]
    public async Task IndiceHistoricoPermiteColisaoLogicaMasRejeitaIdentidadePersistidaIgual(string first, string second)
    {
        await using var database = await HistoricalEmailDatabase.CreateAsync();
        await database.MigrateAsync(HistoricalEmailDatabase.ProfileMigration);
        await database.InsertLegacyAsync(first);
        await database.InsertLegacyAsync(second);
        await database.MigrateAsync();
        var users = await database.Context.Usuarios.AsNoTracking().OrderBy(u => u.Id).ToListAsync();
        Assert.Equal(2, users.Count);
        Assert.NotEqual(users[0].EmailNormalizado, users[1].EmailNormalizado);
        Assert.Equal(EmailNormalizer.ParaIdentidade(users[0].Email), EmailNormalizer.ParaIdentidade(users[1].Email));
        var exception = await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await using var command = database.Connection.CreateCommand();
            command.CommandText = "INSERT INTO Usuarios (NomeCompleto, Apelido, Email, EmailNormalizado, Senha) SELECT NomeCompleto, Apelido, Email, EmailNormalizado, Senha FROM Usuarios WHERE Id = 1";
            await command.ExecuteNonQueryAsync();
        });
        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Equal(2067, exception.SqliteExtendedErrorCode);
    }

    [Theory]
    [InlineData("josé@example.invalid")]
    [InlineData("\tpessoa@example.invalid\t")]
    [InlineData("\u00a0pessoa@example.invalid\u00a0")]
    public async Task FluxoAtualNormalizaEncontraContaERejeitaDuplicidade(string email)
    {
        await using var database = await HistoricalEmailDatabase.CreateAsync();
        await database.MigrateAsync();
        var service = database.CreateService();
        var signup = Signup(email);
        var created = await service.CriarAsync(signup);
        var row = await database.Context.Usuarios.AsNoTracking().SingleAsync();
        Assert.Equal(EmailNormalizer.ParaApresentacao(email), row.Email);
        Assert.Equal(EmailNormalizer.ParaIdentidade(email), row.EmailNormalizado);
        var login = await service.LoginAsync(new LoginUsuarioDTO { Email = email, Senha = signup.Senha });
        Assert.Equal(created.Id, Assert.IsType<LoginRespostaDTO>(login).Usuario.Id);
        await Assert.ThrowsAsync<EmailDuplicadoException>(() => service.CriarAsync(Signup(row.EmailNormalizado)));
        Assert.Equal(1, await database.Context.Usuarios.CountAsync());
    }

    private static CriarUsuarioDTO Signup(string email) => new()
    {
        NomeCompleto = "Pessoa sintética", Apelido = "Jogador", Email = email, ConfirmarEmail = email,
        Senha = "SenhaSinteticaNova123!", ConfirmarSenha = "SenhaSinteticaNova123!"
    };
}
