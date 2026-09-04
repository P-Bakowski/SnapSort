using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SnapSort.App.Models;

namespace SnapSort.App.Services;

public sealed class PhotoIndex
{
    public const int EmbeddingAnalysisVersion = 1;
    public const int BlurAnalysisVersion = 1;
    public const int OrientationAnalysisVersion = 4;

    public PhotoIndex()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Photo (
                FullPath TEXT PRIMARY KEY,
                FileName TEXT NOT NULL,
                FileSize INTEGER NOT NULL,
                Width INTEGER NOT NULL DEFAULT 0,
                Height INTEGER NOT NULL DEFAULT 0,
                DateTaken TEXT NULL,
                FileModified TEXT NOT NULL,
                Sha256 TEXT NULL,
                PerceptualHash TEXT NULL,
                EmbeddingJson TEXT NULL,
                SharpnessScore REAL NULL,
                QualityScore INTEGER NULL,
                Orientation INTEGER NOT NULL DEFAULT 1,
                OrientationConfidence REAL NOT NULL DEFAULT 0,
                SecondBestOrientationConfidence REAL NOT NULL DEFAULT 0,
                SuggestedRotation INTEGER NOT NULL DEFAULT 0,
                AnalysisVersion INTEGER NOT NULL DEFAULT 1,
                EmbeddingAnalysisVersion INTEGER NOT NULL DEFAULT 0,
                BlurAnalysisVersion INTEGER NOT NULL DEFAULT 0,
                OrientationAnalysisVersion INTEGER NOT NULL DEFAULT 0,
                OrientationAcceptedFingerprint TEXT NULL,
                LastAnalyzed TEXT NULL,
                FileFingerprint TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS SimilarityGroup (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FolderPath TEXT NOT NULL,
                Type TEXT NOT NULL,
                Confidence REAL NOT NULL
            );
            CREATE TABLE IF NOT EXISTS SimilarityGroupPhoto (
                GroupId INTEGER NOT NULL,
                PhotoPath TEXT NOT NULL,
                SimilarityScore REAL NOT NULL,
                IsSuggestedBest INTEGER NOT NULL,
                PRIMARY KEY (GroupId, PhotoPath)
            );
            CREATE TABLE IF NOT EXISTS OperationsHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Operation TEXT NOT NULL,
                SourcePath TEXT NOT NULL,
                DestinationPath TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                CanUndo INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "Photo", "EmbeddingJson", "TEXT NULL");
        EnsureColumn(connection, "Photo", "Orientation", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "Photo", "OrientationConfidence", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Photo", "SecondBestOrientationConfidence", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Photo", "SuggestedRotation", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Photo", "AnalysisVersion", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "Photo", "EmbeddingAnalysisVersion", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Photo", "BlurAnalysisVersion", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Photo", "OrientationAnalysisVersion", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Photo", "OrientationAcceptedFingerprint", "TEXT NULL");

        using var migrate = connection.CreateCommand();
        migrate.CommandText = """
            UPDATE Photo SET
                EmbeddingAnalysisVersion = $embedding,
                BlurAnalysisVersion = $blur
            WHERE AnalysisVersion >= 3
              AND LastAnalyzed IS NOT NULL
              AND EmbeddingAnalysisVersion = 0
              AND BlurAnalysisVersion = 0;
            """;
        migrate.Parameters.AddWithValue("$embedding", EmbeddingAnalysisVersion);
        migrate.Parameters.AddWithValue("$blur", BlurAnalysisVersion);
        migrate.ExecuteNonQuery();
    }

    public void UpsertFile(PhotoItem photo)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Photo (FullPath, FileName, FileSize, FileModified, FileFingerprint)
            VALUES ($path, $name, $size, $modified, $fingerprint)
            ON CONFLICT(FullPath) DO UPDATE SET
                FileName = excluded.FileName,
                FileSize = excluded.FileSize,
                FileModified = excluded.FileModified,
                FileFingerprint = excluded.FileFingerprint;
            """;
        command.Parameters.AddWithValue("$path", photo.FullPath);
        command.Parameters.AddWithValue("$name", photo.FileName);
        command.Parameters.AddWithValue("$size", photo.FileSize);
        command.Parameters.AddWithValue("$modified", photo.ModifiedAt.ToString("O"));
        command.Parameters.AddWithValue("$fingerprint", Fingerprint(photo.FullPath));
        command.ExecuteNonQuery();
    }

    public AnalysisState GetAnalysisState(PhotoItem photo)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Sha256, PerceptualHash, SharpnessScore, QualityScore, Width, Height, DateTaken,
                   EmbeddingJson, Orientation, OrientationConfidence, SecondBestOrientationConfidence,
                   SuggestedRotation, EmbeddingAnalysisVersion, BlurAnalysisVersion, OrientationAnalysisVersion,
                   OrientationAcceptedFingerprint
            FROM Photo
            WHERE FullPath = $path
              AND FileFingerprint = $fingerprint
              AND LastAnalyzed IS NOT NULL;
            """;
        command.Parameters.AddWithValue("$path", photo.FullPath);
        command.Parameters.AddWithValue("$fingerprint", Fingerprint(photo.FullPath));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return new AnalysisState(null, true, true, true, false);

        DateTime? dateTaken = null;
        if (!reader.IsDBNull(6) && DateTime.TryParse(reader.GetString(6), out var parsed))
            dateTaken = parsed;

        double[]? embedding = null;
        if (!reader.IsDBNull(7))
            embedding = JsonSerializer.Deserialize<double[]>(reader.GetString(7));

        var needsEmbedding = reader.GetInt32(12) != EmbeddingAnalysisVersion;
        var needsBlur = reader.GetInt32(13) != BlurAnalysisVersion;
        var needsOrientation = reader.GetInt32(14) != OrientationAnalysisVersion;
        var result = new AnalysisResult(
            photo.FullPath,
            reader.IsDBNull(0) ? "" : reader.GetString(0),
            reader.IsDBNull(1) ? "" : reader.GetString(1),
            reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
            reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            dateTaken,
            embedding,
            reader.GetInt32(8),
            needsOrientation ? 0 : reader.GetDouble(9),
            needsOrientation ? 0 : reader.GetDouble(10),
            needsOrientation ? 0 : reader.GetInt32(11));
        var orientationAccepted = !reader.IsDBNull(15)
            && reader.GetString(15).Equals(Fingerprint(photo.FullPath), StringComparison.Ordinal);
        return new AnalysisState(result, needsEmbedding, needsBlur, needsOrientation, orientationAccepted);
    }

    public AnalysisResult? TryGetCurrentAnalysis(PhotoItem photo)
    {
        var state = GetAnalysisState(photo);
        return state.NeedsAnalysis ? null : state.Cached;
    }

    public void AddHistory(string operation, string sourcePath, string destinationPath)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO OperationsHistory (Operation, SourcePath, DestinationPath, CreatedAt, CanUndo)
            VALUES ($operation, $source, $destination, $created, 1);
            """;
        command.Parameters.AddWithValue("$operation", operation);
        command.Parameters.AddWithValue("$source", sourcePath);
        command.Parameters.AddWithValue("$destination", destinationPath);
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void AcceptOrientation(PhotoItem photo)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Photo SET OrientationAcceptedFingerprint = $fingerprint WHERE FullPath = $path;";
        command.Parameters.AddWithValue("$path", photo.FullPath);
        command.Parameters.AddWithValue("$fingerprint", Fingerprint(photo.FullPath));
        command.ExecuteNonQuery();
    }

    public (long Id, string Source, string Destination)? LastUndoableMove()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SourcePath, DestinationPath
            FROM OperationsHistory
            WHERE Operation = 'MoveToTrash' AND CanUndo = 1
            ORDER BY Id DESC
            LIMIT 1;
            """;
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt64(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    public void MarkUndone(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE OperationsHistory SET CanUndo = 0 WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SaveAnalysis(
        AnalysisResult result,
        bool embeddingAnalyzed = true,
        bool blurAnalyzed = true,
        bool orientationAnalyzed = true)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Photo SET
                Width = $width,
                Height = $height,
                DateTaken = $dateTaken,
                Sha256 = $sha,
                PerceptualHash = $phash,
                EmbeddingJson = $embedding,
                SharpnessScore = $sharpness,
                QualityScore = $quality,
                Orientation = $orientation,
                OrientationConfidence = $orientationConfidence,
                SecondBestOrientationConfidence = $secondBestOrientationConfidence,
                SuggestedRotation = $suggestedRotation,
                AnalysisVersion = 4,
                EmbeddingAnalysisVersion = CASE WHEN $embeddingAnalyzed THEN $embeddingVersion ELSE EmbeddingAnalysisVersion END,
                BlurAnalysisVersion = CASE WHEN $blurAnalyzed THEN $blurVersion ELSE BlurAnalysisVersion END,
                OrientationAnalysisVersion = CASE WHEN $orientationAnalyzed THEN $orientationVersion ELSE OrientationAnalysisVersion END,
                FileFingerprint = $fingerprint,
                LastAnalyzed = $analyzed
            WHERE FullPath = $path;
            """;
        command.Parameters.AddWithValue("$path", result.Path);
        command.Parameters.AddWithValue("$width", result.Width);
        command.Parameters.AddWithValue("$height", result.Height);
        command.Parameters.AddWithValue("$dateTaken", result.DateTaken?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$sha", result.Sha256);
        command.Parameters.AddWithValue("$phash", result.PerceptualHash);
        command.Parameters.AddWithValue("$embedding", result.Embedding is null ? DBNull.Value : JsonSerializer.Serialize(result.Embedding));
        command.Parameters.AddWithValue("$sharpness", result.Sharpness);
        command.Parameters.AddWithValue("$quality", result.QualityScore);
        command.Parameters.AddWithValue("$orientation", result.Orientation);
        command.Parameters.AddWithValue("$orientationConfidence", result.OrientationConfidence);
        command.Parameters.AddWithValue("$secondBestOrientationConfidence", result.SecondBestOrientationConfidence);
        command.Parameters.AddWithValue("$suggestedRotation", result.SuggestedRotation);
        command.Parameters.AddWithValue("$embeddingAnalyzed", embeddingAnalyzed);
        command.Parameters.AddWithValue("$blurAnalyzed", blurAnalyzed);
        command.Parameters.AddWithValue("$orientationAnalyzed", orientationAnalyzed);
        command.Parameters.AddWithValue("$embeddingVersion", EmbeddingAnalysisVersion);
        command.Parameters.AddWithValue("$blurVersion", BlurAnalysisVersion);
        command.Parameters.AddWithValue("$orientationVersion", OrientationAnalysisVersion);
        command.Parameters.AddWithValue("$fingerprint", Fingerprint(result.Path));
        command.Parameters.AddWithValue("$analyzed", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static string Fingerprint(string path)
    {
        var info = new FileInfo(path);
        return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }

    private static SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={AppPaths.DbPath}");
        connection.Open();
        return connection;
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
            if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                return;

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }
}

public sealed record AnalysisState(
    AnalysisResult? Cached,
    bool NeedsEmbedding,
    bool NeedsBlur,
    bool NeedsOrientation,
    bool OrientationAccepted)
{
    public bool NeedsAnalysis => NeedsEmbedding || NeedsBlur || NeedsOrientation;
}
