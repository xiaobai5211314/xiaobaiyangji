using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using 估值助手.Models;

namespace 估值助手.Controllers
{
    [ApiController]
    [Route("api/fund/insight-snapshots")]
    public sealed class InsightSnapshotsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public InsightSnapshotsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("latest")]
        public async Task<IActionResult> GetLatest([FromQuery] string username, [FromQuery] string type)
        {
            var user = NormalizeUsername(username);
            var snapshotType = NormalizeType(type);
            if (user == null) return Unauthorized(new { success = false, message = "请提供用户名" });
            if (snapshotType == null) return BadRequest(new { success = false, message = "请提供快照类型" });

            var row = await _context.UserInsightSnapshots
                .AsNoTracking()
                .Where(x => x.Username == user && x.SnapshotType == snapshotType)
                .OrderByDescending(x => x.SnapshotDate)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                success = true,
                type = snapshotType,
                snapshotDate = row?.SnapshotDate.ToString("yyyy-MM-dd"),
                payload = row == null ? null : DeserializePayload(row.PayloadJson),
                updatedAt = row?.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] SaveInsightSnapshotRequest request)
        {
            var user = NormalizeUsername(request.Username);
            var snapshotType = NormalizeType(request.SnapshotType ?? request.Type);
            if (user == null) return Unauthorized(new { success = false, message = "请提供用户名" });
            if (snapshotType == null) return BadRequest(new { success = false, message = "请提供快照类型" });

            var snapshotDate = ParseDate(request.SnapshotDate) ?? DateTime.UtcNow.AddHours(8).Date;
            var payloadJson = request.Payload.ValueKind == JsonValueKind.Undefined
                ? (request.PayloadJson ?? "{}")
                : request.Payload.GetRawText();
            var now = DateTime.UtcNow.AddHours(8);

            var row = await _context.UserInsightSnapshots
                .FirstOrDefaultAsync(x => x.Username == user && x.SnapshotType == snapshotType && x.SnapshotDate == snapshotDate);

            if (row == null)
            {
                row = new UserInsightSnapshot
                {
                    Username = user,
                    SnapshotType = snapshotType,
                    SnapshotDate = snapshotDate,
                    CreatedAt = now
                };
                _context.UserInsightSnapshots.Add(row);
            }

            row.PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
            row.UpdatedAt = now;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, type = snapshotType, snapshotDate = snapshotDate.ToString("yyyy-MM-dd"), updatedAt = now.ToString("yyyy-MM-dd HH:mm:ss") });
        }

        private static object? DeserializePayload(string json)
        {
            try { return JsonSerializer.Deserialize<object>(json); }
            catch { return json; }
        }

        private static DateTime? ParseDate(string? value)
        {
            if (DateTime.TryParse(value, out var date)) return date.Date;
            return null;
        }

        private static string? NormalizeUsername(string? username)
        {
            var value = (username ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string? NormalizeType(string? type)
        {
            var value = (type ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value)) return null;
            return value.Length <= 40 ? value : value[..40];
        }

        public sealed class SaveInsightSnapshotRequest
        {
            public string Username { get; set; } = string.Empty;
            public string? Type { get; set; }
            public string? SnapshotType { get; set; }
            public string? SnapshotDate { get; set; }
            public JsonElement Payload { get; set; }
            public string? PayloadJson { get; set; }
        }
    }
}
