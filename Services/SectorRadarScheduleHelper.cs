using System;

namespace 小白养基.Services;

/// <summary>
/// 板块雷达预热调度纯函数工具。
/// 交易时段判定与 FundController.GetExternalDataFreshTtl 保持一致：
/// 周末 30min / 交易时段(09:25-11:35、12:55-15:10) 2min / 其余 30min。
/// 抽离 IsTradingTime / 可注入时间的 GetWarmupInterval 重载，便于单测确定性校验。
/// </summary>
public static class SectorRadarScheduleHelper
{
    /// <summary>
    /// 依据当前（近似）中国本地时间返回预热周期。供托管服务调用。
    /// </summary>
    public static TimeSpan GetWarmupInterval()
    {
        return GetWarmupInterval(ChinaNow());
    }

    /// <summary>
    /// 依据给定的中国本地时间返回预热周期（可注入时间，供测试直接传参）。
    /// 交易时段 → 2min，其余（含周末、午休、盘后）→ 30min。
    /// </summary>
    public static TimeSpan GetWarmupInterval(DateTime chinaNow)
    {
        return IsTradingTime(chinaNow) ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// 纯函数：依据中国本地时间判定是否处于 A 股交易时段。
    /// 周末（周六/周日）一律视为非交易时段。
    /// 交易时段：09:25-11:35、12:55-15:10（均为闭区间）。
    /// </summary>
    public static bool IsTradingTime(DateTime chinaNow)
    {
        if (chinaNow.DayOfWeek == DayOfWeek.Saturday || chinaNow.DayOfWeek == DayOfWeek.Sunday)
        {
            return false;
        }

        var t = chinaNow.TimeOfDay;
        return (t >= new TimeSpan(9, 25, 0) && t <= new TimeSpan(11, 35, 0))
            || (t >= new TimeSpan(12, 55, 0) && t <= new TimeSpan(15, 10, 0));
    }

    /// <summary>
    /// 近似中国本地时间（UTC+8）。与 FundController.ChinaNow 语义一致。
    /// </summary>
    private static DateTime ChinaNow() => DateTime.UtcNow.AddHours(8);
}
