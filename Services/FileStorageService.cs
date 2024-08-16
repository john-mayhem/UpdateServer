using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Serilog;

namespace UpdateServer.Services
{
    public class FileStorageService(IConfiguration configuration)
    {
        private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
                    ?? throw new ArgumentNullException(nameof(configuration), "DefaultConnection is not configured.");
        private readonly string _storagePath = configuration["StoragePath"]
                    ?? throw new ArgumentNullException(nameof(configuration), "StoragePath is not configured.");

        public async Task IngestFileStream(string gameId, string relativePath, Stream fileStream)
        {
            Log.Information("Ingesting file stream for game {GameId}: {RelativePath}", gameId, relativePath);

            var destinationPath = Path.Combine(_storagePath, "games", gameId, relativePath);

            // Create directory if it doesn't exist
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("Invalid destination path"));

            // Compute hash and copy file
            using (var outputStream = new FileStream(destinationPath, FileMode.Create))
            using (var sha256 = SHA256.Create())
            {
                await fileStream.CopyToAsync(outputStream);
                fileStream.Position = 0;
                var hash = await sha256.ComputeHashAsync(fileStream);
                var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                // Update database
                await UpdateDatabaseAsync(gameId, relativePath, outputStream.Length, hashString, destinationPath);
            }

            Log.Information("File ingested successfully: {DestinationPath}", destinationPath);
        }

        private async Task UpdateDatabaseAsync(string gameId, string relativePath, long fileSize, string hash, string filePath)
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
            command.Parameters.AddWithValue("@Name", Path.GetFileName(relativePath));
            command.Parameters.AddWithValue("@Size", fileSize);
            command.Parameters.AddWithValue("@Hash", hash);
            command.Parameters.AddWithValue("@UploadDate", DateTime.UtcNow);
            command.Parameters.AddWithValue("@FileChangeDate", DateTime.UtcNow);
            command.Parameters.AddWithValue("@FilePath", filePath);

            await command.ExecuteNonQueryAsync();
            Log.Information("Database updated for file: {RelativePath}", relativePath);
        }

        public async Task<bool> IngestClientFileStream(string version, Stream fileStream)
        {
            Log.Information("Ingesting client file stream for version: {Version}", version);
            
            if (await VersionExists(version))
            {
                Log.Warning("Version {Version} already exists. Upload aborted.", version);
                return false;
            }

            var versionFolder = Path.Combine(_storagePath, "clients", version);
            var destinationPath = Path.Combine(versionFolder, "UpdateClient.exe");

            // Create directory if it doesn't exist
            Directory.CreateDirectory(versionFolder);

            // Compute hash and copy file
            using (var outputStream = new FileStream(destinationPath, FileMode.Create))
            using (var sha256 = SHA256.Create())
            {
                await fileStream.CopyToAsync(outputStream);
                fileStream.Position = 0;
                var hash = await sha256.ComputeHashAsync(fileStream);
                var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                // Update database
                await UpdateClientDatabaseAsync(version, "UpdateClient.exe", outputStream.Length, hashString, destinationPath);
            }

            Log.Information("Client file ingested successfully: {DestinationPath}", destinationPath);
            return true;
        }

        private async Task<bool> VersionExists(string version)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = "SELECT COUNT(*) FROM Updates_Client WHERE Version = @Version";
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Version", version);

            var result = await command.ExecuteScalarAsync();

            // Safely convert the result to int
            return result != null && Convert.ToInt32(result) > 0;
        }

        private async Task UpdateClientDatabaseAsync(string version, string name, long fileSize, string hash, string filePath)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                INSERT INTO Updates_Client (Version, Name, Size, Hash, UploadDate, FilePath)
                VALUES (@Version, @Name, @Size, @Hash, @UploadDate, @FilePath)";

            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Version", version);
            command.Parameters.AddWithValue("@Name", name);
            command.Parameters.AddWithValue("@Size", fileSize);
            command.Parameters.AddWithValue("@Hash", hash);
            command.Parameters.AddWithValue("@UploadDate", DateTime.UtcNow);
            command.Parameters.AddWithValue("@FilePath", filePath);

            await command.ExecuteNonQueryAsync();
            Log.Information("Database updated for client version: {Version}", version);
        }
        
        public async Task<IFormFile?> GetClientFile(string version)
        {
            var filePath = await GetClientFilePath(version);
            if (filePath == null)
            {
                return null;
            }

            var memory = new MemoryStream();
            using (var stream = new FileStream(filePath, FileMode.Open))
            {
                await stream.CopyToAsync(memory);
            }
            memory.Position = 0;

            return new FormFile(memory, 0, memory.Length, "clientFile", Path.GetFileName(filePath));
        }

        private async Task<string?> GetClientFilePath(string version)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = new SqlCommand("SELECT FilePath FROM Updates_Client WHERE Version = @Version", connection);
            command.Parameters.AddWithValue("@Version", version);

            var result = await command.ExecuteScalarAsync();
            return result?.ToString();
        }
    }
}