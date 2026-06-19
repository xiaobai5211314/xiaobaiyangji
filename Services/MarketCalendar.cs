namespace 小白养基.Services
{
    public static class MarketCalendar
    {
        // Source: Shanghai Stock Exchange 2026 holiday notice, published 2025-12-22:
        // https://www.sse.com.cn/disclosure/announcement/general/c/c_20251222_10802507.shtml
        public static readonly IReadOnlySet<string> AShareClosedDates = new HashSet<string>(StringComparer.Ordinal)
        {
            "2026-01-01", "2026-01-02", "2026-01-03",
            "2026-02-15", "2026-02-16", "2026-02-17", "2026-02-18", "2026-02-19", "2026-02-20", "2026-02-21", "2026-02-22", "2026-02-23",
            "2026-04-04", "2026-04-05", "2026-04-06",
            "2026-05-01", "2026-05-02", "2026-05-03", "2026-05-04", "2026-05-05",
            "2026-06-19", "2026-06-20", "2026-06-21",
            "2026-09-25", "2026-09-26", "2026-09-27",
            "2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04", "2026-10-05", "2026-10-06", "2026-10-07"
        };

        // Source: HKEX Hong Kong Securities Market Holiday Schedule for Year 2026.
        public static readonly IReadOnlySet<string> HkShareClosedDates = new HashSet<string>(StringComparer.Ordinal)
        {
            "2026-01-01",
            "2026-02-17", "2026-02-18", "2026-02-19",
            "2026-04-03", "2026-04-06", "2026-04-07",
            "2026-05-01", "2026-05-25",
            "2026-06-19",
            "2026-07-01",
            "2026-10-01", "2026-10-19",
            "2026-12-25"
        };

        public static readonly IReadOnlySet<string> UsShareClosedDates = new HashSet<string>(StringComparer.Ordinal);

        public static IReadOnlySet<string> ClosedDatesFor(string market)
            => market switch
            {
                "hk" => HkShareClosedDates,
                "us" => UsShareClosedDates,
                _ => AShareClosedDates
            };

        public static bool IsTradingDay(DateTime date, string market = "cn")
        {
            var closedDates = ClosedDatesFor(market);
            return date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                   && !closedDates.Contains(date.ToString("yyyy-MM-dd"));
        }

        public static DateTime GetPreviousTradingDate(DateTime date, string market = "cn")
        {
            var cursor = date.Date;
            while (!IsTradingDay(cursor, market))
            {
                cursor = cursor.AddDays(-1);
            }
            return cursor;
        }

        public static DateTime GetNextTradingDate(DateTime date, string market = "cn")
        {
            var cursor = date.Date;
            while (!IsTradingDay(cursor, market))
            {
                cursor = cursor.AddDays(1);
            }
            return cursor;
        }

        public static DateTime AddTradingDays(DateTime date, int tradingDays, string market = "cn")
        {
            if (tradingDays < 0) throw new ArgumentOutOfRangeException(nameof(tradingDays));

            var cursor = GetNextTradingDate(date, market);
            for (var i = 0; i < tradingDays; i++)
            {
                cursor = GetNextTradingDate(cursor.AddDays(1), market);
            }
            return cursor;
        }
    }
}
