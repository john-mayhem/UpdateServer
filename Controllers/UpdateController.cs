using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;
using UpdateServer.Models;
using UpdateServer.Services;
using Microsoft.AspNetCore.Http;

namespace UpdateServer.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class UpdateController(FileStorageService fileStorage, ClientUpdateService clientUpdateService, ILogger<UpdateController> logger) : ControllerBase
    {
        private readonly FileStorageService _fileStorage = fileStorage;
        private readonly ClientUpdateService _clientUpdateService = clientUpdateService;
        private readonly ILogger<UpdateController> _logger = logger;

        [HttpGet("check")]
        public async Task<ActionResult<ClientUpdateInfoModel>> CheckForUpdates([FromQuery] string currentVersion)
        {
            try
            {
                var updateInfo = await _clientUpdateService.GetClientUpdateInfo(currentVersion);
                updateInfo.DownloadUrl = Url.Action("DownloadClient", "Update", new { version = updateInfo.LatestVersion }, Request.Scheme);
                return Ok(updateInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for updates");
                return StatusCode(500, "An error occurred while checking for updates");
            }
        }

        [HttpGet("download")]
        public async Task<IActionResult> DownloadClient([FromQuery] string version)
        {
            try
            {
                var clientFile = await _fileStorage.GetClientFile(version);
                if (clientFile == null)
                {
                    return NotFound("Requested version not found");
                }

                return File(clientFile.OpenReadStream(), "application/octet-stream", clientFile.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading client version {Version}", version);
                return StatusCode(500, "An error occurred while downloading the client");
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("ingest")]
        [RequestSizeLimit(5242880000)] // Approximately 5GB
        [RequestFormLimits(MultipartBodyLengthLimit = 5242880000)]
        public async Task<IActionResult> IngestFile([FromQuery] string gameId, [FromQuery] string relativePath)
        {
            if (!Request.HasFormContentType)
            {
                return BadRequest("Unsupported media type");
            }

            var form = await Request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file == null || file.Length == 0)
            {
                return BadRequest("No file was uploaded");
            }

            try
            {
                using var stream = file.OpenReadStream();
                await _fileStorage.IngestFileStream(gameId, relativePath, stream);
                _logger.LogInformation("File ingested successfully for game {GameId}: {RelativePath}", gameId, relativePath);
                return Ok(new { message = "File ingested successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ingesting file for game {GameId}: {RelativePath}", gameId, relativePath);
                return StatusCode(500, "An error occurred while ingesting the file");
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("ingest-client")]
        [RequestSizeLimit(5242880000)] // Approximately 5GB
        [RequestFormLimits(MultipartBodyLengthLimit = 5242880000)]
        public async Task<IActionResult> IngestClientUpdate()
        {
            try
            {
                if (!Request.HasFormContentType)
                {
                    return BadRequest("Request must be multipart/form-data.");
                }

                var form = await Request.ReadFormAsync();
                var file = form.Files.GetFile("file");
                var version = form["version"].FirstOrDefault();
                var releaseNotes = form["releaseNotes"].FirstOrDefault();

                if (file == null || file.Length == 0)
                {
                    return BadRequest("No file was uploaded");
                }

                if (string.IsNullOrEmpty(version))
                {
                    return BadRequest("Version is required");
                }

                using var stream = file.OpenReadStream();
                var ingestSuccess = await _fileStorage.IngestClientFileStream(version, stream);

                if (!ingestSuccess)
                {
                    return Conflict($"Version {version} already exists. Use a different version number.");
                }

                await _clientUpdateService.AddClientVersion(version, releaseNotes ?? string.Empty);

                _logger.LogInformation("Client update ingested successfully. Version: {Version}", version);
                return Ok(new { message = "Client update ingested successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ingesting client update");
                return StatusCode(500, "An error occurred while ingesting the client update");
            }
        }
    }
}