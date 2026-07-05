using Microsoft.AspNetCore.Mvc;
using Poskus1.DTOs;
using Poskus1.Services;

namespace Poskus1.Controllers
{
    [ApiController]
    [Route("api")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;

        public AuthController(AuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var (success, message) = await _authService.RegisterAsync(dto);

            if (!success)
                return BadRequest(new { message });

            return Ok(new { message });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var (success, token, message) = await _authService.LoginAsync(request.loginPayload);

            if (!success)
                return Unauthorized(new { message });

            return Ok(new { token, message });
        }

        [HttpGet("validate")]
        public async Task<IActionResult> Validate()
        {
            var authHeader = Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
                return Unauthorized(new { valid = false, message = "Token ni bil posredovan." });

            var rawToken = authHeader["Bearer ".Length..].Trim();
            var (valid, info) = await _authService.ValidateTokenAsync(rawToken);

            if (!valid)
                return Unauthorized(new { valid = false, message = "Token je neveljaven ali je potekel." });

            return Ok(new { valid = true, token = info });
        }
    }
}
