using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using TamoJuntoGames.API.Data;
using TamoJuntoGames.API.Services;

namespace TamoJuntoGames.API.Tests.Infrastructure;

// Infraestrutura restrita a testes históricos/diagnóstico, sem host HTTP ou JWT.
internal sealed class HistoricalEmailDatabase : IAsyncDisposable
{
    public const string ProfileMigration = "20260701000000_ProfileFieldsOptional";
    public const string EmailMigration = "20260818172938_EmailNormalizadoUniqueIndex";
    public static readonly string[] MigrationIds =
        ["20251215194706_InitialCreate", ProfileMigration, EmailMigration];

    private HistoricalEmailDatabase(SqliteConnection connection)
    {
        Connection = connection;
        Context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    }

    public SqliteConnection Connection { get; }
    public AppDbContext Context { get; }

    public static async Task<HistoricalEmailDatabase> CreateAsync(string dataSource = ":memory:")
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dataSource, Pooling = false
        }.ToString());
        await connection.OpenAsync();
        return new HistoricalEmailDatabase(connection);
    }

    public Task MigrateAsync(string? target = null) => Context.GetService<IMigrator>().MigrateAsync(target);

    public async Task InsertLegacyAsync(string email, string hash = "hash-sintetico-sem-login")
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = "INSERT INTO Usuarios (NomeCompleto, Apelido, Email, Senha) VALUES ('Pessoa sintética', 'Teste', $email, $hash)";
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$hash", hash);
        await command.ExecuteNonQueryAsync();
    }

    public UsuarioService CreateService() => new(Context, new SyntheticTokenService(), NullLogger<UsuarioService>.Instance);

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Connection.DisposeAsync();
    }

    private sealed class SyntheticTokenService : ITokenService
    {
        public (string Token, DateTime ExpiraEm) GerarToken(int usuarioId, string email, string apelido)
            => ("token-sintetico-nao-jwt", DateTime.UnixEpoch);
    }
}
