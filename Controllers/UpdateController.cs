using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using UpdateServer.Models;
using UpdateServer.Services;

namespace UpdateServer.Controllers
{
    /// <summary>
    /// Controller responsible for handling update-related operations.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="UpdateController"/> class.
    /// </remarks>
    /// <param name="fileStorage">The file storage service.</param>
    /// <param name="logger">The logger instance.</param>
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class UpdateController(FileStorageService fileStorage, ILogger<UpdateController> logger) : ControllerBase
    {
        private readonly FileStorageService _fileStorage = fileStorage;
        private readonly ILogger<UpdateController> _logger = logger;

        /// <summary>
        /// Ingests a file into the system.
        /// </summary>
        /// <param name="model">The file ingest model containing file details.</param>
        /// <returns>An IActionResult indicating the success or failure of the operation.</returns>
        [Authorize]
        [HttpPost("ingest")]
        [RequestSizeLimit(5242880000)] // Approximately 5GB
        public async Task<IActionResult> IngestFile([FromBody] FileIngestModel model)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Invalid model state for file ingestion");
                return BadRequest(ModelState);
            }

            try
            {
                // Convert base64 string to byte array
                byte[] fileBytes = Convert.FromBase64String(model.FileContent);
                
                // Create a temporary file
                string tempFilePath = Path.GetTempFileName();
                await System.IO.File.WriteAllBytesAsync(tempFilePath, fileBytes);

                _logger.LogInformation("Ingesting file for game {GameId}: {RelativePath}", model.GameId, model.RelativePath);
                
                // Ingest the file using the FileStorageService
                await _fileStorage.IngestFile(model.GameId, tempFilePath, model.RelativePath);

                // Clean up the temporary file
                System.IO.File.Delete(tempFilePath);

                _logger.LogInformation("File ingested successfully for game {GameId}: {RelativePath}", model.GameId, model.RelativePath);
                return Ok(new { message = "File ingested successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ingesting file for game {GameId}: {RelativePath}", model.GameId, model.RelativePath);
                return StatusCode(500, "An error occurred while ingesting the file");
            }
        }
    }
}