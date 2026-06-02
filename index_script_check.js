
        const { createApp, ref, reactive, onMounted, onUnmounted, nextTick, shallowRef, watch, computed } = Vue;

        createApp({
            setup() {
                // 固定后端 API 域名：CDN 页面不再先试 guzhicdn 自己，避免登录和首屏请求多等一次失败重试。
                const API_BASE_DEFAULT = 'https://guzhi.21212121.xyz';
                let API_BASE = API_BASE_DEFAULT;
                try { localStorage.setItem('fund_api_base', API_BASE_DEFAULT); } catch (e) { }

                const apiFetch = async (path, options = {}) => fetch(API_BASE + path, options);

                const authFetch = async (path, formData) => {
                    const fd = new FormData();
                    for (const [k, v] of formData.entries()) fd.append(k, v);
                    return fetch(API_BASE + path, { method: 'POST', body: fd, cache: 'no-store' });
                };

                const toasts = ref([]); let toastId = 0;
                const expandedSectors = ref(new Set());

                const toggleSector = (id) => {
                    const newSet = new Set(expandedSectors.value);
                    if (newSet.has(id)) newSet.delete(id);
                    else newSet.add(id);
                    expandedSectors.value = newSet;
                };
                window.hasAutoSavedToday = false;

                const showToast = (msg, type = 'info') => {
                    const id = toastId++; toasts.value.push({ id, msg, type });
                    setTimeout(() => { toasts.value = toasts.value.filter(t => t.id !== id); }, 3000);
                };
                const isLoggedIn = ref(localStorage.getItem('fund_username') ? true : false);
                const currentUser = ref(localStorage.getItem('fund_username') || '');
                const fundsList = ref([]);
                const chartRef = ref(null);
                const isLoading = ref(true);
                const isNight = ref(false);
                const totalPrincipal = ref(0);
                const totalProfit = ref(0);
                const totalRate = ref(0);
                const totalTodayProfit = ref(0);
                const totalTodayRate = ref(0);
                const loginForm = reactive({ username: '', password: '' });
                const fundForm = reactive({ code: '', amount: '' });
                const chartInstance = shallowRef(null);
                let timer = null;
                let isFetchingData = false;
                let lastDataFetchAt = 0;
                const isUploading = ref(false);
                const showProfileModal = ref(false);
                const userAvatar = ref('');
                const pullRefresh = reactive({ startY: 0, startX: 0, distance: 0, active: false, show: false, loading: false });

                const activePage = ref('home');
                const todayDashStr = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; })();
                const analysisMonth = ref(todayDashStr.slice(0, 7));
                const selectedAnalysisDate = ref(todayDashStr);
                const analysisViewMode = ref('profit');
                const analysisDetailMode = ref('daily');
                const analysisLoading = ref(false);
                const analysisRecords = ref([]);
                const uiStateMap = ref({});
                const calibrationProfileMap = ref({});

                const assetMode = ref(localStorage.getItem('asset_mode') || 'fund');
                const stockHoldings = ref([]);
                const stockWatchList = ref([]);
                const stockSearchKeyword = ref('');
                const stockSearchResults = ref([]);
                const stockOcrLoading = ref(false);
                const stockOcrPreview = reactive({ show: false, batchId: 0, items: [] });
                const selectedStock = reactive({ code: '', market: '', name: '', price: 0, changeRate: 0 });
                const stockKlinePeriod = ref('minute');
                const stockKlinePeriods = [
                    { value: 'minute', label: '分K' },
                    { value: 'hour', label: '时K' },
                    { value: 'day', label: '日K' },
                    { value: 'month', label: '月K' },
                    { value: 'year', label: '年K' }
                ];
                const stockChartRef = ref(null);
                const stockChartInstance = shallowRef(null);


                const savedMode = localStorage.getItem('privacy_mode');
                const privacyMode = ref(savedMode !== null ? Number(savedMode) : 2);
                const showPrivacyModal = ref(false);
                const setPrivacyMode = (mode) => { privacyMode.value = mode; localStorage.setItem('privacy_mode', mode); showPrivacyModal.value = false; saveUiState('privacy_mode', { value: mode }); };
                const editForm = reactive({ show: false, code: '', name: '', cost: '', shares: '', originalCode: '' });

                const sectorTopList = ref([]);
                const sectorBottomList = ref([]);
                const sectorMode = ref('top');
                const sectorSource = ref('');
                const sectorUpdatedAt = ref('');
                const capitalFlowRows = ref([]);
                const capitalFlowInRows = ref([]);
                const capitalFlowOutRows = ref([]);
                const capitalFlowSource = ref('');
                const capitalFlowUpdatedAt = ref('');
                const capitalFlowError = ref('');
                const isCapitalLoading = ref(false);
                const showSectorRadar = ref(false);
                const isSectorLoading = ref(false);
                const showSectorDetails = ref(false);
                const detailsTitle = ref('');
                const detailsList = ref([]);
                const sectorDetailMeta = reactive({ rate: 0, fundCount: 0, updatedAt: '' });
                const isDetailsLoading = ref(false);

                const showNewsPanel = ref(false);
                const isNewsLoading = ref(false);
                const newsMode = ref('global');
                const newsOnlyImportant = ref(false);
                const newsList = ref([]);
                const newsSource = ref('');
                const newsUpdatedAt = ref('');

                const groupedNews = computed(() => {
                    const map = new Map();
                    for (const item of newsList.value) {
                        const key = item.dateText || (item.showTime ? item.showTime.slice(0, 10) : '今日');
                        if (!map.has(key)) map.set(key, []);
                        map.get(key).push(item);
                    }
                    return Array.from(map.entries()).map(([date, items]) => ({ date, items }));
                });




                // 资讯秒开缓存：先显示本地旧数据，再后台刷新。
                // 7x24 和持仓资讯分开缓存，避免切页时白屏等接口。
                const NEWS_CACHE_DURATION = 2 * 60 * 1000;
                let newsMemoryCache = new Map();
                let newsWarmStarted = false;
                const getNewsCacheKey = (mode = newsMode.value, important = newsOnlyImportant.value) => `news_cache_v2_${currentUser.value || 'guest'}_${mode}_${important ? 1 : 0}`;
                const applyNewsPayload = (data) => {
                    newsList.value = Array.isArray(data?.items) ? data.items : [];
                    newsSource.value = data?.source || '东方财富7x24快讯';
                    newsUpdatedAt.value = data?.updatedAt || '';
                };
                const warmNewsCache = () => {
                    if (newsWarmStarted || !currentUser.value) return;
                    newsWarmStarted = true;
                    setTimeout(() => fetchNews('global', false, { background: true }), 800);
                    setTimeout(() => fetchNews('holding', false, { background: true }), 1800);
                };

                const analysisTotalRecords = computed(() => analysisRecords.value.filter(r => r.fundCode === 'TOTAL'));
                const selectedDayFundRecords = computed(() => analysisRecords.value.filter(r => r.fundCode !== 'TOTAL' && String(r.recordDate || '').slice(0, 10) === selectedAnalysisDate.value).sort((a, b) => Math.abs(Number(b.dailyProfit || 0)) - Math.abs(Number(a.dailyProfit || 0))));
                const selectedDayTotalProfitRecords = computed(() => fundsList.value.map(f => ({ fundCode: f.code, fundName: f.name, assets: Number(f.todayAmount || f.amount || 0), totalProfit: Number(f.estimatedProfit || 0), totalRate: Number(f.existingReturnRate || 0) })).sort((a, b) => Math.abs(Number(b.totalProfit || 0)) - Math.abs(Number(a.totalProfit || 0))));
                const analysisMonthTotal = computed(() => analysisTotalRecords.value.filter(r => String(r.recordDate || '').slice(0, 7) === analysisMonth.value).reduce((sum, r) => sum + Number(r.dailyProfit || 0), 0));
                const analysisCalendarDays = computed(() => {
                    const [y, m] = analysisMonth.value.split('-').map(Number);
                    if (!y || !m) return [];
                    const first = new Date(y, m - 1, 1);
                    const days = new Date(y, m, 0).getDate();
                    const offset = (first.getDay() + 6) % 7;
                    const totalMap = new Map();
                    for (const r of analysisTotalRecords.value) {
                        const d = String(r.recordDate || '').slice(0, 10);
                        if (d.slice(0, 7) === analysisMonth.value) totalMap.set(d, r);
                    }
                    const arr = [];
                    for (let i = 0; i < offset; i++) arr.push({ key: 'e' + i, empty: true });
                    for (let day = 1; day <= days; day++) {
                        const date = `${y}-${String(m).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
                        const rec = totalMap.get(date);
                        arr.push({ key: date, empty: false, day, date, hasRecord: !!rec, profit: Number(rec?.dailyProfit || 0), rate: Number(rec?.dailyRate || 0) });
                    }
                    return arr;
                });
                const analysisProfitTop = computed(() => selectedDayFundRecords.value.filter(r => Number(r.dailyProfit || 0) > 0).slice(0, 5));
                const analysisLossTop = computed(() => selectedDayFundRecords.value.filter(r => Number(r.dailyProfit || 0) < 0).sort((a, b) => Number(a.dailyProfit || 0) - Number(b.dailyProfit || 0)).slice(0, 5));

                const sectorRules = [
                    { name: '半导体 / 芯片', words: ['半导体', '芯片', '集成电路', '电子'] },
                    { name: 'AI / 人工智能', words: ['人工智能', 'AI', '智能', '机器人', '软件', '计算机'] },
                    { name: '港股 / 恒生', words: ['恒生', '港股', '中概', '香港'] },
                    { name: '新能源 / 电池', words: ['新能源', '光伏', '锂电', '电池', '碳中和'] },
                    { name: '医药 / 医疗', words: ['医药', '医疗', '创新药', '生物'] },
                    { name: '黄金 / 有色', words: ['黄金', '有色', '金属', '资源'] },
                    { name: '银行 / 金融', words: ['银行', '证券', '金融', '保险'] },
                    { name: '地产 / 基建', words: ['地产', '房地产', '基建', '工程'] },
                    { name: '消费 / 白酒', words: ['消费', '白酒', '食品', '家电'] },
                    { name: '债券 / 现金', words: ['债', '货币', '现金', '短融'] }
                ];

                const classifyFundSector = (fund) => {
                    const text = `${fund?.name || ''} ${fund?.code || ''}`.toUpperCase();
                    const hit = sectorRules.find(rule => rule.words.some(w => text.includes(w.toUpperCase())));
                    return hit ? hit.name : '其他主题';
                };

                const getConfidence = (fund) => {
                    let score = 62;
                    const reasons = [];
                    if (fund?.isSettled) { score += 28; reasons.push('净值确认'); }
                    else if (fund?.isHoliday) { score += 8; reasons.push('休市沿用'); }
                    else { score += 12; reasons.push('盘中估算'); }
                    const diff = Math.abs(Number(fund?.diffRate || 0));
                    if (diff > 0.6) { score -= 25; reasons.push('偏离较大'); }
                    else if (diff > 0.15) { score -= 12; reasons.push('估值偏离'); }
                    else { score += 8; reasons.push('偏离可控'); }
                    if (Number(fund?.shares || 0) > 0) { score += 5; reasons.push('份额完整'); }
                    score = Math.max(20, Math.min(100, Math.round(score)));
                    const color = score >= 82 ? '#10b981' : (score >= 65 ? '#fbbf24' : '#ef4444');
                    return { code: fund?.code || '', name: fund?.name || '', score, color, reason: reasons.slice(0, 2).join(' / ') };
                };

                const confidenceRows = computed(() => fundsList.value.map(getConfidence).sort((a, b) => a.score - b.score));

                const portfolioExposure = computed(() => {
                    const total = fundsList.value.reduce((sum, f) => sum + Number(f.todayAmount || f.amount || 0), 0);
                    const map = new Map();
                    for (const fund of fundsList.value) {
                        const name = classifyFundSector(fund);
                        const row = map.get(name) || { name, amount: 0, dailyProfit: 0, count: 0, funds: [] };
                        row.amount += Number(fund.todayAmount || fund.amount || 0);
                        row.dailyProfit += Number(fund.todayProfit || 0);
                        row.count += 1;
                        row.funds.push(fund.name);
                        map.set(name, row);
                    }
                    return Array.from(map.values()).map(x => ({ ...x, ratio: total > 0 ? x.amount / total * 100 : 0 })).sort((a, b) => b.amount - a.amount);
                });

                const dailyBattleReport = computed(() => {
                    const rows = [...fundsList.value].map(f => ({ ...f, p: Number(f.todayProfit || 0), amountNum: Number(f.todayAmount || f.amount || 0) }));
                    const best = rows.slice().sort((a, b) => b.p - a.p)[0];
                    const worst = rows.slice().sort((a, b) => a.p - b.p)[0];
                    const exposure = portfolioExposure.value[0];
                    const profit = Number(totalTodayProfit.value || 0);
                    let actionHint = '保持观察';
                    if (profit < 0 && worst && Math.abs(worst.p) > Math.abs(profit) * 0.5) actionHint = `重点复盘 ${worst.name}`;
                    else if (exposure && exposure.ratio >= 45) actionHint = `${exposure.name} 集中度偏高`;
                    else if (profit > 0) actionHint = '盈利日先看净值确认';
                    return {
                        summary: fundsList.value.length ? `${fundsList.value.length} 只持仓，主暴露 ${exposure?.name || '待识别'} ${exposure ? exposure.ratio.toFixed(1) : '0.0'}%。` : '暂无持仓数据。',
                        todayProfitText: `${profit >= 0 ? '+' : ''}${profit.toFixed(2)}`,
                        bestName: best ? `${best.name} ${best.p >= 0 ? '+' : ''}${best.p.toFixed(2)}` : '暂无',
                        worstName: worst ? `${worst.name} ${worst.p >= 0 ? '+' : ''}${worst.p.toFixed(2)}` : '暂无',
                        actionHint
                    };
                });

                const newsImpactSummary = computed(() => {
                    const map = new Map();
                    for (const item of newsList.value) {
                        const key = item.matchedFundName || (item.tags && item.tags[0]) || '全市场';
                        const row = map.get(key) || { name: key, score: 0, latestTitle: '', tags: [] };
                        const sign = item.sentiment === 'negative' ? -1 : 1;
                        row.score += sign * Number(item.impactScore || 0);
                        if (!row.latestTitle) row.latestTitle = item.title || item.summary || '';
                        row.tags = Array.from(new Set([...(row.tags || []), ...(item.tags || []).slice(0, 2)])).slice(0, 4);
                        map.set(key, row);
                    }
                    return Array.from(map.values()).sort((a, b) => Math.abs(b.score) - Math.abs(a.score)).slice(0, 6);
                });

                const recoveryWatchList = computed(() => fundsList.value.filter(f => Number(f.breakEvenRate || 0) > 0).sort((a, b) => Number(b.breakEvenRate || 0) - Number(a.breakEvenRate || 0)));
                const recoveryModal = reactive({ show: false, code: '', name: '', assets: 0, cost: 0, loss: 0, breakEvenRate: 0, addAmount: '' });
                const openRecoveryModal = (fund) => {
                    const assets = Number(fund.todayAmount || fund.amount || 0);
                    const cost = Number(fund.cost || fund.amount || 0);
                    recoveryModal.show = true;
                    recoveryModal.code = fund.code;
                    recoveryModal.name = fund.name;
                    recoveryModal.assets = assets;
                    recoveryModal.cost = cost;
                    recoveryModal.loss = Math.max(0, cost - assets);
                    recoveryModal.breakEvenRate = Number(fund.breakEvenRate || 0);
                    const savedRecovery = uiStateMap.value?.[`recovery_amount_${fund.code}`];
                    recoveryModal.addAmount = savedRecovery?.amount ? String(savedRecovery.amount) : '';
                };
                const customRecoveryPlan = computed(() => {
                    const amount = Math.max(0, Number(recoveryModal.addAmount || 0));
                    const newAssets = recoveryModal.assets + amount;
                    const newCost = recoveryModal.cost + amount;
                    const newBreakEven = amount > 0 && newAssets > 0 ? Math.max(0, (newCost / newAssets - 1) * 100) : recoveryModal.breakEvenRate;
                    return { amount, newAssets, newCost, newBreakEven };
                });

                const setAssetMode = (mode) => {
                    assetMode.value = mode;
                    localStorage.setItem('asset_mode', mode);
                    saveUiState('asset_mode', { value: mode });
                    if (mode === 'stock') nextTick(() => { loadStockDashboard(false); renderStockChart([]); });
                };

                const triggerStockOcr = () => document.getElementById('stockOcrInput')?.click();

                const loadStockDashboard = async (force = false) => {
                    if (!currentUser.value) return;
                    try {
                        const res = await apiFetch(`/api/stock/dashboard?username=${encodeURIComponent(currentUser.value)}`, { cache: 'no-store' });
                        const data = await res.json();
                        if (!res.ok || !data.success) throw new Error(data.message || '股票数据读取失败');
                        stockHoldings.value = data.holdings || [];
                        stockWatchList.value = data.watchList || [];
                        if (!selectedStock.code) {
                            const first = stockHoldings.value[0] || stockWatchList.value[0];
                            if (first) openStockDetail(first);
                        }
                        if (force) showToast('股票数据已刷新', 'success');
                    } catch (e) {
                        if (force) showToast(e.message || '股票数据读取失败', 'error');
                    }
                };

                const searchStocks = async () => {
                    const keyword = stockSearchKeyword.value.trim();
                    if (!keyword) return showToast('请输入股票代码或名称', 'error');
                    try {
                        const res = await apiFetch(`/api/stock/search?keyword=${encodeURIComponent(keyword)}`, { cache: 'no-store' });
                        const data = await res.json();
                        if (!res.ok || !data.success) throw new Error(data.message || '查询失败');
                        stockSearchResults.value = data.items || [];
                        if (!stockSearchResults.value.length) showToast('没有匹配股票', 'info');
                    } catch (e) { showToast(e.message || '股票查询失败', 'error'); }
                };

                const addStockWatch = async (s) => {
                    if (!s?.code) return;
                    try {
                        const res = await apiFetch('/api/stock/watch', {
                            method: 'POST', headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ username: currentUser.value, stockCode: s.code, stockName: s.name })
                        });
                        const data = await res.json();
                        if (!res.ok || !data.success) throw new Error(data.message || '加入自选失败');
                        showToast('已加入自选', 'success');
                        await loadStockDashboard(false);
                    } catch (e) { showToast(e.message || '加入自选失败', 'error'); }
                };

                const openStockHoldingEditor = async (s) => {
                    const shares = Number(prompt(`输入 ${s.name} 持股数量`, '100') || 0);
                    if (shares <= 0) return;
                    const costPrice = Number(prompt(`输入 ${s.name} 成本价`, String(s.price || '')) || 0);
                    if (costPrice <= 0) return;
                    try {
                        const res = await apiFetch('/api/stock/holding', {
                            method: 'POST', headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ username: currentUser.value, stockCode: s.code, stockName: s.name, shares, costPrice, costAmount: shares * costPrice })
                        });
                        const data = await res.json();
                        if (!res.ok || !data.success) throw new Error(data.message || '保存持仓失败');
                        showToast('股票持仓已保存', 'success');
                        await loadStockDashboard(false);
                    } catch (e) { showToast(e.message || '保存持仓失败', 'error'); }
                };

                const deleteStockHolding = async (s) => {
                    if (!confirm(`删除股票持仓：${s.name}？`)) return;
                    const res = await apiFetch(`/api/stock/holding?username=${encodeURIComponent(currentUser.value)}&code=${encodeURIComponent(s.code)}`, { method: 'DELETE' });
                    if (res.ok) { showToast('已删除股票持仓', 'success'); await loadStockDashboard(false); }
                };

                const deleteStockWatch = async (s) => {
                    const res = await apiFetch(`/api/stock/watch?username=${encodeURIComponent(currentUser.value)}&code=${encodeURIComponent(s.code)}`, { method: 'DELETE' });
                    if (res.ok) { showToast('已移除自选', 'success'); await loadStockDashboard(false); }
                };

                const openStockDetail = async (s) => {
                    Object.assign(selectedStock, { code: s.code, market: s.market || '', name: s.name || '', price: Number(s.price || 0), changeRate: Number(s.changeRate || 0) });
                    await fetchStockKlines();
                };

                const switchStockKline = async (period) => {
                    stockKlinePeriod.value = period;
                    await fetchStockKlines();
                };

                const fetchStockKlines = async () => {
                    if (!selectedStock.code) { renderStockChart([]); return; }
                    try {
                        const res = await apiFetch(`/api/stock/klines?code=${encodeURIComponent(selectedStock.code)}&period=${encodeURIComponent(stockKlinePeriod.value)}`, { cache: 'no-store' });
                        const data = await res.json();
                        renderStockChart(data.items || []);
                    } catch (e) { showToast('走势读取失败', 'error'); }
                };

                const renderStockChart = (rows) => {
                    if (!stockChartRef.value || !window.echarts) return;
                    if (!stockChartInstance.value) stockChartInstance.value = echarts.init(stockChartRef.value);
                    const data = (rows || []).map(x => [x.time, Number(x.close || 0), Number(x.open || 0), Number(x.low || 0), Number(x.high || 0)]);
                    stockChartInstance.value.setOption({
                        backgroundColor: 'transparent',
                        tooltip: { trigger: 'axis', backgroundColor: 'rgba(15,23,42,.92)', borderColor: '#334155', textStyle: { color: '#f8fafc' } },
                        grid: { left: 42, right: 20, top: 28, bottom: 36 },
                        xAxis: { type: 'category', data: data.map(x => x[0]), axisLabel: { color: '#94a3b8' } },
                        yAxis: { scale: true, axisLabel: { color: '#94a3b8' }, splitLine: { lineStyle: { color: 'rgba(148,163,184,.14)' } } },
                        series: [{ name: selectedStock.name || '走势', type: 'candlestick', data: data.map(x => [x[2], x[1], x[3], x[4]]) }]
                    }, true);
                };

                const handleStockOcrUpload = async (event) => {
                    const file = event.target.files?.[0];
                    event.target.value = '';
                    if (!file) return;
                    stockOcrLoading.value = true;
                    try {
                        const fd = new FormData();
                        fd.append('username', currentUser.value);
                        fd.append('image', file);
                        const res = await apiFetch('/api/stock/import-ocr-preview', { method: 'POST', body: fd });
                        const data = await res.json();
                        if (!res.ok || !data.success) throw new Error(data.message || '股票 OCR 失败');
                        stockOcrPreview.show = true;
                        stockOcrPreview.batchId = data.batchId;
                        stockOcrPreview.items = (data.items || []).map(x => ({ ...x, action: x.action || 'holding' }));
                        showToast(`识别到 ${stockOcrPreview.items.length} 条股票候选`, 'success');
                    } catch (e) { showToast(e.message || '股票 OCR 失败', 'error'); }
                    finally { stockOcrLoading.value = false; }
                };

                const confirmStockOcr = async () => {
                    try {
                        const res = await apiFetch('/api/stock/import-ocr-confirm', {
                            method: 'POST', headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ username: currentUser.value, batchId: stockOcrPreview.batchId, items: stockOcrPreview.items })
                        });
                        const data = await res.json();
                        if (!res.ok || !data.success) throw new Error(data.message || '写库失败');
                        stockOcrPreview.show = false;
                        showToast(`已写入 ${data.saved} 条股票数据`, 'success');
                        await loadStockDashboard(false);
                    } catch (e) { showToast(e.message || '股票 OCR 确认失败', 'error'); }
                };

                const historyModal = reactive({ show: false, isLoading: false, name: '', data: [] });
                const archiveModal = reactive({ show: false, isLoading: false, targetName: '', data: [] });
                const ocrModal = reactive({ show: false, content: '' });

                const globalIndices = ref([
                    { name: '上证指数', secid: '1.000001', code: '000001', latest: 0, todayRate: 0, yearRate: 0, klines: [] },
                    { name: '科创50', secid: '1.000688', code: '000688', latest: 0, todayRate: 0, yearRate: 0, klines: [] },
                    { name: '创业板指', secid: '0.399006', code: '399006', latest: 0, todayRate: 0, yearRate: 0, klines: [] },
                    { name: '恒生指数', secid: '100.HSI', code: 'HSI', latest: 0, todayRate: 0, yearRate: 0, klines: [] },
                    { name: '纳斯达克', secid: '100.NDX', code: 'NDX', latest: 0, todayRate: 0, yearRate: 0, klines: [] },
                    { name: '标普500', secid: '100.SPX', code: 'SPX', latest: 0, todayRate: 0, yearRate: 0, klines: [] },
                    { name: '道琼斯', secid: '100.DJIA', code: 'DJIA', latest: 0, todayRate: 0, yearRate: 0, klines: [] }
                ]);

                const showGlobalIndices = ref(false);
                const isIndicesLoading = ref(false);

                const fetchNews = async (mode = newsMode.value, force = false, options = {}) => {
                    const background = !!options.background;
                    const targetMode = mode || 'global';
                    const key = getNewsCacheKey(targetMode, newsOnlyImportant.value);
                    const now = Date.now();

                    if (!background) newsMode.value = targetMode;

                    if (!force) {
                        const mem = newsMemoryCache.get(key);
                        if (mem && (now - mem.time) < NEWS_CACHE_DURATION) {
                            if (!background || (activePage.value === 'news' && newsMode.value === targetMode)) applyNewsPayload(mem.data);
                            return;
                        }

                        const disk = readJsonCache(key, 15 * 60 * 1000);
                        if (disk) {
                            newsMemoryCache.set(key, { time: now, data: disk });
                            if (!background || (activePage.value === 'news' && newsMode.value === targetMode)) applyNewsPayload(disk);
                            if (background) return;
                        }
                    }

                    if (!background && newsList.value.length === 0) isNewsLoading.value = true;
                    try {
                        const limit = targetMode === 'holding' ? 90 : 70;
                        const path = `/api/fund/news?username=${encodeURIComponent(currentUser.value)}&mode=${encodeURIComponent(targetMode)}&important=${newsOnlyImportant.value}&limit=${limit}${force ? '&force=true' : ''}`;
                        const res = await apiFetch(path, { cache: 'no-store' });
                        if (!res.ok) throw new Error(await res.text());
                        const data = await res.json();
                        newsMemoryCache.set(key, { time: Date.now(), data });
                        writeJsonCache(key, data);
                        if (!background || (activePage.value === 'news' && newsMode.value === targetMode)) applyNewsPayload(data);
                    } catch (e) {
                        if (!background && newsList.value.length === 0) showToast('📰 资讯同步失败', 'error');
                    } finally {
                        if (!background) isNewsLoading.value = false;
                    }
                };

                const openNewsPanel = () => {
                    showNewsPanel.value = true;
                    fetchNews(newsMode.value, false);
                };

                const switchNewsMode = (mode) => {
                    if (newsMode.value === mode && newsList.value.length > 0) return;
                    fetchNews(mode, false);
                };

                const openNewsItem = (item) => {
                    if (item && item.url) window.open(item.url, '_blank');
                };

                const fetchMyArchives = async (targetCode, targetName) => {
                    archiveModal.show = true; archiveModal.isLoading = true; archiveModal.targetName = targetName; archiveModal.data = [];
                    try {
                        // 只取目标基金/总持仓的档案，避免每次点击都把所有历史记录拖回前端。
                        const url = `${API_BASE}/api/fund/get-archives?username=${encodeURIComponent(currentUser.value)}&fundCode=${encodeURIComponent(targetCode)}&limit=120&_t=${Date.now()}`;
                        const res = await fetch(url, { cache: 'no-store' });
                        if (res.ok) {
                            archiveModal.data = await res.json();
                        }
                    } catch (e) { showToast("🌐 档案馆断线", "error"); } finally { archiveModal.isLoading = false; }
                };


                const loadPersistentUiState = async () => {
                    if (!currentUser.value) return;
                    try {
                        const res = await apiFetch(`/api/fund/ui-state/all?username=${encodeURIComponent(currentUser.value)}`, { cache: 'no-store' });
                        if (!res.ok) return;
                        const data = await res.json();
                        uiStateMap.value = data.states || {};
                        const privacy = uiStateMap.value.privacy_mode;
                        const privacyValue = typeof privacy === 'number' ? privacy : Number(privacy?.value ?? localStorage.getItem('privacy_mode') ?? privacyMode.value);
                        if (Number.isFinite(privacyValue)) privacyMode.value = privacyValue;
                        const analysisPrefs = uiStateMap.value.analysis_preferences || {};
                        if (analysisPrefs.month) analysisMonth.value = analysisPrefs.month;
                        if (analysisPrefs.viewMode) analysisViewMode.value = analysisPrefs.viewMode;
                        if (analysisPrefs.detailMode) analysisDetailMode.value = analysisPrefs.detailMode;
                    } catch (e) { }
                };

                const saveUiState = async (key, state) => {
                    if (!currentUser.value || !key) return;
                    uiStateMap.value = { ...(uiStateMap.value || {}), [key]: state };
                    try {
                        await apiFetch('/api/fund/ui-state', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ username: currentUser.value, key, state })
                        });
                    } catch (e) { }
                };

                const persistAnalysisPreferences = () => saveUiState('analysis_preferences', {
                    month: analysisMonth.value,
                    viewMode: analysisViewMode.value,
                    detailMode: analysisDetailMode.value
                });

                const loadCalibrationProfiles = async () => {
                    if (!currentUser.value) return;
                    try {
                        const res = await apiFetch(`/api/fund/valuation-calibration/current?username=${encodeURIComponent(currentUser.value)}`, { cache: 'no-store' });
                        if (!res.ok) return;
                        const data = await res.json();
                        const map = {};
                        (data.items || []).forEach(item => {
                            const code = item.code || item.fundCode;
                            if (!code) return;
                            map[code] = {
                                offset: Number(item.offset || 0),
                                samples: Number(item.samples || 0),
                                lastError: Number(item.lastError || 0),
                                confidence: item.confidence || '低',
                                note: item.note || ''
                            };
                        });
                        calibrationProfileMap.value = map;
                    } catch (e) { }
                };

                const getCalibrationProfileByCode = (code) => {
                    const row = calibrationProfileMap.value?.[code] || {};
                    return {
                        offset: Number(row.offset || 0),
                        samples: Number(row.samples || 0),
                        lastError: Number(row.lastError || 0),
                        confidence: row.confidence || '低',
                        note: row.note || ''
                    };
                };

                const saveIntradayCalibrationSnapshots = async (items) => {
                    if (!currentUser.value || !items || items.length === 0) return;
                    try {
                        await apiFetch('/api/fund/valuation-calibration/intraday-snapshot', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ username: currentUser.value, items })
                        });
                    } catch (e) { }
                };

                const settleCalibrationSamples = async (items) => {
                    if (!currentUser.value || !items || items.length === 0) return;
                    try {
                        const res = await apiFetch('/api/fund/valuation-calibration/settle', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ username: currentUser.value, items })
                        });
                        if (res.ok) await loadCalibrationProfiles();
                    } catch (e) { }
                };

                const saveInsightSnapshot = async (snapshotType, payload, snapshotDate = todayDashStr) => {
                    if (!currentUser.value || !snapshotType) return;
                    try {
                        await apiFetch('/api/fund/insight-snapshots', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ username: currentUser.value, snapshotType, snapshotDate, payload })
                        });
                    } catch (e) { }
                };

                const calibrationRows = computed(() => {
                    return fundsList.value.map(f => {
                        const profile = getCalibrationProfileByCode(f.code);
                        return { code: f.code, name: f.name, ...profile };
                    }).filter(x => x.samples > 0 || Math.abs(x.offset) > 0.001).sort((a, b) => Math.abs(b.offset) - Math.abs(a.offset));
                });

                const saveDailyArchive = async (silent = false) => {
                    if (!silent && !confirm("💾 确定要将大屏当前的数据作为【今日最终收益】永久封存吗？")) return;
                    let d = new Date();
                    let localDateStr = d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
                    const safeNumber = (val) => Number((parseFloat(val) || 0).toFixed(2));
                    const totalAssetsNow = fundsList.value.reduce((sum, f) => sum + safeNumber(f.todayAmount || f.amount), 0);
                    const payload = {
                        Username: currentUser.value, DateStr: localDateStr,
                        Total: { FundName: "总持仓", Assets: safeNumber(totalAssetsNow), DailyProfit: safeNumber(totalTodayProfit.value), DailyRate: safeNumber(totalTodayRate.value), TotalProfit: safeNumber(totalProfit.value), TotalRate: safeNumber(totalRate.value) },
                        Funds: fundsList.value.map(f => ({ FundCode: f.code, FundName: f.name, Assets: safeNumber(f.todayAmount || f.amount), DailyProfit: safeNumber(f.todayProfit), DailyRate: safeNumber(f.currentRate), TotalProfit: safeNumber(f.estimatedProfit), TotalRate: safeNumber(f.existingReturnRate) }))
                    };
                    try {
                        const response = await fetch(`${API_BASE}/api/fund/save-archive`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
                        if (response.ok) { if (!silent) showToast("✅ 今日收益已安全锁入密码箱！", "success"); } else { if (!silent) showToast("❌ 封存受阻", "error"); }
                    } catch (e) { if (!silent) showToast("🌐 指挥中心失联", "error"); }
                };

                const fetchGlobalIndices = async () => {
                    showGlobalIndices.value = true; isIndicesLoading.value = true;
                    const indices = [{ name: '上证指数', secid: '1.000001' }, { name: '科创50', secid: '1.000688' }, { name: '创业板指', secid: '0.399006' }, { name: '恒生指数', secid: '100.HSI' }, { name: '纳斯达克', secid: '100.NDX' }, { name: '标普500', secid: '100.SPX' }, { name: '道琼斯', secid: '100.DJIA' }];
                    try {
                        const fetchJSONP = (secid) => {
                            return new Promise((resolve) => {
                                const callbackName = 'jsonp_' + secid.replace(/\./g, '_');
                                window[callbackName] = function (data) { resolve(data); try { delete window[callbackName]; const scriptNode = document.getElementById(callbackName); if (scriptNode) scriptNode.remove(); } catch (e) { } };
                                const script = document.createElement('script'); script.id = callbackName; script.referrerPolicy = "no-referrer"; script.src = `https://push2his.eastmoney.com/api/qt/stock/kline/get?secid=${secid}&ut=fa5fd1943c7b386f172d6893dbfba10b&fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59&klt=101&fqt=1&end=20500101&lmt=250&cb=${callbackName}&_t=${Date.now()}`;
                                script.onerror = () => resolve(null); document.body.appendChild(script);
                            });
                        };
                        const tasks = indices.map(async idx => {
                            const json = await fetchJSONP(idx.secid); if (!json || !json.data || !json.data.klines) return null;
                            const klines = json.data.klines; if (klines.length === 0) return null;
                            const latest = klines[klines.length - 1].split(','); const oldest = klines[0].split(',');
                            const latestClose = parseFloat(latest[2]) || 0; const todayRate = parseFloat(latest[8]) || 0; const oldestClose = parseFloat(oldest[2]) || 1;
                            const yearRate = ((latestClose - oldestClose) / oldestClose * 100).toFixed(2);
                            return { name: idx.name, latest: latestClose, todayRate: todayRate, yearRate: parseFloat(yearRate), klines: klines.slice().reverse().map(k => { const p = k.split(','); return { date: p[0], rate: parseFloat(p[8]) || 0 }; }) };
                        });
                        const results = await Promise.all(tasks);
                        globalIndices.value = globalIndices.value.map(item => { const found = results.find(r => r && r.name === item.name); return found ? { ...item, ...found } : item; });
                    } catch (e) { } finally { isIndicesLoading.value = false; }
                };

                const viewIndexHistory = (item) => {
                    if (!item.klines || item.klines.length === 0) return showToast("暂无数据", "error");
                    historyModal.show = true; historyModal.name = item.name; historyModal.isLoading = false; historyModal.data = item.klines;
                };

                const fetchHistory = (code, name) => {
                    if (!/^\d{6}$/.test(code)) return showToast("🚨 非法代码！", "error");
                    historyModal.show = true; historyModal.isLoading = true; historyModal.name = name; historyModal.data = [];
                    const scriptId = `jsonp_${code}`; let oldScript = document.getElementById(scriptId); if (oldScript) oldScript.remove();
                    const script = document.createElement('script'); script.id = scriptId; script.src = `https://fund.eastmoney.com/pingzhongdata/${code}.js?v=${new Date().getTime()}`;
                    script.onload = () => { if (typeof Data_netWorthTrend !== 'undefined') { let trend = Data_netWorthTrend.slice(-250).reverse(); historyModal.data = trend.map(item => { let d = new Date(item.x); return { date: `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`, rate: item.equityReturn || 0 }; }); } historyModal.isLoading = false; };
                    script.onerror = () => { showToast("📡 抓取失败", "error"); historyModal.isLoading = false; };
                    document.body.appendChild(script);
                };

                const fetchSectorDetails = async (sector) => {
                    const sectorKey = sector.key || sector.name;
                    detailsTitle.value = (sector.name || sectorKey) + ' - 板块基金';
                    showSectorDetails.value = true;
                    isDetailsLoading.value = true;
                    detailsList.value = [];
                    sectorDetailMeta.rate = 0;
                    sectorDetailMeta.fundCount = 0;
                    sectorDetailMeta.updatedAt = '';
                    try {
                        const res = await fetch(`${API_BASE}/api/fund/sector-funds?sectorName=${encodeURIComponent(sectorKey)}&limit=24`);
                        if (!res.ok) throw new Error(await res.text());
                        const data = await res.json();
                        detailsTitle.value = `${data.name || sector.name || sectorKey} - 板块基金`;
                        detailsList.value = Array.isArray(data.funds) ? data.funds : (Array.isArray(data) ? data : []);
                        sectorDetailMeta.rate = Number(data.rate || 0);
                        sectorDetailMeta.fundCount = Number(data.fundCount || detailsList.value.length || 0);
                        sectorDetailMeta.updatedAt = data.updatedAt || '';
                    } catch (e) {
                        showToast("🚨 板块基金拉取失败", "error");
                    } finally {
                        isDetailsLoading.value = false;
                    }
                };

                const compressImageBeforeUpload = (file, successCallback) => {
                    const reader = new FileReader(); reader.readAsDataURL(file);
                    reader.onload = function (e) {
                        const img = new Image(); img.src = e.target.result;
                        img.onload = function () {
                            const canvas = document.createElement('canvas'); const ctx = canvas.getContext('2d', { willReadFrequently: false });
                            let targetWidth = img.width; let targetHeight = img.height;
                            // OCR 导入必须保留小数点、负号和基金名称小字。旧版 800px/0.5 会把支付宝持仓截图压糊。
                            // 这里改成高清压缩：长边最多 1800，质量 0.9，速度和准确率更均衡。
                            const maxSide = 1800;
                            if (Math.max(targetWidth, targetHeight) > maxSide) {
                                const scale = maxSide / Math.max(targetWidth, targetHeight);
                                targetWidth = Math.round(targetWidth * scale);
                                targetHeight = Math.round(targetHeight * scale);
                            }
                            canvas.width = targetWidth; canvas.height = targetHeight;
                            ctx.fillStyle = "#fff"; ctx.fillRect(0, 0, canvas.width, canvas.height);
                            ctx.drawImage(img, 0, 0, targetWidth, targetHeight);
                            canvas.toBlob(blob => successCallback(new File([blob], file.name.replace(/\.[^/.]+$/, "") + ".jpg", { type: 'image/jpeg' })), 'image/jpeg', 0.9);
                        };
                    };
                };
                const handleRegister = async () => {
                    const username = (loginForm.username || '').trim();
                    const password = loginForm.password || '';
                    if (!username || !password) return showToast("为空！", "error");
                    let fd = new FormData(); fd.append('username', username); fd.append('password', password);
                    try {
                        const res = await authFetch('/api/auth/register', fd);
                        if (res.ok) showToast("✅ 注册成功", "success");
                        else showToast(await res.text() || "❌ 注册失败", "error");
                    } catch (e) { showToast("🌐 网络异常", "error"); }
                };

                const handleLogin = async () => {
                    const username = (loginForm.username || '').trim();
                    const password = loginForm.password || '';
                    if (!username || !password) return showToast("为空！", "error");
                    let fd = new FormData(); fd.append('username', username); fd.append('password', password);
                    try {
                        const res = await authFetch('/api/auth/login', fd);
                        if (res.ok) {
                            const data = await res.json();
                            localStorage.setItem('fund_username', username);
                            currentUser.value = username;
                            userAvatar.value = data.avatarDataUrl || '';
                            isLoggedIn.value = true;
                            await loadPersistentUiState();
                            showToast("接入成功", "success");
                            initDashboard();
                        } else {
                            const msg = await res.text();
                            showToast(msg || "❌ 密码错误", "error");
                        }
                    } catch (error) { showToast("🌐 登录失败", "error"); }
                };

                const handleLogout = () => { localStorage.removeItem('fund_username'); showProfileModal.value = false; location.reload(); };
                const triggerUpload = () => { document.getElementById('ocrInput').click(); };
                const triggerAvatarUpload = () => { document.getElementById('avatarInput')?.click(); };
                const blobToDataUrl = (blob) => new Promise((resolve, reject) => {
                    const r = new FileReader();
                    r.onload = () => resolve(r.result);
                    r.onerror = reject;
                    r.readAsDataURL(blob);
                });

                const clearAvatar = async () => {
                    if (!currentUser.value) return;
                    const endpoints = ['/api/auth/avatar/clear-v3', '/api/auth/avatar/clear-v2', '/api/auth/avatar/clear'];
                    for (const ep of endpoints) {
                        try {
                            const fd = new FormData(); fd.append('username', currentUser.value);
                            const res = await apiFetch(ep, { method: 'POST', body: fd });
                            if (res.ok) { userAvatar.value = ''; showToast('头像已清除', 'success'); return; }
                        } catch (e) { }
                    }
                    showToast('清除失败：头像接口未部署或被网关拦截', 'error');
                };

                const saveAvatarBlob = async (blob) => {
                    const fileEndpoints = ['/api/auth/avatar-file-v3', '/api/auth/avatar-file-v2', '/api/auth/avatar-file'];
                    let lastMsg = '';
                    for (const ep of fileEndpoints) {
                        try {
                            const fd = new FormData();
                            fd.append('username', currentUser.value);
                            fd.append('avatarFile', blob, 'avatar.jpg');
                            const res = await apiFetch(ep, { method: 'POST', body: fd });
                            if (res.ok) return await res.json();
                            lastMsg = await res.text().catch(() => '');
                        } catch (e) { lastMsg = e && e.message ? e.message : String(e); }
                    }

                    // 兜底：部分面板/网关会拦截 multipart，但允许 application/json。
                    const dataUrl = await blobToDataUrl(blob);
                    try {
                        const res = await apiFetch('/api/auth/avatar-json-v3', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ username: currentUser.value, avatarDataUrl: dataUrl })
                        });
                        if (res.ok) return await res.json();
                        lastMsg = await res.text().catch(() => lastMsg);
                    } catch (e) { lastMsg = e && e.message ? e.message : lastMsg; }
                    throw new Error(lastMsg || '头像接口未部署');
                };

                const handleAvatarUpload = (event) => {
                    const file = event.target.files && event.target.files[0];
                    if (!file || !currentUser.value) return;
                    const reader = new FileReader();
                    reader.onload = (e) => {
                        const img = new Image();
                        img.onload = async () => {
                            const size = 180;
                            const canvas = document.createElement('canvas');
                            canvas.width = size; canvas.height = size;
                            const ctx = canvas.getContext('2d');
                            const minSide = Math.min(img.width, img.height);
                            const sx = (img.width - minSide) / 2;
                            const sy = (img.height - minSide) / 2;
                            ctx.drawImage(img, sx, sy, minSide, minSide, 0, 0, size, size);
                            canvas.toBlob(async (blob) => {
                                if (!blob) { showToast('头像压缩失败', 'error'); event.target.value = ''; return; }
                                try {
                                    const saved = await saveAvatarBlob(blob);
                                    userAvatar.value = saved.avatarDataUrl || '';
                                    showToast('头像已更新', 'success');
                                } catch (e) {
                                    showToast('头像保存失败：' + (e && e.message ? e.message : '请确认已部署 AuthController.cs 并重启后端'), 'error');
                                } finally {
                                    event.target.value = '';
                                }
                            }, 'image/jpeg', 0.72);
                        };
                        img.src = e.target.result;
                    };
                    reader.readAsDataURL(file);
                };

                const handleAddFund = async () => {
                    if (!fundForm.code || !fundForm.amount) return showToast("⚠️ 输入完整！", "error");
                    const fd = new FormData(); fd.append('username', currentUser.value); fd.append('code', fundForm.code); fd.append('amount', fundForm.amount);
                    try { const res = await fetch(API_BASE + '/api/fund/add', { method: 'POST', body: fd }); if (res.ok) { fundForm.code = ''; fundForm.amount = ''; fetchData({ force: true, silent: true }); showToast("✅ 成功", "success"); } } catch (e) { showToast("🌐 异常", "error"); }
                };

                const handleDeleteFund = async (code, name) => {
                    if (confirm(`移除 [${name}]？`)) { const fd = new FormData(); fd.append('username', currentUser.value); fd.append('code', code); try { await fetch(API_BASE + '/api/fund/delete', { method: 'POST', body: fd }); fetchData({ force: true, silent: true }); showToast("✅ 移除", "success"); } catch (e) { } }
                };

                const handleFileUpload = async (event) => {
                    const file = event.target.files[0]; if (!file) return; isUploading.value = true;
                    compressImageBeforeUpload(file, async (compressedFile) => {
                        const formData = new FormData(); formData.append('imageFile', compressedFile);
                        try { const response = await fetch(`${API_BASE}/api/Fund/import-ocr?username=${encodeURIComponent(currentUser.value)}`, { method: 'POST', body: formData }); if (response.ok) { ocrModal.content = await response.text(); ocrModal.show = true; fetchData({ force: true, silent: true }); } else { showToast("❌ 识别失败", "error"); } } catch (error) { showToast("🌐 网络异常", "error"); } finally { isUploading.value = false; event.target.value = ''; }
                    });
                };

                const openEditModal = (fund) => { editForm.show = true; editForm.code = (fund.code || '').startsWith('待核对') ? '' : fund.code; editForm.name = fund.name; editForm.cost = fund.cost || 0; editForm.shares = fund.shares || 0; editForm.originalCode = fund.code; };

                const handleSaveDetails = async () => {
                    if (!editForm.cost || editForm.cost <= 0) return showToast("⚠️ 成本必填", "error");
                    const fd = new FormData(); fd.append('username', currentUser.value); fd.append('code', editForm.code); fd.append('costAmount', editForm.cost); fd.append('holdShares', editForm.shares || 0); fd.append('originalCode', editForm.originalCode);
                    try { const res = await fetch(API_BASE + '/api/Fund/update-details', { method: 'POST', body: fd }); if (res.ok) { showToast("✅ 补给完成", "success"); editForm.show = false; fetchData({ force: true, silent: true }); } } catch (e) { showToast("🌐 异常", "error"); }
                };

                const applySectorPayload = (data) => {
                    sectorTopList.value = data.top || [];
                    sectorBottomList.value = data.bottom || [];
                    sectorSource.value = data.source || '';
                    sectorUpdatedAt.value = data.updatedAt || '';
                    sectorCache = { top: sectorTopList.value, bottom: sectorBottomList.value, source: sectorSource.value, updatedAt: sectorUpdatedAt.value };
                    sectorCacheTime = Date.now();
                };
                const readJsonCache = (key, maxAgeMs) => {
                    try {
                        const raw = localStorage.getItem(key);
                        if (!raw) return null;
                        const box = JSON.parse(raw);
                        if (!box || !box.data || (Date.now() - (box.time || 0)) > maxAgeMs) return null;
                        return box.data;
                    } catch (e) { return null; }
                };
                const writeJsonCache = (key, data) => {
                    try { localStorage.setItem(key, JSON.stringify({ time: Date.now(), data })); } catch (e) { }
                };

                let sectorCache = null; let sectorCacheTime = 0; const SECTOR_CACHE_DURATION = 180000;
                const fetchSectors = async (force = false, openModal = true) => {
                    const now = Date.now();
                    if (!force && sectorCache && (now - sectorCacheTime) < SECTOR_CACHE_DURATION) {
                        applySectorPayload(sectorCache);
                        if (openModal) showSectorRadar.value = true;
                        return;
                    }
                    if (!force && sectorTopList.value.length === 0) {
                        const cached = readJsonCache('sector_fast_cache_v1', 30 * 60 * 1000);
                        if (cached) applySectorPayload(cached);
                    }
                    if (openModal) showSectorRadar.value = true;
                    isSectorLoading.value = sectorTopList.value.length === 0;
                    expandedSectors.value = new Set();
                    try {
                        const res = await fetch(`${API_BASE}/api/fund/sectors${force ? '?force=true' : ''}`);
                        if (!res.ok) throw new Error(await res.text());
                        const data = await res.json();
                        applySectorPayload(data);
                        writeJsonCache('sector_fast_cache_v1', data);
                    } catch (e) {
                        if (sectorTopList.value.length === 0) showToast('🚨 板块雷达连接失败', 'error');
                    } finally {
                        isSectorLoading.value = false;
                    }
                };

                let capitalCacheTime = 0;
                const isBoardFlowName = (name = '') => {
                    const text = String(name || '').trim();
                    if (!text) return false;
                    const blocked = ['融资融券', '富时罗素', '标准普尔', 'MSCI', '沪股通', '深股通', '大盘股', 'HS300', '基金重仓', '昨日涨停', '昨日连板', '昨日触板', '昨日高振幅', '昨日首板', '昨日连板', '证金持股', '养老金', '中字头', '上证', '深证', '北证', '年报预增', '央国企改革', '机构重仓', '东方财富', '龙虎榜', '融资客', '参股', '破净', '高股息', '预增', '高送转', 'ST板块', '含可转债', '同花顺', '中证500', '深成500'];
                    if (blocked.some(k => text.includes(k))) return false;
                    if (/^(90\.|BK|[0-9A-Za-z_.-]+$)/.test(text)) return false;
                    return true;
                };
                const normalizeCapitalRows = (rows = []) => (Array.isArray(rows) ? rows : []).filter(x => isBoardFlowName(x?.name));
                const applyCapitalPayload = (data) => {
                    const rows = normalizeCapitalRows(data.rows || []);
                    capitalFlowRows.value = rows;
                    capitalFlowInRows.value = normalizeCapitalRows(data.inflow || rows).filter(x => (x.mainNet || 0) > 0).sort((a, b) => (b.mainNet || 0) - (a.mainNet || 0));
                    capitalFlowOutRows.value = normalizeCapitalRows(data.outflow || rows).filter(x => (x.mainNet || 0) < 0).sort((a, b) => (a.mainNet || 0) - (b.mainNet || 0));
                    capitalFlowSource.value = data.source || '东方财富板块资金流向';
                    capitalFlowUpdatedAt.value = data.updatedAt || '';
                    const message = String(data.message || '');
                    capitalFlowError.value = data.isFallback
                        ? '使用缓存数据'
                        : (message || '');
                    capitalCacheTime = Date.now();
                };
                const fetchCapitalFlow = async (force = false) => {
                    const now = Date.now();
                    if (!force && capitalFlowRows.value.length > 0 && (now - capitalCacheTime) < 180000) return;
                    if (!force && capitalFlowRows.value.length === 0) {
                        const cached = readJsonCache('capital_flow_cache_v1', 20 * 60 * 1000);
                        if (cached) applyCapitalPayload(cached);
                    }
                    isCapitalLoading.value = capitalFlowRows.value.length === 0;
                    try {
                        const res = await fetch(`${API_BASE}/api/fund/capital-flow?limit=40${force ? '&force=true' : ''}`);
                        if (!res.ok) throw new Error(await res.text());
                        const data = await res.json();
                        applyCapitalPayload(data);
                        writeJsonCache('capital_flow_cache_v1', data);
                    } catch (e) {
                        capitalFlowError.value = force && e?.message
                            ? `主力资金流暂不可用：${String(e.message).slice(0, 80)}`
                            : '主力资金流暂不可用';
                    } finally {
                        isCapitalLoading.value = false;
                    }
                };
                let echartsLoadingPromise = null;
                const ensureEcharts = () => {
                    if (window.echarts) return Promise.resolve(window.echarts);
                    if (echartsLoadingPromise) return echartsLoadingPromise;
                    echartsLoadingPromise = new Promise((resolve, reject) => {
                        const existing = document.getElementById('lazy-echarts');
                        if (existing) {
                            existing.addEventListener('load', () => resolve(window.echarts), { once: true });
                            existing.addEventListener('error', reject, { once: true });
                            return;
                        }
                        const script = document.createElement('script');
                        script.id = 'lazy-echarts';
                        script.src = 'https://cdn.jsdelivr.net/npm/echarts@5.5.0/dist/echarts.min.js';
                        script.async = true;
                        script.onload = () => resolve(window.echarts);
                        script.onerror = reject;
                        document.body.appendChild(script);
                    });
                    return echartsLoadingPromise;
                };

                const syncRealNavIfNeeded = async () => {
                    if (!currentUser.value) return;
                    const now = new Date();
                    const minutes = now.getHours() * 60 + now.getMinutes();
                    if (minutes < 17 * 60) return;

                    const controller = new AbortController();
                    const timeoutId = setTimeout(() => controller.abort(), 12000);
                    try {
                        const fd = new FormData();
                        fd.append('username', currentUser.value);
                        await fetch(`${API_BASE}/api/fund/sync-real-nav`, {
                            method: 'POST',
                            body: fd,
                            signal: controller.signal,
                            cache: 'no-store'
                        });
                    } catch (e) {
                        // 外部净值源偶发慢，不阻断本地数据刷新。
                    } finally {
                        clearTimeout(timeoutId);
                    }
                };

                const refreshNow = async (force = true) => {
                    if (pullRefresh.loading || isFetchingData) return;
                    pullRefresh.loading = true;
                    try {
                        if (force) await syncRealNavIfNeeded();
                        await fetchData({ force, silent: true });
                        showToast('✅ 数据已刷新', 'success');
                    } finally {
                        pullRefresh.loading = false;
                        pullRefresh.show = false;
                        pullRefresh.distance = 0;
                    }
                };

                const isAtPageTop = () => {
                    const docTop = document.documentElement.scrollTop || 0;
                    const bodyTop = document.body.scrollTop || 0;
                    return window.scrollY <= 2 && docTop <= 2 && bodyTop <= 2;
                };

                const isPullRefreshGestureAllowed = (e) => {
                    if (!isLoggedIn.value || pullRefresh.loading || isFetchingData) return false;
                    if (activePage.value !== 'home') return false;
                    if (!isAtPageTop()) return false;
                    if (!e.touches || e.touches.length !== 1) return false;

                    const target = e.target;
                    if (target && target.closest) {
                        // 弹窗、表单、按钮、内层滚动容器禁止触发，避免用户滑新闻/档案/弹窗时被误判为刷新。
                        const blocked = target.closest('.modal-mask, .modal-overlay, .news-page-list, .sector-page-layout, .news-page-layout, .analysis-grid, .archive-scroll, .scrollable, .no-pull-refresh, input, textarea, select, button');
                        if (blocked) return false;
                    }
                    return true;
                };

                const resetPullRefresh = () => {
                    pullRefresh.active = false;
                    pullRefresh.show = false;
                    pullRefresh.distance = 0;
                };

                const onPullStart = (e) => {
                    if (!isPullRefreshGestureAllowed(e)) return;
                    const touch = e.touches[0];
                    pullRefresh.startY = touch.clientY;
                    pullRefresh.startX = touch.clientX;
                    pullRefresh.distance = 0;
                    pullRefresh.active = true;
                };

                const onPullMove = (e) => {
                    if (!pullRefresh.active || pullRefresh.loading) return;
                    if (activePage.value !== 'home' || !isAtPageTop()) { resetPullRefresh(); return; }
                    const touch = e.touches && e.touches[0];
                    if (!touch) return;

                    const dy = touch.clientY - pullRefresh.startY;
                    const dx = touch.clientX - pullRefresh.startX;

                    // 向上滑、横向滑、斜向滑都不算刷新；必须是首页顶部明显向下拖。
                    if (dy <= 0) { resetPullRefresh(); return; }
                    if (Math.abs(dx) > 24 && Math.abs(dx) > dy * 0.65) { resetPullRefresh(); return; }
                    if (dy < 18) return;

                    pullRefresh.distance = Math.min(Math.round((dy - 14) * 0.38), 86);
                    pullRefresh.show = pullRefresh.distance > 10;

                    if (pullRefresh.show && e.cancelable) e.preventDefault();
                };

                const onPullEnd = () => {
                    if (!pullRefresh.active) return;
                    const shouldRefresh = activePage.value === 'home' && pullRefresh.distance >= 72;
                    pullRefresh.active = false;
                    if (shouldRefresh) refreshNow(true);
                    else {
                        pullRefresh.show = false;
                        pullRefresh.distance = 0;
                    }
                };


                const fetchAnalysis = async (force = false) => {
                    if (!currentUser.value) return;
                    if (analysisRecords.value.length > 0 && !force) return;
                    analysisLoading.value = true;
                    try {
                        const url = `${API_BASE}/api/fund/get-archives?username=${encodeURIComponent(currentUser.value)}&limit=500&_t=${Date.now()}`;
                        const res = await fetch(url, { cache: 'no-store' });
                        if (!res.ok) throw new Error(await res.text());
                        analysisRecords.value = await res.json();
                        const monthRecords = analysisTotalRecords.value.filter(r => String(r.recordDate || '').slice(0, 7) === analysisMonth.value);
                        if (monthRecords.length > 0 && !monthRecords.some(r => String(r.recordDate || '').slice(0, 10) === selectedAnalysisDate.value)) {
                            selectedAnalysisDate.value = String(monthRecords[0].recordDate || '').slice(0, 10);
                        }
                    } catch (e) {
                        showToast('📊 盈亏档案读取失败', 'error');
                    } finally {
                        analysisLoading.value = false;
                    }
                };

                const switchPage = async (page) => {
                    activePage.value = page;
                    window.scrollTo({ top: 0, behavior: 'smooth' });
                    if (page === 'sector') { await fetchSectors(false, false); fetchCapitalFlow(false); }
                    if (page === 'news') fetchNews(newsMode.value, false);
                    if (page === 'analysis') { persistAnalysisPreferences(); await fetchAnalysis(false); }
                    if (page === 'home') nextTick(() => { if (chartInstance.value) chartInstance.value.resize(); });
                };

                const fetchData = async (options = {}) => {
                    if (!currentUser.value) return;
                    if (isFetchingData) return;
                    const force = !!options.force;
                    const silent = !!options.silent;
                    isFetchingData = true;
                    if (!silent && fundsList.value.length === 0) isLoading.value = true;
                    try {
                        const forceParam = force ? '&force=true' : '';
                        const response = await fetch(`${API_BASE}/api/fund/today?username=${encodeURIComponent(currentUser.value)}${forceParam}`, { cache: force ? 'no-store' : 'default' });
                        if (!response.ok) throw new Error('today接口异常');
                        const apiData = await response.json();
                        lastDataFetchAt = Date.now();
                        let tPrincipal = 0; let tPrincipalForRate = 0; let tCost = 0; let tToday = 0; let d = new Date();
                        let todaySlash = `${d.getFullYear()}/${String(d.getMonth() + 1).padStart(2, '0')}/${String(d.getDate()).padStart(2, '0')}`;
                        let todayDash = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
                        let currentMinutes = d.getHours() * 60 + d.getMinutes(); let isWeekend = (d.getDay() === 0 || d.getDay() === 6);
                        await loadCalibrationProfiles();
                        const intradayCalibrationPayload = [];
                        const settledCalibrationPayload = [];

                        apiData.forEach(fund => {
                            if (fund.isSettled) { fund.currentRate = fund.actualRate; fund.isHoliday = false; if (fund.data.length > 0 && String(fund.data[fund.data.length - 1][0]).includes("15:00:00")) { fund.data[fund.data.length - 1][1] = fund.actualRate; } else { fund.data.push([`${todaySlash} 15:00:00`, fund.actualRate]); } } else if (isWeekend || currentMinutes < 565) { fund.currentRate = 0; fund.isHoliday = true; } else if (fund.data && fund.data.length > 0) { let lastDataTime = String(fund.data[fund.data.length - 1][0]); let hasTodayDate = lastDataTime.includes(todaySlash) || lastDataTime.includes(todayDash); let isPast935 = (d.getHours() > 9) || (d.getHours() === 9 && d.getMinutes() >= 35); if (hasTodayDate && !(isPast935 && fund.data.length <= 2)) { fund.currentRate = fund.data[fund.data.length - 1][1]; fund.isHoliday = false; } else { fund.currentRate = 0; fund.isHoliday = true; } } else { fund.currentRate = 0; fund.isHoliday = true; }

                            const rawCurrentRate = Number(fund.currentRate || 0);
                            if (fund.isSettled) {
                                settledCalibrationPayload.push({
                                    fundCode: fund.code,
                                    fundName: fund.name,
                                    tradeDate: todayDash,
                                    estimatedRate: rawCurrentRate,
                                    actualRate: Number(fund.actualRate ?? fund.lastSettledRate ?? fund.currentRate ?? 0),
                                    estimatedAssets: Number(fund.amount || 0),
                                    actualAssets: Number(fund.todayAmount || fund.amount || 0)
                                });
                            }
                            const calibrationProfile = getCalibrationProfileByCode(fund.code);
                            fund.rawCurrentRate = rawCurrentRate;
                            fund.calibrationOffset = Number(calibrationProfile.offset.toFixed(2));
                            fund.calibrationSamples = calibrationProfile.samples;
                            fund.calibrationLastError = Number(calibrationProfile.lastError.toFixed(2));
                            fund.calibrationConfidence = calibrationProfile.confidence;
                            if (!fund.isSettled && !fund.isHoliday && calibrationProfile.samples >= 1) {
                                fund.currentRate = Number((rawCurrentRate + calibrationProfile.offset).toFixed(2));
                                fund.calibrationNote = `原估 ${rawCurrentRate >= 0 ? '+' : ''}${rawCurrentRate.toFixed(2)}% · ${calibrationProfile.samples}天样本${calibrationProfile.confidence}可信`;
                                intradayCalibrationPayload.push({ fundCode: fund.code, fundName: fund.name, tradeDate: todayDash, estimatedRate: rawCurrentRate, estimatedAssets: Number(fund.amount || 0), source: 'today-api' });
                            } else if (!fund.isSettled && !fund.isHoliday) {
                                fund.calibrationNote = '正在积累盘中估值样本';
                                intradayCalibrationPayload.push({ fundCode: fund.code, fundName: fund.name, tradeDate: todayDash, estimatedRate: rawCurrentRate, estimatedAssets: Number(fund.amount || 0), source: 'today-api' });
                            } else if (fund.isSettled && calibrationProfile.samples > 0) {
                                fund.calibrationNote = `盘后已校准 · 最近误差 ${calibrationProfile.lastError >= 0 ? '+' : ''}${calibrationProfile.lastError.toFixed(2)}%`;
                            } else {
                                fund.calibrationNote = '';
                            }

                            let isUnconfirmed = fund.lastTradeDate && (fund.lastTradeDate >= todayDash);
                            let todayAddAmount = isUnconfirmed ? (Number(fund.lastAddAmount) || 0) : 0;
                            let isAlreadySettled = (fund.lastSettledDate === todayDash);
                            let currentAmount = Number(fund.amount) || 0;
                            let todayProfitVal = 0;
                            let currentRealTimeAmount = currentAmount;

                            if (isAlreadySettled) {
                                todayProfitVal = Number(fund.lastSettledProfit) || 0;
                                fund.todayProfit = todayProfitVal.toFixed(2);
                                currentRealTimeAmount = currentAmount;
                                fund.currentRate = Number(fund.lastSettledRate) || fund.currentRate || 0;
                            } else if (fund.isSettled && fund.actualExactProfit != null) {
                                todayProfitVal = Number(fund.actualExactProfit) || 0;
                                fund.todayProfit = todayProfitVal.toFixed(2);
                                currentRealTimeAmount = currentAmount + todayProfitVal;
                            } else {
                                let baseAmount = currentAmount - todayAddAmount;
                                todayProfitVal = baseAmount * ((Number(fund.currentRate) || 0) / 100);
                                fund.todayProfit = todayProfitVal.toFixed(2);
                                currentRealTimeAmount = currentAmount + todayProfitVal;
                            }

                            fund.todayAmount = currentRealTimeAmount.toFixed(2);
                            if (!isNaN(todayProfitVal)) { tToday += todayProfitVal; }

                            fund.realizedProfit = Number(fund.realizedProfit) || 0;
                            const floatProfit = (fund.cost && fund.cost > 0) ? (currentRealTimeAmount - fund.cost) : 0;
                            const allProfit = floatProfit + fund.realizedProfit;
                            fund.estimatedProfit = allProfit.toFixed(2);
                            fund.breakEvenRate = (fund.cost && fund.cost > currentRealTimeAmount) ? (((fund.cost / currentRealTimeAmount) - 1) * 100).toFixed(2) : "0.00";
                            fund.existingReturnRate = (fund.cost && fund.cost > 0) ? ((allProfit / fund.cost) * 100).toFixed(2) : "0.00";

                            const rateBaseForToday = isAlreadySettled
                                ? (currentAmount - todayProfitVal - todayAddAmount)
                                : (currentAmount - todayAddAmount);
                            tPrincipal += currentAmount; tPrincipalForRate += rateBaseForToday; tCost += (fund.cost || currentAmount);
                        });

                        fundsList.value = apiData;
                        const tRealized = fundsList.value.reduce((sum, f) => sum + (f.realizedProfit || 0), 0);
                        totalPrincipal.value = tPrincipal; totalTodayProfit.value = tToday;
                        totalTodayRate.value = tPrincipalForRate > 0 ? (tToday / tPrincipalForRate * 100) : 0;
                        const currentTotalAssets = fundsList.value.reduce((sum, f) => sum + (parseFloat(f.todayAmount || f.amount) || 0), 0);
                        totalProfit.value = currentTotalAssets - tCost + tRealized;
                        totalRate.value = tCost > 0 ? ((currentTotalAssets - tCost + tRealized) / tCost * 100) : 0;

                        saveIntradayCalibrationSnapshots(intradayCalibrationPayload);
                        settleCalibrationSamples(settledCalibrationPayload);
                        saveUiState('dashboard_summary', {
                            date: todayDash,
                            totalPrincipal: Number(totalPrincipal.value || 0),
                            totalTodayProfit: Number(totalTodayProfit.value || 0),
                            totalTodayRate: Number(totalTodayRate.value || 0),
                            totalProfit: Number(totalProfit.value || 0),
                            totalRate: Number(totalRate.value || 0)
                        });
                        saveInsightSnapshot('dashboard', {
                            dailyBattleReport: dailyBattleReport.value,
                            confidenceRows: confidenceRows.value,
                            recoveryWatchList: recoveryWatchList.value,
                            portfolioExposure: portfolioExposure.value
                        }, todayDash);

                        // 真实净值确认后，自动重写一次今日档案。
                        // 用“已确认基金代码+真实收益/涨跌幅”的指纹判断，后续更多基金确认时会再次覆盖。
                        // 不再使用旧的 real_archive 固定锁，避免旧版本写坏 TOTAL 后无法修正。
                        const settledFingerprint = fundsList.value
                            .filter(f => f.isSettled)
                            .map(f => `${f.code}:${f.lastSettledProfit || f.actualExactProfit || 0}:${f.lastSettledRate || f.actualRate || 0}`)
                            .sort()
                            .join('|');
                        const realArchiveKey = `real_archive_v4_${currentUser.value}_${todayDash}`;
                        if (settledFingerprint && localStorage.getItem(realArchiveKey) !== settledFingerprint) {
                            try {
                                await saveDailyArchive(true);
                                localStorage.setItem(realArchiveKey, settledFingerprint);
                            } catch (e) { }
                        }

                        if (!window.hasAutoSavedToday && !isNight.value && fundsList.value.length > 0) {
                            const now = new Date(); const hour = now.getHours(); const isAfter15 = hour >= 15;
                            if (isAfter15) {
                                try {
                                    const firstFund = fundsList.value[0]; const todayStr = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
                                    window.jsonpgz = function (data) { if (data && data.gztime && data.gztime.startsWith(todayStr) && data.gztime >= todayStr + ' 15:00') { saveDailyArchive(true); window.hasAutoSavedToday = true; } delete window.jsonpgz; };
                                    const scriptId = `jsonp_check_${firstFund.code}`; let oldScript = document.getElementById(scriptId); if (oldScript) oldScript.remove();
                                    const script = document.createElement('script'); script.id = scriptId; script.src = `https://fundgz.1234567.com.cn/js/${firstFund.code}.js?rt=${Date.now()}`; document.body.appendChild(script);
                                } catch (e) { }
                            }
                        }
                        isLoading.value = false;
                        const runChart = () => renderChart(apiData);
                        if ('requestIdleCallback' in window) requestIdleCallback(runChart, { timeout: 800 });
                        else requestAnimationFrame(runChart);
                    } catch (e) {
                        isLoading.value = false;
                        if (!silent) showToast("🌐 数据刷新失败", "error");
                    } finally {
                        isFetchingData = false;
                    }
                };

                const handleResize = () => { if (chartInstance.value) chartInstance.value.resize(); };
                let lastChartDataHash = '';
                const renderChart = async (apiData) => {
                    if (!chartRef.value || activePage.value !== 'home') return;
                    try { await ensureEcharts(); } catch (e) { return; }
                    const currentHash = JSON.stringify(apiData.map(f => ({ code: f.code, data: f.data?.slice(-5) })));
                    if (currentHash === lastChartDataHash) { return; } lastChartDataHash = currentHash;
                    if (!chartInstance.value) { chartInstance.value = echarts.init(chartRef.value, 'dark'); window.addEventListener('resize', handleResize); }
                    var holdAmounts = {}; var validData = apiData.filter(f => f.data && f.data.length > 1 && !f.isHoliday);
                    var seriesData = validData.map(fund => {
                        holdAmounts[fund.name] = fund.amount; let diff = 0;
                        if (fund.isSettled && fund.data.length >= 2) { let realRate = fund.actualRate; let estRate = fund.data[fund.data.length - 2][1]; diff = (realRate - estRate).toFixed(2); }
                        fund._diff = diff;
                        return {
                            name: fund.name, type: 'line', smooth: true, symbol: 'none', lineStyle: { width: 3, shadowColor: 'rgba(0,0,0,0.5)', shadowBlur: 10 },
                            endLabel: {
                                show: true, color: 'inherit', fontSize: 13, fontWeight: 'bold', lineHeight: 20, formatter: (p) => {
                                    let profit = (fund.amount * p.value[1] / 100).toFixed(2); let sign = profit >= 0 ? '+' : ''; let profitDisplay = privacyMode.value <= 1 ? `${sign}${profit} 元` : '**** 元'; let rateDisplay = privacyMode.value <= 2 ? `${p.value[1]}%` : '****';
                                    if (fund.isSettled) { let diffSign = fund._diff > 0 ? '+' : ''; return `${profitDisplay}\n✅ 真: ${rateDisplay}\n差: ${diffSign}${fund._diff}%`; } else if (isNight.value && fund.data.length > 50) { return `${profitDisplay}\n⏳ 等待财报...`; } else { return `${profitDisplay}\n估: ${rateDisplay}`; }
                                }
                            }, data: fund.data
                        };
                    });
                    var d = new Date(); var todayStr = d.getFullYear() + '/' + String(d.getMonth() + 1).padStart(2, '0') + '/' + String(d.getDate()).padStart(2, '0');
                    chartInstance.value.setOption({
                        backgroundColor: 'transparent',
                        tooltip: {
                            trigger: 'axis', backgroundColor: 'rgba(15, 23, 42, 0.9)', borderColor: '#334155', textStyle: { color: '#f8fafc' }, formatter: function (params) {
                                let html = `<div style="margin-bottom:5px;color:#94a3b8;">${params[0].axisValueLabel}</div>`;
                                params.forEach(p => {
                                    let profit = (holdAmounts[p.seriesName] * p.data[1] / 100).toFixed(2); let color = profit >= 0 ? '#ff4d4f' : '#10b981'; let originalFund = apiData.find(f => f.name === p.seriesName); let labelSuffix = '估';
                                    if (isNight.value && originalFund && originalFund.data && p.dataIndex === originalFund.data.length - 1) { labelSuffix = originalFund.isSettled ? '✅ 真' : '⏳ 待更新'; }
                                    let profitDisplay = privacyMode.value <= 1 ? `${profit >= 0 ? '+' : ''}${profit} 元` : '**** 元'; let rateDisplay = privacyMode.value <= 2 ? `${p.data[1]}%` : '****';
                                    html += `<div>${p.marker} ${p.seriesName}: <span style="color:${color};font-weight:bold;">${profitDisplay}</span> (${rateDisplay} [${labelSuffix}])</div>`;
                                });
                                return html;
                            }
                        },
                        legend: { top: '0%', textStyle: { color: '#cbd5e1' } }, grid: { left: '3%', right: '15%', bottom: '3%', containLabel: true },
                        xAxis: { type: 'time', min: todayStr + ' 09:30:00', max: todayStr + ' 16:00:00', splitLine: { show: false } }, yAxis: { type: 'value', splitLine: { lineStyle: { color: 'rgba(255,255,255,0.05)', type: 'dashed' } } }, series: seriesData
                    }, true);
                };

                watch(privacyMode, () => { if (fundsList.value.length > 0) renderChart(fundsList.value); });

                const updateRadarMode = () => { let d = new Date(); let time = d.getHours() * 100 + d.getMinutes(); isNight.value = (d.getDay() === 0 || d.getDay() === 6 || time < 925 || time > 1605); };

                // ✅ 核心修复：在这里定义防抖，确保 fetchData 已经初始化！
                const debounce = (fn, delay) => { let t = null; return (...args) => { clearTimeout(t); t = setTimeout(() => fn(...args), delay); }; };
                const debouncedFetchData = debounce(fetchData, 500);

                const handleVisibilityChange = () => { if (document.visibilityState === 'visible') { updateRadarMode(); } };

                const initDashboard = async () => { await loadPersistentUiState(); await loadCalibrationProfiles(); nextTick(() => { updateRadarMode(); setTimeout(() => { fetchData({ force: false }); if (assetMode.value === 'stock') loadStockDashboard(false); warmNewsCache(); }, 120); }); };

                const fetchUserProfile = async () => {
                    if (!currentUser.value) return;
                    try {
                        let res = await apiFetch('/api/auth/profile-v3?username=' + encodeURIComponent(currentUser.value));
                        if (!res.ok) res = await apiFetch('/api/auth/profile-v2?username=' + encodeURIComponent(currentUser.value));
                        if (!res.ok) res = await apiFetch('/api/auth/profile?username=' + encodeURIComponent(currentUser.value));
                        if (res.ok) { const data = await res.json(); userAvatar.value = data.avatarDataUrl || ''; }
                    } catch (e) { }
                };

                watch([analysisMonth, analysisViewMode, analysisDetailMode], () => persistAnalysisPreferences());
                watch(() => recoveryModal.addAmount, (value) => {
                    if (recoveryModal.code) saveUiState(`recovery_amount_${recoveryModal.code}`, { amount: Number(value || 0) });
                });

                onMounted(() => { if (currentUser.value) { isLoggedIn.value = true; fetchUserProfile(); initDashboard(); } document.addEventListener('visibilitychange', handleVisibilityChange); window.addEventListener('touchstart', onPullStart, { passive: true }); window.addEventListener('touchmove', onPullMove, { passive: false }); window.addEventListener('touchend', onPullEnd, { passive: true }); });

                const todayStr = new Date(new Date().getTime() + 8 * 3600 * 1000).toISOString().split('T')[0];
                const addModal = reactive({ show: false, code: '', name: '', amount: '', rawDate: todayStr, timeSlot: 'before', currentAmount: 0, currentCost: 0 });
                const reduceModal = reactive({ show: false, code: '', name: '', shares: '', amount: '', rawDate: todayStr, timeSlot: 'before', currentAmount: 0, currentShares: 0 });

                const calculateTradeDate = (rawDateStr, timeSlot) => { let d = new Date(rawDateStr); if (timeSlot === 'after') { d.setDate(d.getDate() + 1); } while (d.getDay() === 0 || d.getDay() === 6) { d.setDate(d.getDate() + 1); } return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };

                const openAddModal = (fund) => { addModal.show = true; addModal.code = fund.code; addModal.name = fund.name; addModal.amount = ''; addModal.currentAmount = fund.amount; addModal.currentCost = fund.cost > 0 ? fund.cost : '未设置'; const now = new Date(); addModal.rawDate = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`; addModal.timeSlot = now.getHours() >= 15 ? 'after' : 'before'; };

                const handleAddPosition = async () => { if (!addModal.amount) return showToast("请输入加仓金额", "error"); const tradeDate = calculateTradeDate(addModal.rawDate, addModal.timeSlot); const fd = new FormData(); fd.append('username', currentUser.value); fd.append('code', addModal.code); fd.append('addAmount', addModal.amount); fd.append('tradeDate', tradeDate); try { const res = await fetch(API_BASE + '/api/Fund/add-position', { method: 'POST', body: fd }); const result = await res.json(); if (res.ok) { showToast(result.msg, "success"); addModal.show = false; fetchData({ force: true, silent: true }); } else showToast(result.msg || "加仓失败", "error"); } catch (e) { showToast("网络异常", "error"); } };

                const openReduceModal = (fund) => { reduceModal.show = true; reduceModal.code = fund.code; reduceModal.name = fund.name; reduceModal.shares = ''; reduceModal.amount = ''; reduceModal.currentAmount = fund.amount; reduceModal.currentShares = fund.shares || 0; const now = new Date(); reduceModal.rawDate = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`; reduceModal.timeSlot = now.getHours() >= 15 ? 'after' : 'before'; };

                const handleReducePosition = async () => { if (!reduceModal.shares || !reduceModal.rawDate) return showToast("情报不完整，请检查份额或日期", "error"); const tradeDate = calculateTradeDate(reduceModal.rawDate, reduceModal.timeSlot); const fd = new FormData(); fd.append('username', currentUser.value); fd.append('code', reduceModal.code); fd.append('reduceShares', reduceModal.shares); fd.append('reduceAmount', reduceModal.amount || 0); fd.append('tradeDate', tradeDate); try { const res = await fetch(API_BASE + '/api/Fund/reduce-position', { method: 'POST', body: fd }); const result = await res.json(); if (res.ok) { showToast(result.msg || "结转成功", "success"); reduceModal.show = false; fetchData({ force: true, silent: true }); } else showToast(result.msg || "核算失败", "error"); } catch (e) { showToast("网络异常", "error"); } };

                onUnmounted(() => { if (timer) clearInterval(timer); window.removeEventListener('resize', handleResize); document.removeEventListener('visibilitychange', handleVisibilityChange); window.removeEventListener('touchstart', onPullStart); window.removeEventListener('touchmove', onPullMove); window.removeEventListener('touchend', onPullEnd); if (chartInstance.value) chartInstance.value.dispose(); if (stockChartInstance.value) stockChartInstance.value.dispose(); });

                return {
                    toasts, isLoggedIn, currentUser, fundsList, chartRef, loginForm, fundForm, isLoading, isNight, pullRefresh, refreshNow, activePage, switchPage, showProfileModal, userAvatar,
                    totalPrincipal, totalProfit, totalRate, totalTodayProfit, privacyMode, showPrivacyModal, setPrivacyMode, isUploading, editForm,
                    handleLogin, handleRegister, handleLogout, handleAddFund, handleDeleteFund, triggerUpload, handleFileUpload, triggerAvatarUpload, handleAvatarUpload, clearAvatar,
                    openEditModal, handleSaveDetails, totalTodayRate, showSectorRadar, isSectorLoading, fetchSectors,
                    sectorTopList, sectorBottomList, sectorMode, sectorSource, sectorUpdatedAt, capitalFlowRows, capitalFlowInRows, capitalFlowOutRows, capitalFlowSource, capitalFlowUpdatedAt, capitalFlowError, isCapitalLoading, fetchCapitalFlow,
                    showSectorDetails, detailsTitle, detailsList, sectorDetailMeta, isDetailsLoading, expandedSectors, toggleSector,
                    fetchSectorDetails, historyModal, fetchHistory, showGlobalIndices, isIndicesLoading, fetchGlobalIndices,
                    showNewsPanel, isNewsLoading, newsMode, newsOnlyImportant, newsList, newsSource, newsUpdatedAt, groupedNews, openNewsPanel, fetchNews, switchNewsMode, openNewsItem,
                    analysisMonth, selectedAnalysisDate, analysisViewMode, analysisDetailMode, analysisLoading, analysisRecords, analysisCalendarDays, selectedDayFundRecords, selectedDayTotalProfitRecords, analysisProfitTop, analysisLossTop, analysisMonthTotal, fetchAnalysis, todayDashStr,
                    assetMode, setAssetMode, stockHoldings, stockWatchList, stockSearchKeyword, stockSearchResults, stockOcrLoading, stockOcrPreview, selectedStock, stockKlinePeriod, stockKlinePeriods, stockChartRef, triggerStockOcr, handleStockOcrUpload, confirmStockOcr, loadStockDashboard, searchStocks, addStockWatch, openStockHoldingEditor, deleteStockHolding, deleteStockWatch, openStockDetail, switchStockKline, dailyBattleReport, confidenceRows, getConfidence, portfolioExposure, newsImpactSummary, recoveryWatchList, recoveryModal, customRecoveryPlan, openRecoveryModal, calibrationRows,
                    globalIndices, viewIndexHistory, archiveModal, fetchMyArchives, saveDailyArchive, ocrModal,
                    reduceModal, openReduceModal, handleReducePosition, addModal, openAddModal, handleAddPosition
                };
            }
        }).mount('#app');
