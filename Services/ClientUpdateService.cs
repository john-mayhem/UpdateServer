using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using UpdateServer.Models;

namespace UpdateServer.Services
{
    public class ClientUpdateService(IConfiguration configuration)
    {
        private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new ArgumentNullException(nameof(configuration), "DefaultConnection is not configured.");

        public async Task<string> GetLatestClientVersion()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = new SqlCommand("SELECT TOP 1 Version FROM Updates_Client ORDER BY UploadDate DESC", connection);
            var result = await command.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
            {
                throw new InvalidOperationException("No client versions found in the database.");
            }

            var versionString = result.ToString();

            if (string.IsNullOrEmpty(versionString))
            {
                throw new InvalidOperationException("Retrieved client version is empty or null.");
            }

            return versionString;
        }


        public async Task<string> GetReleaseNotes(string version)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = new SqlCommand("SELECT ReleaseNotes FROM Updates_Client WHERE Version = @Version", connection);
            command.Parameters.AddWithValue("@Version", version);
            var result = await command.ExecuteScalarAsync();
            return result?.ToString() ?? "No release notes available.";
        }

        public async Task<ClientUpdateInfoModel> GetClientUpdateInfo(string currentVersion)
        {
            try
            {
                var latestVersion = await GetLatestClientVersion();

                return new ClientUpdateInfoModel
                {
                    LatestVersion = latestVersion,
                    ReleaseNotes = await GetReleaseNotes(latestVersion),
                    IsMandatory = latestVersion != currentVersion
                };
            }
            catch (InvalidOperationException ex)
            {
                // Log the exception
                // You might want to inject an ILogger and use it here
                Console.WriteLine($"Error retrieving client update info: {ex.Message}");

                return new ClientUpdateInfoModel
                {
                    LatestVersion = null,
                    ReleaseNotes = "No updates available.",
                    IsMandatory = false
                };
            }
        }

        
        public async Task AddClientVersion(string version, string releaseNotes)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = new SqlCommand(@"
                UPDATE Updates_Client 
                SET ReleaseNotes = @ReleaseNotes
                WHERE Version = @Version", connection);

            command.Parameters.AddWithValue("@Version", version);
            command.Parameters.AddWithValue("@ReleaseNotes", releaseNotes);

            await command.ExecuteNonQueryAsync();
        }
    }
}