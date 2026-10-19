using System.Text.Json;
using Microsoft.Data.Sqlite;
using TamoJuntoGames.API.Services;

namespace TamoJuntoGames.EmailIdentityDiagnostic;

public sealed record EmailIdentityReport(
    int RegistrosExaminados,
    int QuantidadeDivergencias,
    int QuantidadeGruposColisao,
    bool NecessitaAnaliseManual,
    int[] IdsDivergentes,
    int[][] GruposColisao);

public static class EmailIdentityReader
{
    public static async Task<EmailIdentityReport> ReadAsync(string databaseCopyPath)
    {
        if (!Path.IsPathFullyQualified(databaseCopyPath))
            throw new ArgumentException("É necessário um caminho absoluto.");

        // Open, nunca OpenOrCreate. Mantido aberto durante a leitura; em Windows,
        // FileShare.Read também impede gravações concorrentes convencionais.
        using var file = new FileStream(databaseCopyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = new byte[100];
        if (file.Read(header, 0, header.Length) != header.Length ||
            !header.AsSpan(0, 16).SequenceEqual("SQLite format 3\0"u8))
            throw new InvalidDataException("Formato não suportado.");

        // WAL pode conter dados não presentes no arquivo principal. Não ignorá-lo
        // com immutable=1 nem tentar recuperar/checkpointar journals automaticamente.
        if (header[18] != 1 || header[19] != 1 ||
            new[] { "-wal", "-shm", "-journal" }.Any(suffix => File.Exists(databaseCopyPath + suffix)))
            throw new InvalidDataException("É necessária uma cópia consistente e offline sem journals.");

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseCopyPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Projeção explícita: não ler Senha, nomes, apelidos ou outros dados de perfil.
        // Um único SELECT observa uma visão consistente dos registros.
        command.CommandText = "SELECT u.\"Id\", u.\"Email\", u.\"EmailNormalizado\" FROM \"Usuarios\" AS u ORDER BY u.\"Id\"";
        await using var reader = await command.ExecuteReaderAsync();
        var examined = 0;
        var divergentIds = new List<int>();
        var identities = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2))
                throw new InvalidDataException("Estrutura ou dados incompatíveis.");
            var id = reader.GetInt32(0);
            var identity = EmailNormalizer.ParaIdentidade(reader.GetString(1));
            if (!string.Equals(reader.GetString(2), identity, StringComparison.Ordinal))
                divergentIds.Add(id);
            if (!identities.TryGetValue(identity, out var ids))
                identities.Add(identity, ids = []);
            ids.Add(id);
            examined++;
        }

        var collisions = identities.Values.Where(ids => ids.Count > 1).Select(ids => ids.ToArray()).ToArray();
        return new EmailIdentityReport(examined, divergentIds.Count, collisions.Length,
            divergentIds.Count > 0 || collisions.Length > 0, divergentIds.ToArray(), collisions);
    }
}

public static class DiagnosticCommand
{
    // 0 = sem achados, 2 = análise manual necessária, 1 = não foi possível diagnosticar.
    public static async Task<int> ExecuteAsync(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 2 || args[0] != "--database-copy")
        {
            await error.WriteLineAsync("Uso: --database-copy <caminho absoluto da cópia SQLite consistente e offline>");
            return 1;
        }
        try
        {
            var report = await EmailIdentityReader.ReadAsync(args[1]);
            // Somente contagens/IDs técnicos; nunca serializar identidades ou exceções.
            await output.WriteLineAsync(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return report.NecessitaAnaliseManual ? 2 : 0;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or InvalidCastException or OverflowException)
        {
            // Mensagem fixa: exceções SQLite/IO podem conter caminhos e valores pessoais.
            await error.WriteLineAsync("Diagnóstico não realizado. Verifique caminho absoluto, permissões, esquema e cópia offline sem WAL/journal. Nenhum reparo foi executado.");
            return 1;
        }
    }
}
