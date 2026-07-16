namespace 小白养基.Services
{
    public sealed class SectorCatalogEntry
    {
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string[] Include { get; init; } = Array.Empty<string>();
        public string[] Exclude { get; init; } = Array.Empty<string>();
    }

    public static class SectorFundCatalog
    {
        public const string GroupAll = "all";
        public const string GroupIndex = "index";
        public const string GroupActive = "active";
        public const string GroupMixed = "mixed";
        public const string GroupEquity = "equity";
        public const string GroupOther = "other";

        public static IReadOnlyList<SectorCatalogEntry> Definitions { get; } = new List<SectorCatalogEntry>
        {
            new() { Key = "gold", Name = "黄金", Include = new[] { "黄金", "上海金", "黄金ETF", "黄金基金" }, Exclude = new[] { "黄金股" } },
            new() { Key = "gold_stock", Name = "黄金股", Include = new[] { "黄金股", "贵金属" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "nonferrous", Name = "有色金属", Include = new[] { "有色", "有色金属", "工业有色", "资源产业", "矿业" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "rare_earth", Name = "稀土永磁", Include = new[] { "稀土", "永磁", "稀有金属" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "lithium", Name = "锂矿锂电", Include = new[] { "锂矿", "锂电", "锂电池", "锂产业" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "steel", Name = "钢铁", Include = new[] { "钢铁" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "coal", Name = "煤炭", Include = new[] { "煤炭", "煤炭产业" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "oil_gas", Name = "油气", Include = new[] { "油气", "石油", "原油", "天然气" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "chemical", Name = "化工", Include = new[] { "化工", "化学原料", "化学制品", "基础化工" }, Exclude = new[] { "债", "货币" } },

            new() { Key = "new_energy", Name = "新能源", Include = new[] { "新能源", "碳中和", "清洁能源" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "pv", Name = "光伏", Include = new[] { "光伏", "太阳能" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "wind", Name = "风电", Include = new[] { "风电", "风能" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "storage", Name = "储能", Include = new[] { "储能" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "solid_battery", Name = "固态电池", Include = new[] { "固态电池" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "battery", Name = "电池", Include = new[] { "电池", "电池产业" }, Exclude = new[] { "债", "货币", "固态电池" } },
            new() { Key = "hydrogen", Name = "氢能", Include = new[] { "氢能", "氢能源", "燃料电池" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "grid", Name = "电网设备", Include = new[] { "电网", "智能电网", "电力设备" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "power", Name = "电力", Include = new[] { "电力", "公用事业", "绿色电力" }, Exclude = new[] { "债", "货币", "电力设备" } },
            new() { Key = "nuclear_power", Name = "核电", Include = new[] { "核电", "核能" }, Exclude = new[] { "债", "货币", "核聚变" } },
            new() { Key = "nuclear", Name = "可控核聚变", Include = new[] { "核聚变", "可控核聚变" }, Exclude = new[] { "债", "货币" } },

            new() { Key = "semiconductor", Name = "半导体", Include = new[] { "半导体", "芯片", "集成电路", "科创芯片" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "semi_material", Name = "半导体材料设备", Include = new[] { "半导体材料", "半导体设备", "芯片设备", "芯片材料" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "pcb", Name = "PCB", Include = new[] { "PCB", "印制电路", "电路板" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "cpo", Name = "CPO光模块", Include = new[] { "CPO", "光模块", "光通信" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "communication", Name = "通信", Include = new[] { "通信", "通信设备", "5G", "信息通信" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "consumer_electronics", Name = "消费电子", Include = new[] { "消费电子", "智能终端", "电子产业" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "software", Name = "软件计算机", Include = new[] { "软件", "计算机", "信息技术", "信创" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "cloud", Name = "云计算", Include = new[] { "云计算", "大数据", "数据中心" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "data_element", Name = "数据要素", Include = new[] { "数据要素", "数字经济", "大数据产业" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "cyber_security", Name = "网络安全", Include = new[] { "网络安全", "信息安全", "网络信息安全" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "ai", Name = "人工智能", Include = new[] { "人工智能", "AI产业", "大模型", "智能产业" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "ai_app", Name = "AI应用", Include = new[] { "AI应用", "人工智能应用", "数字创意" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "big_tech", Name = "大科技", Include = new[] { "科技", "TMT", "数字产业" }, Exclude = new[] { "债", "货币", "科技债" } },
            new() { Key = "robot", Name = "机器人", Include = new[] { "机器人", "智能制造", "工业母机", "人形机器人", "自动化" }, Exclude = new[] { "债", "货币" } },

            new() { Key = "military", Name = "军工国防", Include = new[] { "军工", "国防", "军事" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "aerospace", Name = "航天航空", Include = new[] { "航天航空", "航空航天", "航天军工", "空天军工", "航天海工", "航空产业" }, Exclude = new[] { "债", "货币", "航空运输", "航空服务", "机场" } },
            new() { Key = "satellite", Name = "卫星产业", Include = new[] { "卫星", "卫星产业", "卫星通信", "商业航天", "北斗", "空天信息" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "low_altitude", Name = "低空经济", Include = new[] { "低空经济", "低空", "通用航空", "飞行汽车" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "shipbuilding", Name = "船舶海工", Include = new[] { "船舶", "船舶制造", "海工装备" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "machinery", Name = "机械装备", Include = new[] { "机械", "机械设备", "高端装备", "专用设备" }, Exclude = new[] { "债", "货币", "军工" } },

            new() { Key = "auto", Name = "汽车", Include = new[] { "汽车", "汽车产业", "汽车整车" }, Exclude = new[] { "债", "货币", "汽车零部件", "新能源汽车" } },
            new() { Key = "auto_parts", Name = "汽车零部件", Include = new[] { "汽车零部件", "汽零" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "new_energy_vehicle", Name = "新能源汽车", Include = new[] { "新能源汽车", "新能源车", "电动车" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "smart_car", Name = "智能汽车", Include = new[] { "智能汽车", "智能车", "车联网", "汽车电子" }, Exclude = new[] { "债", "货币" } },

            new() { Key = "innovative_drug", Name = "创新药", Include = new[] { "创新药", "创新医疗" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "medicine", Name = "医药生物", Include = new[] { "医药", "生物医药", "中药", "疫苗", "CRO" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "healthcare", Name = "医疗", Include = new[] { "医疗", "医疗器械", "医美", "健康产业" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "oversea_medicine", Name = "海外医药", Include = new[] { "海外医药", "全球医药", "港股通医药", "恒生医疗" }, Exclude = new[] { "债", "货币" } },

            new() { Key = "food_beverage", Name = "食品饮料", Include = new[] { "食品饮料", "食品", "饮料" }, Exclude = new[] { "债", "货币", "食品债" } },
            new() { Key = "liquor", Name = "白酒", Include = new[] { "白酒", "酒类", "酒ETF" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "consumer", Name = "消费", Include = new[] { "消费", "内需", "消费升级" }, Exclude = new[] { "债", "货币", "消费电子" } },
            new() { Key = "home_appliance", Name = "家用电器", Include = new[] { "家电", "家用电器", "白色家电" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "tourism", Name = "旅游酒店", Include = new[] { "旅游", "酒店", "文旅" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "media", Name = "传媒", Include = new[] { "传媒", "影视", "文化产业" }, Exclude = new[] { "债", "货币", "游戏" } },
            new() { Key = "game", Name = "游戏动漫", Include = new[] { "游戏", "动漫", "电竞" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "education", Name = "教育", Include = new[] { "教育", "在线教育" }, Exclude = new[] { "债", "货币" } },

            new() { Key = "bank", Name = "银行", Include = new[] { "银行" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "securities", Name = "证券券商", Include = new[] { "证券", "券商" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "insurance", Name = "保险", Include = new[] { "保险", "保险主题" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "real_estate", Name = "地产", Include = new[] { "地产", "房地产", "地产等权" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "construction", Name = "基建工程", Include = new[] { "基建", "工程建设", "建筑材料", "建筑装饰" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "environment", Name = "环保", Include = new[] { "环保", "环境治理", "绿色发展" }, Exclude = new[] { "债", "货币" } },

            new() { Key = "agri", Name = "农业", Include = new[] { "农业", "农林牧渔", "粮食", "种业" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "breeding", Name = "养殖", Include = new[] { "养殖", "畜牧", "生猪", "农牧" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "transport", Name = "交通运输", Include = new[] { "交通运输", "运输产业", "铁路公路" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "shipping", Name = "航运港口", Include = new[] { "航运", "港口", "航运港口" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "logistics", Name = "物流", Include = new[] { "物流", "快递" }, Exclude = new[] { "债", "货币" } },

            new() { Key = "gem", Name = "创业板", Include = new[] { "创业板", "创业成长" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "double_innovation", Name = "双创50", Include = new[] { "双创", "科创创业", "双创50" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "north_exchange", Name = "北证", Include = new[] { "北证", "北交所", "北证50", "专精特新" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "dividend", Name = "红利低波", Include = new[] { "红利", "低波", "高股息" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "central_soe", Name = "央企国企", Include = new[] { "央企", "国企", "国资央企" }, Exclude = new[] { "债", "货币" } },

            new() { Key = "hs_tech", Name = "恒生科技", Include = new[] { "恒生科技", "恒生互联网", "港股科技", "中概互联" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "sp500", Name = "标普500", Include = new[] { "标普", "S&P", "SP500", "美国500" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "nasdaq", Name = "纳斯达克", Include = new[] { "纳斯达克", "纳指", "NASDAQ" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "asia_pacific", Name = "亚太市场", Include = new[] { "亚太", "日本", "越南", "印度", "东南亚" }, Exclude = new[] { "债", "货币" } },

            new() { Key = "bond", Name = "债券基金", Include = new[] { "债", "债券", "纯债", "信用债", "中短债" }, Exclude = new[] { "可转债", "转债" } },
            new() { Key = "convertible_bond", Name = "可转债", Include = new[] { "可转债", "转债" }, Exclude = Array.Empty<string>() },
            new() { Key = "mixed_bond", Name = "固收+", Include = new[] { "混债", "二级债", "一级债", "固收+", "固收加" }, Exclude = Array.Empty<string>() },
            new() { Key = "money", Name = "货币基金", Include = new[] { "货币", "现金", "添利", "余额", "天天理财" }, Exclude = new[] { "股票", "混合" } }
        };

        public static SectorCatalogEntry Resolve(string? keyOrName)
        {
            var clean = (keyOrName ?? string.Empty).Trim();
            return Definitions.FirstOrDefault(s =>
                       s.Key.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                       s.Name.Equals(clean, StringComparison.OrdinalIgnoreCase))
                   ?? Definitions.FirstOrDefault(s => clean.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
                   ?? new SectorCatalogEntry
                   {
                       Key = clean,
                       Name = clean,
                       Include = string.IsNullOrWhiteSpace(clean) ? Array.Empty<string>() : new[] { clean },
                       Exclude = Array.Empty<string>()
                   };
        }

        public static bool IsMatch(string? fundName, SectorCatalogEntry sector)
        {
            var name = fundName ?? string.Empty;
            return ContainsAny(name, sector.Include) && !ContainsAny(name, sector.Exclude);
        }

        public static int Score(string? fundName, string? fundType, SectorCatalogEntry sector)
        {
            var name = fundName ?? string.Empty;
            if (!IsMatch(name, sector)) return 0;

            var score = name.Contains(sector.Name, StringComparison.OrdinalIgnoreCase) ? 160 : 0;
            foreach (var keyword in sector.Include)
            {
                if (string.IsNullOrWhiteSpace(keyword) || !name.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
                score += Math.Min(90, 20 + keyword.Length * 10);
            }

            var group = ClassifyFundGroup(name, fundType);
            score += group switch
            {
                GroupIndex => 16,
                GroupMixed => 14,
                GroupEquity => 12,
                _ => 4
            };
            if (name.Contains("主题", StringComparison.OrdinalIgnoreCase)) score += 8;
            if (name.Contains("行业", StringComparison.OrdinalIgnoreCase)) score += 6;
            return score;
        }

        public static string ClassifyFundGroup(string? fundName, string? fundType)
        {
            var name = fundName ?? string.Empty;
            var type = fundType ?? string.Empty;
            if (type.Contains("指数", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("ETF", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("联接", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("指数", StringComparison.OrdinalIgnoreCase))
            {
                return GroupIndex;
            }
            if (type.Contains("混合", StringComparison.OrdinalIgnoreCase) || name.Contains("混合", StringComparison.OrdinalIgnoreCase))
            {
                return GroupMixed;
            }
            if (type.Contains("股票", StringComparison.OrdinalIgnoreCase) || name.Contains("股票", StringComparison.OrdinalIgnoreCase))
            {
                return GroupEquity;
            }
            return GroupOther;
        }

        public static bool MatchesGroup(string? actualGroup, string? requestedGroup)
        {
            var requested = NormalizeGroup(requestedGroup);
            if (requested == GroupAll) return true;
            if (requested == GroupActive) return actualGroup is GroupMixed or GroupEquity;
            return string.Equals(actualGroup, requested, StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeGroup(string? value)
        {
            var clean = (value ?? string.Empty).Trim().ToLowerInvariant();
            return clean is GroupIndex or GroupActive or GroupMixed or GroupEquity or GroupOther
                ? clean
                : GroupAll;
        }

        private static bool ContainsAny(string text, IEnumerable<string> words)
        {
            foreach (var word in words)
            {
                if (!string.IsNullOrWhiteSpace(word) && text.Contains(word, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
