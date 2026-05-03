using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using 估值助手.Models;

namespace 估值助手.Controllers
{
    [ApiController]
    [Route("api/fund/ui-state")]
    public sealed class UserUiStateController : ControllerBase
    {
        private readonly AppDbContext _context;

        public UserUiStateController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAll([FromQuery] string username)
        {
            var user = NormalizeUsername(username);
            if (user == null) return Unauthorized(new { success = false, message = "请提供用户名" });

            var rows = await _context.UserUiStates
                .AsNoTracking()
                .Where(x => x.Username == user)
                .ToListAsync();

            var states = new Dictionary<string, object?>();
            foreach (var row in rows)
            {
                states[row.StateKey] = DeserializeState(row.StateJson);
            }

            return Ok(new { success = true, username = user, states });
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string username, [FromQuery] string key)
        {
            var user = NormalizeUsername(username);
            var stateKey = NormalizeKey(key);
            if (user == null) return Unauthorized(new { success = false, message = "请提供用户名" });
            if (stateKey == null) return BadRequest(new { success = false, message = "请提供状态 key" });

            var row = await _context.UserUiStates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == user && x.StateKey == stateKey);

            return Ok(new { success = true, key = stateKey, state = row == null ? null : DeserializeState(row.StateJson), updatedAt = row?.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss") });
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] SaveUiStateRequest request)
        {
            var user = NormalizeUsername(request.Username);
            var stateKey = NormalizeKey(request.Key ?? request.StateKey);
            if (user == null) return Unauthorized(new { success = false, message = "请提供用户名" });
            if (stateKey == null) return BadRequest(new { success = false, message = "请提供状态 key" });

            var now = DateTime.UtcNow.AddHours(8);
            var stateJson = request.State.ValueKind == JsonValueKind.Undefined
                ? (request.StateJson ?? "{}")
                : request.State.GetRawText();

            var row = await _context.UserUiStates
                .FirstOrDefaultAsync(x => x.Username == user && x.StateKey == stateKey);

            if (row == null)
            {
                row = new UserUiState
                {
                    Username = user,
                    StateKey = stateKey
                };
                _context.UserUiStates.Add(row);
            }

            row.StateJson = string.IsNullOrWhiteSpace(stateJson) ? "{}" : stateJson;
            row.UpdatedAt = now;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, key = stateKey, updatedAt = now.ToString("yyyy-MM-dd HH:mm:ss") });
        }

        [HttpDelete]
        public async Task<IActionResult> Delete([FromQuery] string username, [FromQuery] string key)
        {
            var user = NormalizeUsername(username);
            var stateKey = NormalizeKey(key);
            if (user == null) return Unauthorized(new { success = false, message = "请提供用户名" });
            if (stateKey == null) return BadRequest(new { success = false, message = "请提供状态 key" });

            var rows = _context.UserUiStates.Where(x => x.Username == user && x.StateKey == stateKey);
            _context.UserUiStates.RemoveRange(rows);
            var deleted = await _context.SaveChangesAsync();
            return Ok(new { success = true, deleted });
        }

        private static object? DeserializeState(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<object>(json);
            }
            catch
            {
                return json;
            }
        }

        private static string? NormalizeUsername(string? username)
        {
            var value = (username ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string? NormalizeKey(string? key)
        {
            var value = (key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value)) return null;
            return value.Length <= 80 ? value : value[..80];
        }

        public sealed class SaveUiStateRequest
        {
            public string Username { get; set; } = string.Empty;
            public string? Key { get; set; }
            public string? StateKey { get; set; }
            public JsonElement State { get; set; }
            public string? StateJson { get; set; }
        }
    }
}
