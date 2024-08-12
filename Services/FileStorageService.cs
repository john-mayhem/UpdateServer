using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Serilog;

namespace UpdateServer.Services
{
    /// <summary>
    /// Provides file storage and management services for the Update Server.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the FileStorageService class.
    /// </remarks>
    /// <param name="configuration">The application configuration.</param>
    public class FileStorageService(IConfiguration configuration)
    {
        private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration), "DefaultConnection is not configured.");
        private readonly string _storagePath = configuration["StoragePath"]
                ?? throw new ArgumentNullException(nameof(configuration), "StoragePath is not configured.");

        /// <summary>
        /// Ingests a file into the storage system and updates the database.
        /// </summary>
        /// <param name="gameId">The ID of the game the file belongs to.</param>
        /// <param name="filePath">The current path of the file to ingest.</param>
        /// <param name="relativePath">The relative path where the file should be stored.</param>
        public async Task IngestFile(string gameId, string filePath, string relativePath)
        {
            Log.Information("Ingesting file for game {GameId}: {RelativePath}", gameId, relativePath);
            
            var fileInfo = new FileInfo(filePath);
            var hash = ComputeFileHash(filePath);
            var destinationPath = Path.Combine(_storagePath, "games", gameId, relativePath);

            // Check if file already exists
            if (File.Exists(destinationPath))
            {
                var existingHash = ComputeFileHash(destinationPath);
                if (hash != existingHash)
                {
                    // Move existing file to deprecated folder
                    var deprecatedPath = Path.Combine(_storagePath, "games", "deprecated", gameId,
                        DateTime.Now.ToString("yyyyMMddHHmmss"), relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(deprecatedPath) ?? throw new InvalidOperationException("Invalid deprecated path"));
                    File.Move(destinationPath, deprecatedPath);
                    Log.Information("Existing file moved to deprecated: {DeprecatedPath}", deprecatedPath);
                }
                else
                {
                    Log.Information("File unchanged, skipping update: {RelativePath}", relativePath);
                    return;
                }
            }

            // Create directory if it doesn't exist
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("Invalid destination path"));

            // Copy new file
            File.Copy(filePath, destinationPath, true);
            Log.Information("File copied to: {DestinationPath}", destinationPath);

            // Update database
            await UpdateDatabaseAsync(gameId, relativePath, fileInfo, hash);
        }

        private static string ComputeFileHash(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private async Task UpdateDatabaseAsync(string gameId, string relativePath, FileInfo fileInfo, string hash)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                MERGE INTO Updates_Games AS target
                USING (VALUES (@GameId, @RelativePath, @Name, @Size, @Hash, @UploadDate, @FileChangeDate, @FilePath))
                    AS source (GameId, RelativePath, Name, Size, Hash, UploadDate, FileChangeDate, FilePath)
                ON target.GameId = source.GameId AND target.RelativePath = source.RelativePath
                WHEN MATCHED THEN
                    UPDATE SET 
                        Name = source.Name,
                        Size = source.Size,
                        Hash = source.Hash,
                        UploadDate = source.UploadDate,
                        FileChangeDate = source.FileChangeDate,
                        FilePath = source.FilePath
                WHEN NOT MATCHED THEN
                    INSERT (GameId, RelativePath, Name, Size, Hash, UploadDate, FileChangeDate, FilePath)
                    VALUES (source.GameId, source.RelativePath, source.Name, source.Size, source.Hash, source.UploadDate, source.FileChangeDate, source.FilePath);";

            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@GameId", gameId);
            command.Parameters.AddWithValue("@RelativePath", relativePath);
            command.Parameters.AddWithValue("@Name", fileInfo.Name);
            command.Parameters.AddWithValue("@Size", fileInfo.Length);
            command.Parameters.AddWithValue("@Hash", hash);
            command.Parameters.AddWithValue("@UploadDate", DateTime.UtcNow);
            command.Parameters.AddWithValue("@FileChangeDate", fileInfo.LastWriteTimeUtc);
            command.Parameters.AddWithValue("@FilePath", Path.Combine(_storagePath, "games", gameId, relativePath));

            await command.ExecuteNonQueryAsync();
            Log.Information("Database updated for file: {RelativePath}", relativePath);
        }
    }
}