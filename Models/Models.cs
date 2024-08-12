using System.Collections.Generic;

namespace UpdateServer.Models
{
    /// <summary>
    /// Represents the model for file ingestion requests.
    /// </summary>
    public class FileIngestModel
    {
        /// <summary>
        /// Gets or sets the unique identifier for the game.
        /// </summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the relative path where the file should be stored.
        /// </summary>
        public string RelativePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the base64 encoded content of the file.
        /// </summary>
        public string FileContent { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents the model for user login requests.
    /// </summary>
    public class LoginModel
    {
        /// <summary>
        /// Gets or sets the username for login.
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the password for login.
        /// </summary>
        public string Password { get; set; } = string.Empty;
    }
}