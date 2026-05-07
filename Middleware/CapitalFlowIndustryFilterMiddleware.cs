using System.Text.Json;

namespace 估值助手.Middleware;

public sealed class CapitalFlowIndustryFilterMiddleware
{
    private static readonly string[] ExcludedNameParts =
    {
        "概念",
        "新高",
        "近期新高",
        "百日新高",
        "历史新高",
        "昨日涨停",
        "昨日连板",
        "连板",
        "融资",
        "融券",
        "沪股通",
        "深股通",
        "陆股通",
        "预盈",
        "预增",
        "预亏",
        "预减",
        "次新",
        "高送转",
        "机构重仓",
        "MSCI",
        "富时罗素",
        "中特估",
        "ST",
        "小米",
        "华为",
        "苹果",
        "周期股",
        "最近多板",
        "反内卷"
    };

    private readonly RequestDelegate _next;

    public CapitalFlowIndustryFilterMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsCapitalFlowRequest(context))
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            buffer.Position = 0;
            if (!ShouldFilterResponse(context))
            {
                context.Response.Body = originalBody;
                await buffer.CopyToAsync(originalBody);
                return;
            }

            using var document = await JsonDocument.ParseAsync(buffer);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                buffer.Position = 0;
                context.Response.Body = originalBody;
                await buffer.CopyToAsync(originalBody);
                return;
            }

            await using var filteredBody = new MemoryStream();
            await using (var writer = new Utf8JsonWriter(filteredBody))
            {
                writer.WriteStartArray();
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    var name = GetName(item);
                    if (!ShouldExclude(name))
                    {
                        item.WriteTo(writer);
                    }
                }
                writer.WriteEndArray();
            }

            filteredBody.Position = 0;
            context.Response.Body = originalBody;
            context.Response.Headers["X-Capital-Flow-Filter"] = "industry";
            context.Response.ContentLength = filteredBody.Length;
            await filteredBody.CopyToAsync(originalBody);
        }
        catch
        {
            buffer.Position = 0;
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool IsCapitalFlowRequest(HttpContext context)
    {
        return context.Request.Path.Equals("/api/fund/capital-flow", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldFilterResponse(HttpContext context)
    {
        if (context.Response.StatusCode != StatusCodes.Status200OK) return false;

        var contentType = context.Response.ContentType ?? string.Empty;
        return contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetName(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return string.Empty;
        if (item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            return name.GetString() ?? string.Empty;
        }
        if (item.TryGetProperty("Name", out var upperName) && upperName.ValueKind == JsonValueKind.String)
        {
            return upperName.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static bool ShouldExclude(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;

        return ExcludedNameParts.Any(part =>
            name.Contains(part, StringComparison.OrdinalIgnoreCase));
    }
}
