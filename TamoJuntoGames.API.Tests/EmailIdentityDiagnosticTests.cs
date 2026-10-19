using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TamoJuntoGames.API.Tests.Infrastructure;
using TamoJuntoGames.EmailIdentityDiagnostic;
using Xunit;

namespace TamoJuntoGames.API.Tests;

public class EmailIdentityDiagnosticTests
{
    public static TheoryData<string[], int[], int> Scenarios => new()
    {
        { [], [], 0 },
        { ["pessoa@example.invalid"], [], 0 },
        { ["josé@example.invalid"], [1], 0 },
        { ["josé@example.invalid", "\tpessoa@example.invalid\t", "\u00a0outra@example.invalid\u00a0"], [1, 2, 3], 0 },
        { ["josé@example.invalid", "JOSÉ@example.invalid"], [1], 1 }
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task DiagnosticaCopiaSinteticaSemModificarArquivoRegistrosOuEsquema(
        string[] emails, int[] expectedDivergentIds, int collisions)
    {
        using var temporary = new TemporaryDatabase();
        await temporary.PrepareAsync(emails);
        var bytesBefore = await File.ReadAllBytesAsync(temporary.Path);
        var stateBefore = await SnapshotAsync(temporary.Path);
        var output = new StringWriter();
        var error = new StringWriter();

        // Mesmo ponto de entrada do console, sem construir a API/configuração/JWT.
        var exitCode = await DiagnosticCommand.ExecuteAsync(["--database-copy", temporary.Path], output, error);

        var report = JsonSerializer.Deserialize<EmailIdentityReport>(output.ToString())!;
        Assert.Equal(emails.Length, report.RegistrosExaminados);
        Assert.Equal(expectedDivergentIds.Length, report.QuantidadeDivergencias);
        Assert.Equal(collisions, report.QuantidadeGruposColisao);
        Assert.Equal(expectedDivergentIds.Length > 0 || collisions > 0, report.NecessitaAnaliseManual);
        Assert.Equal(report.NecessitaAnaliseManual ? 2 : 0, exitCode);
        Assert.Equal(expectedDivergentIds.Order(), report.IdsDivergentes.Order());
        Assert.Equal(collisions, report.GruposColisao.Length);
        if (collisions > 0) Assert.Equal(new[] { 1, 2 }, Assert.Single(report.GruposColisao).Order());
        Assert.Empty(error.ToString());
        foreach (var email in emails) Assert.DoesNotContain(email, output.ToString());
        Assert.DoesNotContain("example.invalid", output.ToString());
        Assert.DoesNotContain("hash-sintetico", output.ToString());
        Assert.DoesNotContain(temporary.Path, output.ToString());
        Assert.Equal(stateBefore, await SnapshotAsync(temporary.Path));
        Assert.Equal(SHA256.HashData(bytesBefore), SHA256.HashData(await File.ReadAllBytesAsync(temporary.Path)));
        Assert.Equal(new[] { temporary.Path }, Directory.GetFiles(temporary.DirectoryPath));
    }

    [Fact]
    public async Task DoisGruposIndependentesPreservamIdsCompletosSemMisturaOuDuplicacao()
    {
        using var temporary = new TemporaryDatabase();
        // IDs intercalados: grupo {1,3,5}, grupo {2,4} e conta 6 sem colisão.
        await temporary.PrepareAsync([
            "josé@example.invalid", "maria@example.invalid", "JOSÉ@example.invalid",
            "\u00a0maria@example.invalid\u00a0", "\tjosé@example.invalid\t", "outra@example.invalid"
        ]);
        var bytesBefore = await File.ReadAllBytesAsync(temporary.Path);
        var stateBefore = await SnapshotAsync(temporary.Path);
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(2, await DiagnosticCommand.ExecuteAsync(["--database-copy", temporary.Path], output, error));

        var report = JsonSerializer.Deserialize<EmailIdentityReport>(output.ToString())!;
        Assert.Equal(6, report.RegistrosExaminados);
        Assert.Equal(3, report.QuantidadeDivergencias);
        Assert.Equal(new[] { 1, 4, 5 }, report.IdsDivergentes.Order());
        Assert.True(report.NecessitaAnaliseManual);
        Assert.Equal(2, report.QuantidadeGruposColisao);
        Assert.Equal(2, report.GruposColisao.Length);
        Assert.Equal(new[] { 1, 3, 5 }, Assert.Single(report.GruposColisao, group => group.Contains(1)).Order());
        Assert.Equal(new[] { 2, 4 }, Assert.Single(report.GruposColisao, group => group.Contains(2)).Order());
        var groupedIds = report.GruposColisao.SelectMany(group => group).ToArray();
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, groupedIds.Order());
        Assert.Equal(groupedIds.Length, groupedIds.Distinct().Count());
        Assert.Empty(error.ToString());
        Assert.DoesNotContain("example.invalid", output.ToString());
        Assert.Equal(stateBefore, await SnapshotAsync(temporary.Path));
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(temporary.Path));
        Assert.Equal(new[] { temporary.Path }, Directory.GetFiles(temporary.DirectoryPath));
    }

    [Theory]
    [InlineData(null, "PESSOA@EXAMPLE.INVALID", 1L)]
    [InlineData("pessoa@example.invalid", null, 1L)]
    [InlineData("pessoa@example.invalid", "PESSOA@EXAMPLE.INVALID", 2147483648L)]
    public async Task DadosIncompativeisRetornamErroSanitizadoSemAlterarBanco(
        string? email, string? normalizedEmail, long id)
    {
        using var temporary = new TemporaryDatabase();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = temporary.Path, Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            // Esquema apenas deste teste: permite NULL e INTEGER de 64 bits.
            command.CommandText = "CREATE TABLE Usuarios (Id INTEGER PRIMARY KEY, Email TEXT, EmailNormalizado TEXT); INSERT INTO Usuarios (Id, Email, EmailNormalizado) VALUES ($id, $email, $normalizado)";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$email", (object?)email ?? DBNull.Value);
            command.Parameters.AddWithValue("$normalizado", (object?)normalizedEmail ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }
        var bytesBefore = await File.ReadAllBytesAsync(temporary.Path);
        var stateBefore = await SnapshotAsync(temporary.Path);
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(1, await DiagnosticCommand.ExecuteAsync(["--database-copy", temporary.Path], output, error));

        Assert.Empty(output.ToString());
        var message = error.ToString();
        Assert.NotEmpty(message);
        Assert.DoesNotContain(temporary.Path, message);
        Assert.DoesNotContain("example.invalid", message, StringComparison.OrdinalIgnoreCase);
        foreach (var internalDetail in new[]
        {
            "EmailNormalizado", "Usuarios", "SELECT", "SQLite Error", "InvalidDataException",
            "OverflowException", "Int32", "2147483648", "Estrutura ou dados incompatíveis"
        })
            Assert.DoesNotContain(internalDetail, message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(stateBefore, await SnapshotAsync(temporary.Path));
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(temporary.Path));
        Assert.Equal(new[] { temporary.Path }, Directory.GetFiles(temporary.DirectoryPath));
    }

    [Fact]
    public async Task ConsoleExecutaSemHostConfiguracaoOuChaveJwt()
    {
        using var temporary = new TemporaryDatabase();
        await temporary.PrepareAsync([]);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = temporary.DirectoryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(EmailIdentityReader).Assembly.Location);
        start.ArgumentList.Add("--database-copy");
        start.ArgumentList.Add(temporary.Path);
        // Somente no processo filho: nenhum arquivo/configuração/segredo é alterado.
        start.Environment["Jwt__Key"] = string.Empty;
        start.Environment["Jwt__Issuer"] = string.Empty;
        start.Environment["Jwt__Audience"] = string.Empty;
        start.Environment["Jwt__ExpireMinutes"] = "0";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(await stderr);
        var report = JsonSerializer.Deserialize<EmailIdentityReport>(await stdout)!;
        Assert.Equal(0, report.RegistrosExaminados);
        Assert.False(report.NecessitaAnaliseManual);
    }

    [Fact]
    public async Task ColisaoSemDivergenciaIndividualTambemExigeAnaliseManual()
    {
        using var temporary = new TemporaryDatabase();
        await temporary.PrepareAsync([]);
        // Esquema sintético incompatível com o UNIQUE atual, mas legível. O diagnóstico
        // não deve depender do índice nem confundir colisão com divergência individual.
        await using (var connection = new SqliteConnection($"Data Source={temporary.Path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP INDEX IX_Usuarios_EmailNormalizado; INSERT INTO Usuarios (NomeCompleto, Apelido, Email, EmailNormalizado, Senha) VALUES ('Sintético', 'Teste', 'pessoa@example.invalid', 'PESSOA@EXAMPLE.INVALID', 'hash-sintetico'), ('Sintético', 'Teste', 'PESSOA@example.invalid', 'PESSOA@EXAMPLE.INVALID', 'hash-sintetico')";
            await command.ExecuteNonQueryAsync();
        }
        var report = await EmailIdentityReader.ReadAsync(temporary.Path);
        Assert.Equal(0, report.QuantidadeDivergencias);
        Assert.Equal(1, report.QuantidadeGruposColisao);
        Assert.Equal(new[] { 1, 2 }, Assert.Single(report.GruposColisao));
        Assert.True(report.NecessitaAnaliseManual);
    }

    [Fact]
    public async Task BancoInexistenteNaoECriadoENaoExpoeCaminho()
    {
        using var temporary = new TemporaryDatabase();
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(1, await DiagnosticCommand.ExecuteAsync(["--database-copy", temporary.Path], output, error));
        Assert.False(File.Exists(temporary.Path));
        Assert.Empty(Directory.GetFiles(temporary.DirectoryPath));
        Assert.Empty(output.ToString());
        Assert.DoesNotContain(temporary.Path, error.ToString());
        Assert.DoesNotContain(nameof(FileNotFoundException), error.ToString());
        Assert.NotEmpty(error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BancoIncompativelFalhaSemExporDetalhesOuModificarArquivo(bool validSqlite)
    {
        using var temporary = new TemporaryDatabase();
        if (validSqlite)
        {
            await using var connection = new SqliteConnection($"Data Source={temporary.Path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            // Falta EmailNormalizado: simula banco anterior à terceira migration.
            command.CommandText = "CREATE TABLE Usuarios (Id INTEGER, Email TEXT)";
            await command.ExecuteNonQueryAsync();
        }
        else
            await File.WriteAllTextAsync(temporary.Path, "conteudo-sintetico-nao-e-sqlite");
        var before = await File.ReadAllBytesAsync(temporary.Path);
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(1, await DiagnosticCommand.ExecuteAsync(["--database-copy", temporary.Path], output, error));
        Assert.Empty(output.ToString());
        Assert.DoesNotContain(temporary.Path, error.ToString());
        Assert.DoesNotContain("EmailNormalizado", error.ToString());
        Assert.DoesNotContain("SQLite Error", error.ToString());
        Assert.Equal(before, await File.ReadAllBytesAsync(temporary.Path));
        Assert.Equal(new[] { temporary.Path }, Directory.GetFiles(temporary.DirectoryPath));
    }

    [Theory]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    public async Task RecusaSidecarsSemTentarRecuperarOuApagar(string suffix)
    {
        using var temporary = new TemporaryDatabase();
        await temporary.PrepareAsync([]);
        await File.WriteAllTextAsync(temporary.Path + suffix, "sidecar-sintetico");
        var before = await File.ReadAllBytesAsync(temporary.Path);
        var output = new StringWriter();
        Assert.Equal(1, await DiagnosticCommand.ExecuteAsync(["--database-copy", temporary.Path], output, new StringWriter()));
        Assert.Empty(output.ToString());
        Assert.Equal(before, await File.ReadAllBytesAsync(temporary.Path));
        Assert.Equal("sidecar-sintetico", await File.ReadAllTextAsync(temporary.Path + suffix));
    }

    [Fact]
    public async Task RecusaModoWalMesmoAposCheckpointESemSidecars()
    {
        using var temporary = new TemporaryDatabase();
        await temporary.PrepareAsync([]);
        await using (var connection = new SqliteConnection($"Data Source={temporary.Path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL";
            Assert.Equal("wal", await command.ExecuteScalarAsync());
        }
        Assert.Equal(new[] { temporary.Path }, Directory.GetFiles(temporary.DirectoryPath));
        var before = await File.ReadAllBytesAsync(temporary.Path);
        Assert.Equal(1, await DiagnosticCommand.ExecuteAsync(["--database-copy", temporary.Path], new StringWriter(), new StringWriter()));
        Assert.Equal(before, await File.ReadAllBytesAsync(temporary.Path));
        Assert.Equal(new[] { temporary.Path }, Directory.GetFiles(temporary.DirectoryPath));
    }

    [Fact]
    public async Task ExigeInvocacaoExplicitaECaminhoAbsoluto()
    {
        Assert.Equal(1, await DiagnosticCommand.ExecuteAsync([], new StringWriter(), new StringWriter()));
        Assert.Equal(1, await DiagnosticCommand.ExecuteAsync(["--database-copy", "relativo.db"], new StringWriter(), new StringWriter()));
    }

    private static async Task<string[]> SnapshotAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        await connection.OpenAsync();
        var rows = new List<string>();
        foreach (var sql in new[]
        {
            "SELECT type, name, tbl_name, sql FROM sqlite_schema ORDER BY type, name",
            "SELECT Id, Email, EmailNormalizado FROM Usuarios ORDER BY Id"
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add(JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue).ToArray()));
        }
        return rows.ToArray();
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        public string DirectoryPath { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tjg-email-tests-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(DirectoryPath, "synthetic.db");

        public TemporaryDatabase() => Directory.CreateDirectory(DirectoryPath);

        public async Task PrepareAsync(string[] emails)
        {
            await using var database = await HistoricalEmailDatabase.CreateAsync(Path);
            await database.MigrateAsync(HistoricalEmailDatabase.ProfileMigration);
            foreach (var email in emails) await database.InsertLegacyAsync(email);
            await database.MigrateAsync();
            // Exclusivamente preparação do banco sintético: EF habilita WAL na criação.
            // Entregar ao leitor uma cópia standalone em modo rollback-journal.
            await using var command = database.Connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=DELETE";
            Assert.Equal("delete", await command.ExecuteScalarAsync());
        }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
