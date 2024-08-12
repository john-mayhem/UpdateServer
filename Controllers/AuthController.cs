using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using System.Text;
using UpdateServer.Services;
using UpdateServer.Models;

namespace UpdateServer.Controllers
{
    /// <summary>
    /// Controller responsible for authentication-related operations.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="AuthController"/> class.
    /// </remarks>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dbUserValidation">The database user validation service.</param>
    [ApiController]
    [Route("[controller]")]
    public class AuthController(IConfiguration configuration, ILogger<AuthController> logger, DbUserValidation dbUserValidation) : ControllerBase
    {
        private readonly IConfiguration _configuration = configuration;
        private readonly ILogger<AuthController> _logger = logger;
        private readonly DbUserValidation _dbUserValidation = dbUserValidation;

        /// <summary>
        /// Authenticates a user and returns a JWT token upon successful login.
        /// </summary>
        /// <param name="login">The login model containing username and password.</param>
        /// <returns>An IActionResult containing the JWT token or an Unauthorized response.</returns>
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginModel login)
        {
            _logger.LogInformation("Login attempt for user: {Username}", login.Username);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var (isValid, isAdmin) = await _dbUserValidation.ValidateUserAsync(login.Username, login.Password, ipAddress);

            if (isValid)
            {
                var token = GenerateJwtToken(login.Username, isAdmin);
                _logger.LogInformation("Login successful for user: {Username}", login.Username);
                return Ok(new { token });
            }

            _logger.LogWarning("Login failed for user: {Username}", login.Username);
            return Unauthorized("Invalid username or password");
        }

        /// <summary>
        /// Generates a JWT token for anonymous clients.
        /// </summary>
        /// <returns>An IActionResult containing the generated JWT token.</returns>
        [AllowAnonymous]
        [HttpPost("token")]
        public IActionResult GetToken()
        {
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var token = GenerateJwtToken($"Client-{Guid.NewGuid()}", false);
            _logger.LogInformation("Token generated for client IP: {ClientIp}", clientIp);
            return Ok(new { token });
        }

        /// <summary>
        /// Generates a JWT token for the given client ID and admin status.
        /// </summary>
        /// <param name="clientId">The client ID to include in the token.</param>
        /// <param name="isAdmin">A boolean indicating whether the client is an admin.</param>
        /// <returns>A string containing the generated JWT token.</returns>
        private string GenerateJwtToken(string clientId, bool isAdmin)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Secret"] ?? throw new InvalidOperationException("JWT secret is not configured."));
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, clientId)
            };
            if (isAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddDays(7), // Token expires after 7 days
                Issuer = _configuration["Jwt:Issuer"],
                Audience = _configuration["Jwt:Audience"],
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}