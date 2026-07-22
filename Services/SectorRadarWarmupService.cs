using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace 小白养基.Services;

/// <summary>
/// 板块基金雷达定时预热托管服务。
/// 通过 HttpClient 自 ping 本进程端点 GET {SelfBaseUrl}/api/fund/sectors?force=true，
/// 100% 复用 GetSectors 的全部构建 + 写回逻辑（Redis/内存/DB 缓存），零改动控制器。
/// 默认直连 http://127.0.0.1:7084（部署监听地址，绕开 nginx）。
/// 503 "refreshing" 属并发刷新中，属正常，本周期跳过；单次失败不崩溃，下周期重试。
/// </summary>
public sealed class SectorRadarWarmupService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SectorRadarWarmupService> _logger;

    public SectorRadarWarmupService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<SectorRadarWarmupService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动即预热一次，使 app 启动即有热数据。
        await WarmUpOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SectorRadarScheduleHelper.GetWarmupInterval(), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await WarmUpOnceAsync(stoppingToken);
        }
    }

    private async Task WarmUpOnceAsync(CancellationToken stoppingToken)
    {
        var baseUrl = _config["SelfBaseUrl"]?.TrimEnd('/') ?? "http://127.0.0.1:7084";
        var url = $"{baseUrl}/api/fund/sectors?force=true";
        try
        {
            using var client = _httpClientFactory.CreateClient("SectorWarmup");
            using var resp = await client.GetAsync(url, stoppingToken);
            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("[板块预热] 板块雷达缓存已刷新 ({StatusCode})", (int)resp.StatusCode);
            }
            else
            {
                // 503 "refreshing" 是并发刷新中，属正常，跳过本周期。
                _logger.LogWarning("[板块预热] 预热端点返回 {StatusCode}，本周期跳过", (int)resp.StatusCode);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭。
        }
        catch (Exception ex)
        {
            // 单次失败不崩溃，下周期重试。
            Console.WriteLine($"[警告] 板块雷达预热失败: {ex.Message}");
        }

        // 大盘指数（全球大盘雷达）预热：复用 GET /api/fund/global-indices 的
        // Redis(api:fund:global-indices:v1) + DB(global_indices_1y_v2) 多级缓存重建逻辑，
        // 公开接口、无需 token，force=true 确保真正重建而非命中缓存。
        var indicesUrl = $"{baseUrl}/api/fund/global-indices?force=true";
        try
        {
            using var indicesClient = _httpClientFactory.CreateClient("SectorWarmup");
            using var indicesResp = await indicesClient.GetAsync(indicesUrl, stoppingToken);
            if (indicesResp.IsSuccessStatusCode)
            {
                _logger.LogInformation("[指数预热] 大盘指数缓存已刷新 ({StatusCode})", (int)indicesResp.StatusCode);
            }
            else
            {
                // 503 "refreshing" 是并发刷新中，属正常，跳过本周期。
                _logger.LogWarning("[指数预热] 大盘指数预热端点返回 {StatusCode}，本周期跳过", (int)indicesResp.StatusCode);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭。
        }
        catch (Exception ex)
        {
            // 单次失败不崩溃，下周期重试。
            Console.WriteLine($"[警告] 大盘指数预热失败: {ex.Message}");
        }

        // 资讯(7x24快讯)预热：复用 GET /api/fund/news 的 NewsV3 多级缓存重建逻辑。
        // 仅预热公开 global 模式(username 为空)，holding 模式依赖 username 由前端缓存兜底。
        var newsUrl = $"{baseUrl}/api/fund/news?mode=global&force=true";
        try
        {
            using var newsClient = _httpClientFactory.CreateClient("SectorWarmup");
            using var newsResp = await newsClient.GetAsync(newsUrl, stoppingToken);
            if (newsResp.IsSuccessStatusCode)
            {
                _logger.LogInformation("[资讯预热] 资讯缓存已刷新 ({StatusCode})", (int)newsResp.StatusCode);
            }
            else
            {
                // 503 "refreshing" 是并发刷新中，属正常，跳过本周期。
                _logger.LogWarning("[资讯预热] 资讯预热端点返回 {StatusCode}，本周期跳过", (int)newsResp.StatusCode);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭。
        }
        catch (Exception ex)
        {
            // 单次失败不崩溃，下周期重试。
            Console.WriteLine($"[警告] 资讯预热失败: {ex.Message}");
        }
    }
}
