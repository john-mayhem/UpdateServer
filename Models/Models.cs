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

    /// <summary>
    /// Represents the model for client update information.
    /// </summary>
    public class ClientUpdateInfoModel
    {
        /// <summary>
        /// Gets or sets the latest version of the client.
        /// </summary>
        public string? LatestVersion { get; set; }

        /// <summary>
        /// Gets or sets the download URL for the latest client version.
        /// </summary>
        public string? DownloadUrl { get; set; }

        /// <summary>
        /// Gets or sets the release notes for the latest version.
        /// </summary>
        public string? ReleaseNotes { get; set; }

        /// <summary>
        /// Gets or sets the Hash for the latest version.
        /// </summary>
        public string? FileHash { get; set; } 

        /// <summary>
        /// Gets or sets a value indicating whether the update is mandatory.
        /// </summary>
        public bool IsMandatory { get; set; }
    }

    public class ClientUpdateModel
    {
        public IFormFile File { get; set; } = null!;

        public string Version { get; set; } = string.Empty;

        public string? ReleaseNotes { get; set; }
    }
}