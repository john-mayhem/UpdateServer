using System;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Threading.Tasks;

namespace UpdateServer.Services
{
    /// <summary>
    /// Provides user validation and authentication services for the Update Server.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the DbUserValidation class.
    /// </remarks>
    /// <param name="configuration">The application configuration.</param>
    public class DbUserValidation(IConfiguration configuration)
    {
        private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        /// <summary>
        /// Validates a user's credentials and updates their last login information.
        /// </summary>
        /// <param name="username">The username to validate.</param>
        /// <param name="password">The password to validate.</param>
        /// <param name="ipAddress">The IP address of the login attempt.</param>
        /// <returns>A tuple indicating if the user is valid and if they are an admin.</returns>
        public async Task<(bool isValid, bool isAdmin)> ValidateUserAsync(string username, string password, string ipAddress)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            try
            {
                await EnsureUserTableColumnsExistAsync(connection);

                string query = "SELECT PasswordHash, IsAdmin FROM Users WHERE Username = @Username";
                await using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@Username", username);
                await using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    string storedHash = reader.GetString(0);
                    bool isAdmin = reader.GetBoolean(1);

                    Log.Information("User found. Username: {Username}, IsAdmin: {IsAdmin}", username, isAdmin);
                    
                    bool isValid = VerifyPassword(password, storedHash);

                    Log.Information("Password validation result: {IsValid}", isValid);

                    if (isValid)
                    {
                        // Close the reader before updating
                        await reader.CloseAsync();
                        await UpdateLastLoginAsync(connection, username, ipAddress);
                    }

                    return (isValid, isAdmin);
                }
                else
                {
                    Log.Warning("No user found with username: {Username}", username);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error validating user: {Username}", username);
            }

            return (false, false);
        }

        private static async Task UpdateLastLoginAsync(SqlConnection connection, string username, string ipAddress)
        {
            string updateQuery = @"
        UPDATE Users 
        SET LastLogin = GETUTCDATE(), LastLoginIP = @IpAddress 
        WHERE Username = @Username";

            try
            {
                await using var command = new SqlCommand(updateQuery, connection);
                command.Parameters.AddWithValue("@Username", username);
                command.Parameters.AddWithValue("@IpAddress", ipAddress);
                int rowsAffected = await command.ExecuteNonQueryAsync();
                Log.Information("Updated last login for user {Username}. Rows affected: {RowsAffected}", username, rowsAffected);
            }
            catch (SqlException ex)
            {
                Log.Error(ex, "SQL Error updating last login for user {Username}. Error number: {ErrorNumber}", username, ex.Number);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating last login for user {Username}", username);
            }
        }

        private static async Task EnsureUserTableColumnsExistAsync(SqlConnection connection)
        {
            string checkColumnsQuery = @"
        SELECT 
            CASE WHEN COL_LENGTH('Users', 'LastLogin') IS NOT NULL THEN 1 ELSE 0 END AS HasLastLogin,
            CASE WHEN COL_LENGTH('Users', 'LastLoginIP') IS NOT NULL THEN 1 ELSE 0 END AS HasLastLoginIP";

            await using var command = new SqlCommand(checkColumnsQuery, connection);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                bool hasLastLogin = reader.GetInt32(0) == 1;
                bool hasLastLoginIP = reader.GetInt32(1) == 1;

                if (!hasLastLogin || !hasLastLoginIP)
                {
                    Log.Warning("Missing columns in Users table. LastLogin: {HasLastLogin}, LastLoginIP: {HasLastLoginIP}", hasLastLogin, hasLastLoginIP);
                    // TODO: Add code here to create the missing columns if necessary
                }
            }
        }

        private static bool VerifyPassword(string password, string storedHash)
        {
            byte[] hashBytes = Convert.FromBase64String(storedHash);
            byte[] salt = new byte[16];
            Array.Copy(hashBytes, 0, salt, 0, 16);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
            byte[] hash = pbkdf2.GetBytes(32);
            for (int i = 0; i < 32; i++)
            {
                if (hashBytes[i + 16] != hash[i])
                    return false;
            }
            return true;
        }
    }
}