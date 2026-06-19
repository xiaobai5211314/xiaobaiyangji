using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using 小白养基.Services;

namespace 小白养基.Controllers
{
    [ApiController]
    [Route("api/influencer-posts")]
    public sealed class InfluencerPostsController : ControllerBase
    {
        private readonly InfluencerPostsCacheService _cacheService;

        public InfluencerPostsController(InfluencerPostsCacheService cacheService)
        {
            _cacheService = cacheService;
        }

        [HttpGet("latest")]
        public async Task<IActionResult> GetLatest([FromQuery] int? limit = null)
        {
            var result = await _cacheService.GetLatestAsync(limit);
            Response.Headers[HeaderNames.CacheControl] = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            return Ok(new
            {
                success = true,
                status = result.Status,
                fetchedAt = result.FetchedAt,
                items = result.Items
            });
        }
    }
}
