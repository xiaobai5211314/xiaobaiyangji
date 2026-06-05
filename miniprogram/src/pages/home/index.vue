<template>
  <view :class="['page-shell', 'home-page', themeClass]">
    <view class="page-header home-header compact-header">
      <view class="title-block">
        <text class="page-subtitle compact-subtitle">持仓、收益与净值参考</text>
      </view>
      <text class="chip">{{ headerCountText }}</text>
    </view>

    <view v-if="dashboardStale && !loading" class="dashboard-stale-bar">
      <text class="dashboard-stale-text">{{ dashboardUpdatedAt }}</text>
    </view>

    <view class="glass-card action-card">
      <view class="action-buttons">
        <button class="ocr-button" :disabled="ocrBusy" @tap="handleSmartOcr">
          {{ ocrButtonText }}
        </button>
      </view>
      <view class="user-panel">
        <button :class="['account-button', sessionState.username ? 'logged-in' : 'guest']" @tap="openProfile">
          <image v-if="sessionState.username && avatarUrl" class="avatar-img" :src="avatarUrl" mode="aspectFill" />
          <view v-else-if="sessionState.username" class="avatar-fallback">
            <text>{{ avatarText }}</text>
          </view>
          <view class="account-copy">
            <text class="account-title">{{ accountEntryTitle }}</text>
            <text class="account-subtitle">{{ accountEntrySubtitle }}</text>
          </view>
        </button>
      </view>
      <button class="privacy-button" @tap="togglePrivacyMode">{{ privacyLabel }}</button>
    </view>

    <view class="asset-switch glass-card">
      <button :class="['asset-switch-btn', assetMode === 'fund' ? 'active' : '']" @tap="setAssetMode('fund')">基金</button>
      <button :class="['asset-switch-btn', assetMode === 'stock' ? 'active' : '']" @tap="setAssetMode('stock')">股票</button>
    </view>

    <view class="glass-card notice-card">
      <view class="notice-text">数据仅供个人记录与行情参考，不构成投资建议，实际数据以基金公司、交易所或券商披露为准。</view>
    </view>

    <view v-if="assetMode === 'fund'" class="mode-pane">
    <view class="glass-card hero-card">
      <view class="hero-top">
        <view>
          <text class="muted-text">持仓市值</text>
          <text class="hero-sub">按 WebApp 持仓口径计算</text>
        </view>
        <text class="chip">{{ funds.length }} 只基金</text>
      </view>
      <text class="finance-number hero-money">{{ displayMoney(metrics.totalAssets, 0, false) }}</text>

      <view class="summary-grid">
        <view class="summary-cell">
          <text class="metric-label">持仓本金</text>
          <text class="metric-value finance-number">{{ displayMoney(metrics.totalCost, 0, false) }}</text>
        </view>
        <view class="summary-cell">
          <text class="metric-label">昨日总市值</text>
          <text class="metric-value finance-number">{{ displayMoney(metrics.totalPrincipal, 0, false) }}</text>
        </view>
        <view class="summary-cell glow-cell">
          <text class="metric-label">今日收益</text>
          <text :class="['metric-value', 'finance-number', profitClass(metrics.totalTodayProfit)]">
            {{ displayMoney(metrics.totalTodayProfit, 1, true) }}
          </text>
        </view>
        <view class="summary-cell">
          <text class="metric-label">今日收益率</text>
          <text :class="['metric-value', 'finance-number', profitClass(metrics.totalTodayRate)]">
            {{ displayPercent(metrics.totalTodayRate, 2) }}
          </text>
        </view>
        <view class="summary-cell glow-cell">
          <text class="metric-label">累计盈亏</text>
          <text :class="['metric-value', 'finance-number', profitClass(metrics.totalProfit)]">
            {{ displayMoney(metrics.totalProfit, 1, true) }}
          </text>
        </view>
        <view class="summary-cell">
          <text class="metric-label">累计收益率</text>
          <text :class="['metric-value', 'finance-number', profitClass(metrics.totalRate)]">
            {{ displayPercent(metrics.totalRate, 2) }}
          </text>
        </view>
      </view>
    </view>

    <view class="feature-grid">
      <view class="glass-card feature-card">
        <text class="feature-title">今日收盘战报</text>
        <text class="feature-subtitle">{{ metrics.dailyBattleReport.summary }}</text>
        <text :class="['feature-number', 'finance-number', profitClass(metrics.totalTodayProfit)]">
          {{ maskByPrivacy(metrics.dailyBattleReport.todayProfitText, privacyMode, 1) }}
        </text>
        <view class="report-line">
          <text>贡献核心</text>
          <text class="profit-text">{{ metrics.dailyBattleReport.bestName }}</text>
        </view>
        <view class="report-line">
          <text>风险拖累</text>
          <text class="loss-text">{{ metrics.dailyBattleReport.worstName }}</text>
        </view>
        <view class="report-line">
          <text>记录提醒</text>
          <text class="warning-text">{{ metrics.dailyBattleReport.actionHint }}</text>
        </view>
      </view>

      <view class="glass-card feature-card">
        <text class="feature-title">估值参考雷达</text>
        <text class="feature-subtitle">净值参考、休市和偏离度综合评分</text>
        <view v-for="item in confidenceRows" :key="`conf-${item.viewKey}`" class="confidence-row">
          <view class="confidence-copy">
            <text>{{ item.name || item.code }}</text>
            <text>{{ item.confidenceView.reason }}</text>
          </view>
          <text :class="['confidence-score', confidenceToneClass(item.confidenceView.tone)]">
            {{ item.confidenceView.score }}
          </text>
        </view>
      </view>
    </view>

    <view class="top-grid">
      <view class="glass-card rank-card">
        <text class="section-title small-section-title">盈利 TOP</text>
        <view v-if="metrics.profitTop.length === 0" class="muted-empty">暂无盈利持仓</view>
        <view v-for="fund in metrics.profitTop" :key="`profit-${fund.viewKey}`" class="rank-row">
          <text>{{ fund.name || fund.code }}</text>
          <text class="profit-text finance-number">{{ displayMoney(fund.estimatedProfitValue, 1, true) }}</text>
        </view>
      </view>
      <view class="glass-card rank-card">
        <text class="section-title small-section-title">亏损 TOP</text>
        <view v-if="metrics.lossTop.length === 0" class="muted-empty">暂无亏损持仓</view>
        <view v-for="fund in metrics.lossTop" :key="`loss-${fund.viewKey}`" class="rank-row">
          <text>{{ fund.name || fund.code }}</text>
          <text class="loss-text finance-number">{{ displayMoney(fund.estimatedProfitValue, 1, true) }}</text>
        </view>
      </view>
    </view>

    <view class="list-head">
      <view>
        <text class="section-title">总持仓</text>
        <text class="list-subtitle">共 {{ funds.length }} 支，下拉刷新</text>
      </view>
    </view>

    <view v-if="funds.length === 0 && !loading" class="glass-card empty-card" @tap="sessionState.username && loadFunds(true)">
      <text>{{ sessionState.username ? '暂无持仓数据，可使用智能截图导入基金' : '暂无个人持仓数据。登录后可同步你的个人持仓记录。' }}</text>
    </view>

    <view v-for="(fund, fundIndex) in funds" :key="fund.viewKey" class="glass-card fund-card">
      <view class="fund-head">
        <view class="fund-title">
          <text class="fund-name">{{ fund.name || '未命名基金' }}</text>
          <text class="fund-code">{{ fund.code || '待更新' }}</text>
        </view>
        <view class="fund-status">
          <text :class="['status-pill', fund.isSettledValue ? 'settled' : fund.isHolidayValue ? 'holiday' : 'tracking']">
            {{ fund.statusLabel }}
          </text>
          <text :class="['rate', 'finance-number', profitClass(fund.currentRateValue)]">
            {{ displayPercent(fund.currentRateValue, 2) }}
          </text>
        </view>
      </view>

      <view v-if="fund.calibrationNote || fund.calibrationOffset !== undefined" class="calibration-strip">
        <text>估值修正 {{ signedPlainPercent(fund.calibrationOffset || 0) }}</text>
        <text>{{ fund.calibrationNote || '滚动误差校准' }}</text>
      </view>

      <view v-if="fund.pendingBuyValue || fund.pendingBuyAmountValue > 0" class="pending-trade-strip">
        <text>买入待确认</text>
        <text>
          {{ privacyMode === 0 && fund.pendingBuyAmountValue > 0 ? displayMoney(fund.pendingBuyAmountValue, 0, false) + ' · ' : '' }}不参与今日收益
        </text>
      </view>

      <view class="trend-panel">
        <view class="trend-head">
          <text>今日估值走势</text>
          <text>{{ trendPointCount(fund) }} 点</text>
        </view>
        <SparklineChart
          v-if="shouldRenderFundTrend(fund, fundIndex)"
          :canvas-id="fundTrendCanvasId(fund)"
          :points="todayTrendPoints(fund)"
          :tone="fund.currentRateValue >= 0 ? 'profit' : 'loss'"
          empty-text="暂无估值走势数据"
        />
        <button v-else class="expand-trend-button" @tap="expandFundTrend(fund)">
          展开走势
        </button>
      </view>

      <view class="fund-grid">
        <view class="fund-metric">
          <text class="metric-label">{{ fundNavLabel(fund) }}</text>
          <text class="metric-value nav-value finance-number">{{ fundNavText(fund) }}</text>
          <text v-if="fundDeviationText(fund) !== '--'" :class="['metric-hint', 'nav-deviation', profitClass(fundDeviationValue(fund))]">
            估值偏离 {{ fundDeviationText(fund) }}
          </text>
          <text v-else class="metric-hint">{{ fund.trendLabel }}</text>
        </view>
        <view class="fund-metric">
          <text class="metric-label">持仓本金</text>
          <text class="metric-value finance-number">{{ displayFundCost(fund) }}</text>
        </view>
        <view class="fund-metric accent-green">
          <text class="metric-label">今日市值</text>
          <text class="metric-value finance-number">{{ displayMoney(fund.todayAmountValue, 0, false) }}</text>
        </view>
        <view class="fund-metric accent-blue">
          <text class="metric-label">今日收益</text>
          <text :class="['metric-value', 'finance-number', profitClass(fund.todayProfitValue)]">
            {{ displayMoney(fund.todayProfitValue, 1, true) }}
          </text>
        </view>
        <view class="fund-metric">
          <text class="metric-label">今日收益率</text>
          <text :class="['metric-value', 'finance-number', profitClass(fund.currentRateValue)]">
            {{ displayPercent(fund.currentRateValue, 2) }}
          </text>
        </view>
        <view class="fund-metric">
          <text class="metric-label">累计盈亏</text>
          <text :class="['metric-value', 'finance-number', profitClass(fund.estimatedProfitValue)]">
            {{ displayMoney(fund.estimatedProfitValue, 1, true) }}
          </text>
        </view>
        <view class="fund-metric">
          <text class="metric-label">累计收益率</text>
          <text :class="['metric-value', 'finance-number', profitClass(fund.existingReturnRateValue)]">
            {{ displayPercent(fund.existingReturnRateValue, 2) }}
          </text>
        </view>
        <view class="fund-metric">
          <text class="metric-label">份额</text>
          <text class="metric-value finance-number">{{ numericOrDash(fund.shares, 2) }}</text>
        </view>
      </view>

      <view class="fund-foot">
        <view>
          <text class="muted-text">可信度</text>
          <text class="confidence-reason">{{ fund.confidenceView.reason }}</text>
        </view>
        <button class="history-button" @tap="openFundHistory(fund)">近1年</button>
        <text :class="['reliability', confidenceToneClass(fund.confidenceView.tone)]">
          {{ fund.confidenceView.level }} {{ fund.confidenceView.score }}
        </text>
      </view>
    </view>

    <view v-if="ocrPreviewVisible" class="modal-mask" @tap.self="closeOcrPreview">
      <view class="ocr-modal glass-card">
        <view class="modal-head">
          <view>
            <text class="section-title">OCR 识别预览</text>
            <text class="modal-subtitle">保存后写入基金持仓</text>
          </view>
          <button class="modal-close" @tap="closeOcrPreview">×</button>
        </view>

        <scroll-view scroll-y class="ocr-scroll">
          <view v-if="ocrItems.length === 0" class="empty-card inner-empty">
            <text>暂无识别结果</text>
          </view>
          <view v-if="ocrProfitUpdateState && ocrProfitUpdateState !== 'UNKNOWN'" class="ocr-update-state">
            <text :class="['update-badge', ocrProfitUpdateState === 'UPDATED' ? 'updated' : 'not-updated']">
              {{ ocrProfitUpdateState === 'UPDATED' ? '收益已更新' : '收益未更新' }}
            </text>
          </view>
          <view v-for="(item, index) in ocrItems" :key="ocrItemKey(item, index)" class="ocr-row">
            <view class="ocr-row-head">
              <text class="ocr-name">{{ ocrText(item, 'name', 'Name') || ocrText(item, 'ocrName', 'OcrName') || '待匹配基金' }}</text>
              <text class="chip">{{ ocrText(item, 'code', 'Code') || '待核对' }}</text>
            </view>
            <view class="ocr-grid">
              <view>
                <text>持有金额</text>
                <text>{{ ocrNumber(item, 'holdAmount', 'HoldAmount') }}</text>
              </view>
              <view>
                <text>确认持仓金额</text>
                <text>{{ ocrNumber(item, 'confirmedAmount', 'ConfirmedAmount') }}</text>
              </view>
              <view>
                <text>买入待确认</text>
                <text>{{ ocrNumber(item, 'pendingBuyAmount', 'PendingBuyAmount') }}</text>
              </view>
              <view>
                <text>参与今日收益</text>
                <text>{{ ocrParticipatesTodayText(item) }}</text>
              </view>
              <view>
                <text>持仓本金</text>
                <text>{{ ocrNumber(item, 'costAmount', 'CostAmount') }}</text>
              </view>
              <view>
                <text>累计收益</text>
                <text>{{ ocrNumber(item, 'holdingIncome', 'HoldingIncome') }}</text>
              </view>
              <view>
                <text>今日收益</text>
                <text>{{ ocrNumber(item, 'yesterdayIncome', 'YesterdayIncome') }}</text>
              </view>
              <view>
                <text>持有收益率</text>
                <text>{{ percentDash(ocrPick(item, 'holdingRate', 'HoldingRate')) }}</text>
              </view>
              <view>
                <text>匹配分</text>
                <text>{{ numericOrDash(ocrPick(item, 'matchScore', 'MatchScore'), 0) }}</text>
              </view>
            </view>
            <text v-if="ocrText(item, 'warning', 'Warning')" class="ocr-warning">{{ ocrText(item, 'warning', 'Warning') }}</text>
            <text v-if="ocrText(item, 'pendingReason', 'PendingReason')" class="ocr-reason">
              来源：{{ ocrText(item, 'pendingSource', 'PendingSource') || 'none' }} · {{ ocrText(item, 'pendingReason', 'PendingReason') }}
              <text v-if="ocrText(item, 'pendingEvidence', 'PendingEvidence')" style="color:#93c5fd;"> [{{ ocrText(item, 'pendingEvidence', 'PendingEvidence') }}]</text>
            </text>
            <text v-if="ocrText(item, 'profitUpdateState', 'ProfitUpdateState') && ocrText(item, 'profitUpdateState', 'ProfitUpdateState') !== 'UNKNOWN'" class="ocr-reason" style="font-size:20rpx;">
              收益状态：{{ ocrText(item, 'profitUpdateState', 'ProfitUpdateState') === 'UPDATED' ? '已更新' : '未更新' }}
            </text>
          </view>
          <view v-if="ocrDiagnostics.length" class="diagnostics">
            <text v-for="(line, index) in ocrDiagnostics" :key="`diag-${index}`">{{ line }}</text>
          </view>
        </scroll-view>

        <view class="modal-actions">
          <button class="secondary-action" @tap="closeOcrPreview">取消</button>
          <button class="primary-gradient-button confirm-button" :disabled="ocrConfirming || ocrItems.length === 0" @tap="confirmOcrImport">
            {{ ocrConfirming ? '导入中...' : '保存导入' }}
          </button>
        </view>
      </view>
    </view>

    <view v-if="historyModal.show" class="modal-mask" @tap.self="closeFundHistory">
      <view class="history-modal glass-card">
        <view class="modal-head">
          <view>
            <text class="section-title">{{ historyModal.name }}</text>
            <text class="modal-subtitle">近 1 年收益档案 · 官方净值历史待后端代理核实</text>
          </view>
          <button class="modal-close" @tap="closeFundHistory">×</button>
        </view>
        <view v-if="historyModal.loading" class="history-loading">正在读取历史档案...</view>
        <view v-else>
          <view class="history-chart-block">
            <view class="trend-head">
              <text>收益走势</text>
              <text>{{ historyRows.length }} 条</text>
            </view>
            <SparklineChart
              canvas-id="fund-history-profit"
              :points="historyProfitPoints"
              :tone="historyProfitTone"
            />
          </view>
          <view class="history-chart-block">
            <view class="trend-head">
              <text>收益率走势</text>
              <text>{{ historyRows.length }} 条</text>
            </view>
            <SparklineChart
              canvas-id="fund-history-rate"
              :points="historyRatePoints"
              tone="neutral"
            />
          </view>
          <scroll-view scroll-y class="history-list">
            <view v-if="historyRows.length === 0" class="empty-card inner-empty">
              <text>暂无历史档案</text>
            </view>
            <view v-for="(row, index) in historyRows.slice(0, 80)" :key="historyRowKey(row, index)" class="history-row">
              <view>
                <text class="history-date">{{ formatDate(row.recordDate) }}</text>
                <text class="history-sub">市值 {{ archiveMoney(row.assets) }} · 本金 {{ archiveMoney(row.cost) }}</text>
              </view>
              <view class="history-values">
                <text :class="['finance-number', profitClass(row.dailyProfit)]">{{ signedMoney(row.dailyProfit || 0) }}</text>
                <text :class="['small-rate', profitClass(row.dailyRate)]">{{ signedPercent(row.dailyRate || 0) }}</text>
              </view>
            </view>
          </scroll-view>
        </view>
      </view>
    </view>
    </view>

    <view v-else class="stock-mode mode-pane">
      <view class="glass-card stock-search-card">
        <view class="stock-section-head">
          <view>
            <text class="section-title">股票查询</text>
            <text class="list-subtitle">输入代码或名称，查询后可加入自选或持仓</text>
          </view>
          <text class="chip">{{ stockUpdatedAt || '待刷新' }}</text>
        </view>
        <view class="stock-search-row">
          <input
            v-model="stockSearchKeyword"
            class="stock-search-input"
            placeholder="输入股票代码或名称，如 000001 / 平安银行"
            placeholder-class="input-placeholder"
            confirm-type="search"
            @confirm="handleStockSearch"
          />
          <button class="stock-primary-button" :disabled="stockSearchLoading" @tap="handleStockSearch">
            {{ stockSearchLoading ? '查询中' : '查询' }}
          </button>
        </view>

        <view v-if="stockSearchResults.length" class="stock-result-list">
          <view v-for="item in stockSearchResults" :key="stockItemKey(item, 'search')" class="stock-result-row">
            <view class="stock-main">
              <text class="stock-name">{{ stockName(item) }}</text>
              <text class="stock-code">{{ stockCode(item) }} · {{ stockMarket(item) }}</text>
            </view>
            <view class="stock-market-data">
              <text class="finance-number">{{ stockPriceText(item) }}</text>
              <text :class="['stock-rate', profitClass(stockRate(item))]">{{ stockRateText(item) }}</text>
            </view>
            <view class="stock-inline-actions">
              <button @tap="openStockTrend(item)">走势</button>
              <button @tap="addWatchFromStock(item)">加自选</button>
              <button @tap="openHoldingEditor(item)">加持仓</button>
            </view>
          </view>
        </view>
      </view>

      <view class="glass-card stock-chart-card" v-if="selectedStock.code">
        <view class="stock-section-head">
          <view>
            <text class="section-title">{{ selectedStock.name || selectedStock.code }}</text>
            <text class="list-subtitle">{{ selectedStock.code }} · {{ selectedStock.market || '--' }}</text>
          </view>
          <view class="stock-market-data right">
            <text class="finance-number">{{ stockPriceText(selectedStock) }}</text>
            <text :class="['stock-rate', profitClass(stockRate(selectedStock))]">{{ stockRateText(selectedStock) }}</text>
          </view>
        </view>
        <view class="kline-tabs">
          <button
            v-for="period in stockKlinePeriods"
            :key="period.value"
            :class="['kline-tab', stockKlinePeriod === period.value ? 'active' : '']"
            @tap="switchStockKline(period.value)"
          >
            {{ period.label }}
          </button>
        </view>
        <view class="trend-panel stock-trend-panel">
          <view class="trend-head">
            <text>当前周期走势</text>
            <text>{{ stockKlineRows.length }} 点</text>
          </view>
          <SparklineChart
            :canvas-id="`stock-${selectedStock.code}-${stockKlinePeriod}`"
            :points="stockKlinePoints"
            :tone="stockRate(selectedStock) >= 0 ? 'profit' : 'loss'"
            empty-text="暂无走势数据"
          />
        </view>
      </view>

      <view class="glass-card stock-list-card">
        <view class="stock-section-head">
          <view>
            <text class="section-title">持有股票</text>
            <text class="list-subtitle">共 {{ stockHoldings.length }} 只，下拉刷新</text>
          </view>
        </view>
        <view v-if="stockHoldings.length === 0" class="empty-card inner-empty">
          <text>暂无持有股票</text>
        </view>
        <view v-for="item in stockHoldings" :key="stockItemKey(item, 'holding')" class="stock-card">
          <view class="stock-card-head">
            <view class="stock-main">
              <text class="stock-name">{{ stockName(item) }}</text>
              <text class="stock-code">{{ stockCode(item) }} · {{ stockMarket(item) }}</text>
            </view>
            <view class="stock-market-data">
              <text class="finance-number">{{ stockPriceText(item) }}</text>
              <text :class="['stock-rate', profitClass(stockRate(item))]">{{ stockRateText(item) }}</text>
            </view>
          </view>
          <view class="stock-metric-grid">
            <view>
              <text>持股数量</text>
              <text>{{ stockSharesText(item) }}</text>
            </view>
            <view>
              <text>市值</text>
              <text>{{ stockMoneyText(stockMarketValue(item)) }}</text>
            </view>
            <view>
              <text>持有收益</text>
              <text :class="profitClass(stockProfit(item))">{{ stockSignedMoneyText(stockProfit(item)) }}</text>
            </view>
            <view>
              <text>收益率</text>
              <text :class="profitClass(stockPickNumber(item, 'totalProfitRate'))">{{ stockPercentText(stockPickNumber(item, 'totalProfitRate')) }}</text>
            </view>
          </view>
          <view class="stock-actions">
            <button @tap="openStockTrend(item)">走势</button>
            <button @tap="addWatchFromStock(item)">加自选</button>
            <button class="danger" @tap="removeHolding(item)">删除</button>
          </view>
        </view>
      </view>

      <view class="glass-card stock-list-card">
        <view class="stock-section-head">
          <view>
            <text class="section-title">自选股票</text>
            <text class="list-subtitle">共 {{ stockWatchList.length }} 只</text>
          </view>
        </view>
        <view v-if="stockWatchList.length === 0" class="empty-card inner-empty">
          <text>暂无自选股票</text>
        </view>
        <view v-for="item in stockWatchList" :key="stockItemKey(item, 'watch')" class="stock-card compact-stock-card">
          <view class="stock-card-head">
            <view class="stock-main">
              <text class="stock-name">{{ stockName(item) }}</text>
              <text class="stock-code">{{ stockCode(item) }} · {{ stockMarket(item) }}</text>
            </view>
            <view class="stock-market-data">
              <text class="finance-number">{{ stockPriceText(item) }}</text>
              <text :class="['stock-rate', profitClass(stockRate(item))]">{{ stockRateText(item) }}</text>
            </view>
          </view>
          <view class="stock-actions">
            <button @tap="openStockTrend(item)">走势</button>
            <button @tap="openHoldingEditor(item)">转持有</button>
            <button class="danger" @tap="removeWatch(item)">移除</button>
          </view>
        </view>
      </view>

      <view v-if="stockOcrPreviewVisible" class="modal-mask" @tap.self="closeStockOcrPreview">
        <view class="ocr-modal glass-card">
          <view class="modal-head">
            <view>
              <text class="section-title">股票 OCR 识别预览</text>
            <text class="modal-subtitle">保存后按持仓/自选写入</text>
            </view>
            <button class="modal-close" @tap="closeStockOcrPreview">×</button>
          </view>
          <scroll-view scroll-y class="ocr-scroll">
            <view v-if="stockOcrItems.length === 0" class="empty-card inner-empty">
              <text>暂无识别结果</text>
            </view>
            <view v-for="(item, index) in stockOcrItems" :key="stockOcrItemKey(item, index)" class="stock-ocr-row">
              <view class="ocr-row-head">
                <text class="ocr-name">{{ item.stockName || item.recognizedName || '待匹配股票' }}</text>
                <text class="chip">{{ item.stockCode || '待核对' }}</text>
              </view>
              <view class="stock-ocr-grid">
                <view>
                  <text>写入类型</text>
                  <picker :range="stockOcrActionLabels" :value="stockOcrActionIndex(item)" @change="updateStockOcrAction(index, $event)">
                    <text>{{ item.action === 'watch' ? '自选' : '持仓' }}</text>
                  </picker>
                </view>
                <view>
                  <text>持股数量</text>
                  <input :value="stockOcrInputText(item.shares)" type="digit" @input="updateStockOcrNumber(index, 'shares', $event)" />
                </view>
                <view>
                  <text>成本价</text>
                  <input :value="stockOcrInputText(item.costPrice)" type="digit" @input="updateStockOcrNumber(index, 'costPrice', $event)" />
                </view>
                <view>
                  <text>成本金额</text>
                  <input :value="stockOcrInputText(item.costAmount)" type="digit" @input="updateStockOcrNumber(index, 'costAmount', $event)" />
                </view>
                <view>
                  <text>市值</text>
                  <text>{{ stockMoneyText(item.marketValue) }}</text>
                </view>
                <view>
                  <text>浮动盈亏</text>
                  <text :class="profitClass(item.floatingProfit)">{{ stockSignedMoneyText(item.floatingProfit) }}</text>
                </view>
              </view>
              <text v-if="item.note" class="ocr-warning">{{ item.note }}</text>
            </view>
            <view v-if="stockOcrDiagnostics.length" class="diagnostics">
              <text v-for="(line, index) in stockOcrDiagnostics" :key="`stock-diag-${index}`">{{ line }}</text>
            </view>
          </scroll-view>
          <view class="modal-actions">
            <button class="secondary-action" @tap="closeStockOcrPreview">取消</button>
            <button class="primary-gradient-button confirm-button" :disabled="stockOcrConfirming || stockOcrItems.length === 0" @tap="confirmStockOcrImport">
              {{ stockOcrConfirming ? '导入中...' : '保存导入' }}
            </button>
          </view>
        </view>
      </view>

      <view v-if="holdingEditor.show" class="modal-mask" @tap.self="closeHoldingEditor">
        <view class="holding-editor glass-card">
          <view class="modal-head">
            <view>
              <text class="section-title">加入持仓</text>
              <text class="modal-subtitle">{{ holdingEditor.name }} · {{ holdingEditor.code }}</text>
            </view>
            <button class="modal-close" @tap="closeHoldingEditor">×</button>
          </view>
          <view class="holding-form">
            <view>
              <text>持股数量</text>
              <input v-model="holdingEditor.shares" type="digit" placeholder="例如 100" placeholder-class="input-placeholder" />
            </view>
            <view>
              <text>成本价</text>
              <input v-model="holdingEditor.costPrice" type="digit" placeholder="例如 12.35" placeholder-class="input-placeholder" />
            </view>
          </view>
          <view class="modal-actions">
            <button class="secondary-action" @tap="closeHoldingEditor">取消</button>
            <button class="primary-gradient-button confirm-button" @tap="submitHoldingEditor">保存</button>
          </view>
        </view>
      </view>
    </view>

    <view class="safe-tabbar-space" />
    <AppTabBar active="home" />
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onPullDownRefresh, onShow } from '@dcloudio/uni-app';
import AppTabBar from '../../components/AppTabBar.vue';
import SparklineChart from '../../components/SparklineChart.vue';
import { getProfile, isGeneratedWechatUsername, pickAvatar, pickDisplayName, pickUsername } from '../../services/api/auth';
import {
  confirmFundOcr,
  getFundArchives,
  getTodayFunds,
  previewFundOcr,
  type FundArchiveRow,
  type FundTodayItem,
  type OcrImportPreviewItem
} from '../../services/api/fund';
import {
  confirmStockOcr,
  deleteStockHolding,
  deleteStockWatch,
  getStockDashboard,
  getStockKlines,
  previewStockOcr,
  saveStockHolding,
  saveStockWatch,
  searchStocks,
  type StockBaseItem,
  type StockHoldingItem,
  type StockKlineItem,
  type StockKlinePeriod,
  type StockOcrPreviewItem,
  type StockSearchItem,
  type StockWatchItem
} from '../../services/api/stock';
import { loadSession, saveSession, sessionState } from '../../stores/session';
import { loadTheme, themeClass } from '../../stores/theme';
import { avatarInitial, formatMoney, profitClass, signedMoney, signedPercent } from '../../utils/format';
import { getStorage, setStorage } from '../../utils/storage';
import { getLocalStorageCache, setLocalStorageCache } from '../../services/request';
import {
  buildPortfolioMetrics,
  maskByPrivacy,
  moneyDash,
  percentDash,
  type FundView,
  type PrivacyMode
} from '../../utils/fundMetrics';

const PRIVACY_KEY = 'privacy_mode';
const DEBUG_FIELD_AUDIT =
  (import.meta as ImportMeta & { env?: { VITE_DEBUG_FIELD_AUDIT?: string } }).env?.VITE_DEBUG_FIELD_AUDIT === 'true';
const LOGIN_REQUIRED_TIP = '登录后可使用该功能';
const FUND_PAGE_CACHE_TTL = 30000;
const STOCK_PAGE_CACHE_TTL = 30000;
const PROFILE_PAGE_CACHE_TTL = 60000;
const DEFAULT_RENDERED_TREND_COUNT = 3;

const rawFunds = ref<FundTodayItem[]>([]);
const assetMode = ref<'fund' | 'stock'>('fund');
const loading = ref(false);
const profileLoading = ref(false);
const ocrBusy = ref(false);
const ocrConfirming = ref(false);
const ocrPreviewVisible = ref(false);
const ocrItems = ref<OcrImportPreviewItem[]>([]);
const ocrProfitUpdateState = ref('');
const ocrDiagnostics = ref<string[]>([]);
const privacyMode = ref<PrivacyMode>(normalizePrivacyMode(getStorage(PRIVACY_KEY, 2)));
const stockLoading = ref(false);
const stockSearchLoading = ref(false);
const stockSearchKeyword = ref('');
const stockSearchResults = ref<StockSearchItem[]>([]);
const stockHoldings = ref<StockHoldingItem[]>([]);
const stockWatchList = ref<StockWatchItem[]>([]);
const stockUpdatedAt = ref('');
const stockKlinePeriod = ref<StockKlinePeriod>('minute');
const stockKlineRows = ref<StockKlineItem[]>([]);
const selectedStock = ref<StockBaseItem>({});
const stockOcrPreviewVisible = ref(false);
const stockOcrConfirming = ref(false);
const stockOcrBatchId = ref<number | null>(null);
const stockOcrItems = ref<StockOcrPreviewItem[]>([]);
const stockOcrDiagnostics = ref<string[]>([]);
const holdingEditor = ref({
  show: false,
  code: '',
  market: '',
  name: '',
  shares: '',
  costPrice: ''
});
const historyModal = ref({
  show: false,
  loading: false,
  code: '',
  name: '',
  rows: [] as FundArchiveRow[]
});
const fundLoadedAt = ref(0);
const stockLoadedAt = ref(0);
const profileLoadedAt = ref(0);
const dashboardStale = ref(false);
const dashboardUpdatedAt = ref('');
const expandedTrendKeys = ref<Record<string, boolean>>({});

const metrics = computed(() => buildPortfolioMetrics(rawFunds.value));
const funds = computed(() => metrics.value.funds);
const stockKlinePeriods: Array<{ label: string; value: StockKlinePeriod }> = [
  { label: '分K', value: 'minute' },
  { label: '时K', value: 'hour' },
  { label: '日K', value: 'day' },
  { label: '月K', value: 'month' },
  { label: '年K', value: 'year' }
];
const stockOcrActionLabels = ['持仓', '自选'];
const confidenceRows = computed(() =>
  [...funds.value].sort((a, b) => a.confidenceView.score - b.confidenceView.score).slice(0, 4)
);
const headerCountText = computed(() =>
  assetMode.value === 'stock'
    ? `${stockHoldings.value.length} 持有 · ${stockWatchList.value.length} 自选`
    : `${funds.value.length} 只基金`
);
const ocrButtonText = computed(() => {
  if (ocrBusy.value) return assetMode.value === 'stock' ? '股票解析中...' : '基金解析中...';
  return assetMode.value === 'stock' ? '智能截图导入股票' : '智能截图导入基金';
});
const avatarUrl = computed(() => sessionState.avatarDataUrl || sessionState.avatarUrl || '');
const avatarText = computed(() => avatarInitial(sessionState.username));
const accountEntryTitle = computed(() => (sessionState.username ? '个人中心' : '登录 / 同步持仓'));
const accountEntrySubtitle = computed(() => {
  if (!sessionState.username) return '同步个人记录';
  if (sessionState.displayName) return sessionState.displayName;
  return isGeneratedWechatUsername(sessionState.username) ? '填写微信昵称' : sessionState.username;
});
const stockKlinePoints = computed(() =>
  normalizeStockKlines(stockKlineRows.value).map((row) => row.close)
);
const historyRows = computed(() =>
  [...historyModal.value.rows].sort((a, b) => String(b.recordDate || '').localeCompare(String(a.recordDate || '')))
);
const historyProfitPoints = computed(() => [...historyRows.value].reverse().map((row) => Number(row.dailyProfit || 0)));
const historyRatePoints = computed(() => [...historyRows.value].reverse().map((row) => Number(row.totalRate ?? row.dailyRate ?? 0)));
const historyProfitTone = computed(() => {
  const last = historyProfitPoints.value[historyProfitPoints.value.length - 1] || 0;
  return last >= 0 ? 'profit' : 'loss';
});
const privacyLabel = computed(() => {
  const labels: Record<PrivacyMode, string> = {
    0: '睁眼模式',
    1: '半遮蔽',
    2: '全遮蔽',
    3: '极致隐匿'
  };
  return labels[privacyMode.value];
});

onShow(() => {
  loadTheme();
  loadSession();
  privacyMode.value = normalizePrivacyMode(getStorage(PRIVACY_KEY, privacyMode.value));
  if (!sessionState.username) {
    rawFunds.value = [];
    stockHoldings.value = [];
    stockWatchList.value = [];
    stockUpdatedAt.value = '';
    fundLoadedAt.value = 0;
    stockLoadedAt.value = 0;
    profileLoadedAt.value = 0;
    return;
  }

  loadProfile().catch((error) => console.warn('[home:profile]', error));
  if (assetMode.value === 'stock') {
    loadStocks(false).catch((error) => console.warn('[stock:load]', error));
  } else {
    loadFunds(false).catch((error) => console.warn('[home:load]', error));
  }
});

onPullDownRefresh(async () => {
  try {
    if (assetMode.value === 'stock') {
      await loadStocks(true);
      if (selectedStock.value.code || selectedStock.value.stockCode || selectedStock.value.symbol) {
        await loadStockKlines(false);
      }
    } else {
      await loadFunds(true);
    }
  } catch (error) {
    console.warn('[home:pull-down-refresh]', error);
    uni.showToast({ title: '刷新失败，请稍后重试', icon: 'none' });
  } finally {
    uni.stopPullDownRefresh();
  }
});

async function loadFunds(force: boolean) {
  if (loading.value) return;
  if (!sessionState.username) {
    rawFunds.value = [];
    fundLoadedAt.value = 0;
    dashboardStale.value = false;
    dashboardUpdatedAt.value = '';
    return;
  }
  if (!force && rawFunds.value.length > 0 && Date.now() - fundLoadedAt.value < FUND_PAGE_CACHE_TTL) return;

  const cacheKey = `dashboard_cache_${sessionState.username}_v1`;
  if (!force && rawFunds.value.length === 0) {
    const cached = getLocalStorageCache<FundTodayItem[]>(cacheKey);
    if (cached && Array.isArray(cached) && cached.length > 0) {
      rawFunds.value = cached;
      dashboardStale.value = true;
      dashboardUpdatedAt.value = '使用缓存';
    }
  }

  loading.value = true;
  try {
    const hasCachedData = rawFunds.value.length > 0;
    const data = await getTodayFunds(sessionState.username, force, hasCachedData);
    const items = Array.isArray(data) ? data : [];
    logFundTodayAudit(items);
    rawFunds.value = items;
    fundLoadedAt.value = Date.now();
    dashboardStale.value = false;
    dashboardUpdatedAt.value = '';
    setLocalStorageCache(cacheKey, items, 3600000);
  } catch (error) {
    console.warn('[dashboard] load failed, keeping cached data:', error);
    if (rawFunds.value.length > 0) {
      dashboardStale.value = true;
      dashboardUpdatedAt.value = '使用上次数据';
    }
  } finally {
    loading.value = false;
  }
}

function logFundTodayAudit(items: FundTodayItem[], phase = 'today') {
  if (!DEBUG_FIELD_AUDIT) return;

  items.slice(0, 8).forEach((fund) => {
    const fields = getFundNavFields(fund as FundView);
    console.warn('[fund.today nav fields]', {
      phase,
      name: fund.name,
      code: fund.code,
      nav: fields.nav,
      estimate: fields.estimate,
      estimateRate: fields.estimateRate,
      deviation: fields.deviation
    });
  });
}

function setAssetMode(mode: 'fund' | 'stock') {
  if (assetMode.value === mode) return;
  assetMode.value = mode;
  if (mode === 'stock' && sessionState.username) {
    loadStocks(false).catch((error) => console.warn('[stock:load]', error));
  }
}

function handleSmartOcr() {
  if (assetMode.value === 'stock') {
    startStockOcr();
    return;
  }

  startFundOcr();
}

async function loadStocks(force: boolean) {
  if (stockLoading.value) return;
  if (!sessionState.username) {
    stockHoldings.value = [];
    stockWatchList.value = [];
    stockUpdatedAt.value = '';
    stockLoadedAt.value = 0;
    return;
  }
  if (
    !force &&
    (stockHoldings.value.length > 0 || stockWatchList.value.length > 0) &&
    Date.now() - stockLoadedAt.value < STOCK_PAGE_CACHE_TTL
  ) {
    return;
  }

  stockLoading.value = true;
  try {
    const data = await getStockDashboard(sessionState.username);
    if (data.success === false) throw new Error(String(data.message || '股票数据读取失败'));

    stockHoldings.value = Array.isArray(data.holdings) ? data.holdings : [];
    stockWatchList.value = Array.isArray(data.watchList) ? data.watchList : [];
    stockUpdatedAt.value = String(data.updatedAt || '');
    stockLoadedAt.value = Date.now();

    if (!selectedStock.value.code && !selectedStock.value.stockCode && !selectedStock.value.symbol) {
      const first = stockHoldings.value[0] || stockWatchList.value[0];
      if (first) await openStockTrend(first, false);
    }

    if (force) uni.showToast({ title: '股票数据已刷新', icon: 'none' });
  } finally {
    stockLoading.value = false;
  }
}

async function handleStockSearch() {
  const keyword = stockSearchKeyword.value.trim();
  if (!keyword) {
    uni.showToast({ title: '请输入股票代码或名称', icon: 'none' });
    return;
  }

  stockSearchLoading.value = true;
  try {
    const data = await searchStocks(keyword);
    if (data.success === false) throw new Error(String(data.message || '查询失败'));

    stockSearchResults.value = Array.isArray(data.items) ? data.items : [];
    if (!stockSearchResults.value.length) {
      uni.showToast({ title: '没有匹配股票', icon: 'none' });
    }
  } catch (error) {
    console.warn('[stock:search]', error);
    uni.showToast({ title: getErrorMessage(error, '查询失败，请稍后重试'), icon: 'none' });
  } finally {
    stockSearchLoading.value = false;
  }
}

async function addWatchFromStock(item: StockBaseItem) {
  if (!requireLogin()) return;

  const payload = buildStockWatchPayload(item);
  if (!payload) return;

  try {
    const result = await saveStockWatch(payload);
    if (result.success === false) throw new Error(String(result.message || '加入自选失败'));

    uni.showToast({ title: '已加入自选', icon: 'none' });
    await loadStocks(false);
  } catch (error) {
    console.warn('[stock:add-watch]', error);
    uni.showToast({ title: getErrorMessage(error, '加入自选失败'), icon: 'none' });
  }
}

function openHoldingEditor(item: StockBaseItem) {
  if (!requireLogin()) return;

  const code = stockCode(item);
  if (!code) {
    uni.showToast({ title: '股票代码缺失', icon: 'none' });
    return;
  }

  const shares = stockShares(item);
  const price = firstKnown(stockPickNumber(item, 'costPrice'), stockPrice(item));
  holdingEditor.value = {
    show: true,
    code,
    market: stockMarket(item),
    name: stockName(item),
    shares: shares === null ? '' : String(shares),
    costPrice: price === null ? '' : String(price)
  };
}

function closeHoldingEditor() {
  holdingEditor.value.show = false;
}

async function submitHoldingEditor() {
  if (!requireLogin()) return;

  const shares = toFiniteNumber(holdingEditor.value.shares);
  const costPrice = toFiniteNumber(holdingEditor.value.costPrice);
  if (!holdingEditor.value.code || shares === null || shares <= 0 || costPrice === null || costPrice <= 0) {
    uni.showToast({ title: '请填写有效持股数量和成本价', icon: 'none' });
    return;
  }

  try {
    const result = await saveStockHolding({
      username: sessionState.username,
      stockCode: holdingEditor.value.code,
      stockName: holdingEditor.value.name,
      market: holdingEditor.value.market,
      shares,
      costPrice,
      costAmount: Number((shares * costPrice).toFixed(2))
    });
    if (result.success === false) throw new Error(String(result.message || '保存持仓失败'));

    closeHoldingEditor();
    uni.showToast({ title: '股票持仓已保存', icon: 'none' });
    await loadStocks(false);
  } catch (error) {
    console.warn('[stock:save-holding]', error);
    uni.showToast({ title: getErrorMessage(error, '保存持仓失败'), icon: 'none' });
  }
}

async function removeHolding(item: StockBaseItem) {
  if (!requireLogin()) return;
  const code = stockCode(item);
  if (!code) return;

  const confirmed = await confirmModal(`删除股票持仓：${stockName(item)}？`);
  if (!confirmed) return;

  try {
    const result = await deleteStockHolding(sessionState.username, code, stockMarket(item));
    if (result.success === false) throw new Error(String(result.message || '删除持仓失败'));

    uni.showToast({ title: '已删除股票持仓', icon: 'none' });
    await loadStocks(false);
  } catch (error) {
    console.warn('[stock:delete-holding]', error);
    uni.showToast({ title: getErrorMessage(error, '删除持仓失败'), icon: 'none' });
  }
}

async function removeWatch(item: StockBaseItem) {
  if (!requireLogin()) return;
  const code = stockCode(item);
  if (!code) return;

  try {
    const result = await deleteStockWatch(sessionState.username, code, stockMarket(item));
    if (result.success === false) throw new Error(String(result.message || '移除自选失败'));

    uni.showToast({ title: '已移除自选', icon: 'none' });
    await loadStocks(false);
  } catch (error) {
    console.warn('[stock:delete-watch]', error);
    uni.showToast({ title: getErrorMessage(error, '移除自选失败'), icon: 'none' });
  }
}

async function openStockTrend(item: StockBaseItem, showLoading = true) {
  const code = stockCode(item);
  if (!code) {
    uni.showToast({ title: '股票代码缺失', icon: 'none' });
    return;
  }

  selectedStock.value = {
    ...item,
    code,
    market: stockMarket(item),
    name: stockName(item)
  };
  await loadStockKlines(showLoading);
}

async function switchStockKline(period: StockKlinePeriod) {
  if (stockKlinePeriod.value === period) return;
  stockKlinePeriod.value = period;
  await loadStockKlines(true);
}

async function loadStockKlines(showError = true) {
  const code = stockCode(selectedStock.value);
  if (!code) {
    stockKlineRows.value = [];
    return;
  }

  try {
    const data = await getStockKlines(code, stockKlinePeriod.value);
    if (data.success === false) throw new Error(String(data.message || '走势读取失败'));
    stockKlineRows.value = Array.isArray(data.items) ? normalizeStockKlines(data.items) : [];
  } catch (error) {
    console.warn('[stock:klines]', error);
    stockKlineRows.value = [];
    if (showError) uni.showToast({ title: getErrorMessage(error, '走势读取失败'), icon: 'none' });
  }
}

async function startStockOcr() {
  if (!requireLogin() || ocrBusy.value) return;

  try {
    const filePath = await chooseImage();
    if (!filePath) return;

    ocrBusy.value = true;
    uni.showLoading({ title: '股票解析中', mask: true });
    const result = await previewStockOcr(sessionState.username, filePath);
    if (result.success === false) throw new Error(String(result.message || '股票 OCR 失败'));

    stockOcrBatchId.value = Number(result.batchId || 0) || null;
    stockOcrItems.value = (Array.isArray(result.items) ? result.items : []).map(normalizeStockOcrPreviewItem);
    stockOcrDiagnostics.value = Array.isArray(result.diagnostics) ? result.diagnostics : [];
    stockOcrPreviewVisible.value = true;
    uni.showToast({ title: `识别到 ${stockOcrItems.value.length} 条股票`, icon: 'none' });
  } catch (error) {
    console.warn('[stock:ocr-preview]', error);
    uni.showToast({ title: getErrorMessage(error, '股票 OCR 失败'), icon: 'none' });
  } finally {
    ocrBusy.value = false;
    uni.hideLoading();
  }
}

async function confirmStockOcrImport() {
  if (!requireLogin() || stockOcrConfirming.value) return;
  if (!stockOcrBatchId.value || stockOcrItems.value.length === 0) return;

  stockOcrConfirming.value = true;
  try {
    const result = await confirmStockOcr({
      username: sessionState.username,
      batchId: stockOcrBatchId.value,
      items: stockOcrItems.value
    });
    if (result.success === false) throw new Error(String(result.message || '股票 OCR 确认失败'));

    closeStockOcrPreview();
    uni.showToast({ title: `已写入 ${result.saved ?? 0} 条股票数据`, icon: 'none' });
    await loadStocks(false);
  } catch (error) {
    console.warn('[stock:ocr-confirm]', error);
    uni.showToast({ title: getErrorMessage(error, '股票 OCR 确认失败'), icon: 'none' });
  } finally {
    stockOcrConfirming.value = false;
  }
}

function closeStockOcrPreview() {
  stockOcrPreviewVisible.value = false;
}

function normalizeStockOcrPreviewItem(item: StockOcrPreviewItem): StockOcrPreviewItem {
  const shares = toFiniteNumber(item.shares);
  const costPrice = toFiniteNumber(item.costPrice);
  const costAmount = toFiniteNumber(item.costAmount);
  const marketValue = toFiniteNumber(item.marketValue);
  const normalized = {
    ...item,
    action: item.action || 'holding',
    shares,
    costPrice,
    costAmount,
    marketValue,
    floatingProfit: toFiniteNumber(item.floatingProfit),
    floatingProfitRate: toFiniteNumber(item.floatingProfitRate)
  };

  return recalculateStockOcrItem(normalized);
}

function recalculateStockOcrItem(item: StockOcrPreviewItem): StockOcrPreviewItem {
  const shares = toFiniteNumber(item.shares);
  const costPrice = toFiniteNumber(item.costPrice);
  if (shares !== null && costPrice !== null) {
    item.costAmount = Number((shares * costPrice).toFixed(2));
  }

  const costAmount = toFiniteNumber(item.costAmount);
  const marketValue = toFiniteNumber(item.marketValue);
  if (marketValue !== null && costAmount !== null) {
    item.floatingProfit = Number((marketValue - costAmount).toFixed(2));
    item.floatingProfitRate = costAmount > 0
      ? Number(((Number(item.floatingProfit) / costAmount) * 100).toFixed(4))
      : item.floatingProfitRate;
  }

  return item;
}

function stockOcrActionIndex(item: StockOcrPreviewItem) {
  return item.action === 'watch' ? 1 : 0;
}

function updateStockOcrAction(index: number, event: unknown) {
  const next = [...stockOcrItems.value];
  const item = next[index];
  if (!item) return;
  item.action = Number(pickEventValue(event)) === 1 ? 'watch' : 'holding';
  stockOcrItems.value = next;
}

function updateStockOcrNumber(index: number, key: 'shares' | 'costPrice' | 'costAmount', event: unknown) {
  const next = [...stockOcrItems.value];
  const item = next[index];
  if (!item) return;
  item[key] = toFiniteNumber(pickEventValue(event));
  next[index] = recalculateStockOcrItem(item);
  stockOcrItems.value = next;
}

function pickEventValue(event: unknown) {
  if (event && typeof event === 'object' && 'detail' in event) {
    return (event as { detail?: { value?: unknown } }).detail?.value;
  }

  return undefined;
}

function stockOcrInputText(value: unknown) {
  const n = toFiniteNumber(value);
  return n === null ? '' : String(n);
}

function stockOcrItemKey(item: StockOcrPreviewItem, index: number) {
  return `${item.id || item.stockCode || item.stockName || item.recognizedName || 'stock-ocr'}-${index}`;
}

function buildStockWatchPayload(item: StockBaseItem) {
  if (!sessionState.username) return null;
  const code = stockCode(item);
  if (!code) {
    uni.showToast({ title: '股票代码缺失', icon: 'none' });
    return null;
  }

  return {
    username: sessionState.username,
    stockCode: code,
    stockName: stockName(item),
    market: stockMarket(item)
  };
}

function normalizeStockKlines(rows: StockKlineItem[]) {
  return rows
    .map((row, index) => ({
      time: String(firstKnown(row.time, row.date, row.datetime, row.tradeTime, index) || ''),
      timeOrder: parsePointTime(firstKnown(row.time, row.date, row.datetime, row.tradeTime), index),
      open: stockPickNumber(row, 'open') ?? stockPickNumber(row, 'close') ?? 0,
      close: stockPickNumber(row, 'close') ?? stockPickNumber(row, 'price') ?? stockPickNumber(row, 'current') ?? 0,
      high: stockPickNumber(row, 'high') ?? stockPickNumber(row, 'highest') ?? stockPickNumber(row, 'close') ?? 0,
      low: stockPickNumber(row, 'low') ?? stockPickNumber(row, 'lowest') ?? stockPickNumber(row, 'close') ?? 0,
      volume: stockPickNumber(row, 'volume') ?? undefined,
      amount: stockPickNumber(row, 'amount') ?? undefined,
      changeRate: stockPickNumber(row, 'changeRate') ?? stockPickNumber(row, 'rate') ?? undefined
    }))
    .filter((row) => Number.isFinite(row.close))
    .sort((a, b) => a.timeOrder - b.timeOrder);
}

function stockName(item: StockBaseItem) {
  return String(firstKnown(item.name, item.stockName, item.securityName, stockCode(item), '未命名股票') || '未命名股票');
}

function stockCode(item: StockBaseItem) {
  return String(firstKnown(item.code, item.stockCode, item.symbol, '') || '');
}

function stockMarket(item: StockBaseItem) {
  return String(firstKnown(item.market, item.exchange, item.type, '--') || '--');
}

function stockPrice(item: StockBaseItem) {
  return firstKnownNumber(item.price, item.current, item.latest, item.last, item.close);
}

function stockRate(item: StockBaseItem) {
  return stockRateValue(item) ?? 0;
}

function stockRateValue(item: StockBaseItem) {
  return firstKnownNumber(item.changeRate, item.rate, item.pct, item.percent, item.changePercent);
}

function stockShares(item: StockBaseItem) {
  return firstKnownNumber(
    (item as StockHoldingItem).shares,
    (item as StockHoldingItem).amount,
    (item as StockHoldingItem).quantity,
    (item as StockHoldingItem).count,
    (item as StockHoldingItem).holdAmount
  );
}

function stockMarketValue(item: StockBaseItem) {
  return firstKnownNumber(
    (item as StockHoldingItem).marketValue,
    (item as StockHoldingItem).value,
    (item as StockHoldingItem).totalValue
  );
}

function stockProfit(item: StockBaseItem) {
  return firstKnownNumber(
    (item as StockHoldingItem).totalProfit,
    (item as StockHoldingItem).profit,
    (item as StockHoldingItem).holdingProfit,
    (item as StockHoldingItem).income,
    (item as StockHoldingItem).gain
  );
}

function stockPickNumber(item: Record<string, unknown>, ...keys: string[]) {
  const values = keys.map((key) => item[key]);
  return firstKnownNumber(...values);
}

function firstKnownNumber(...values: unknown[]) {
  for (const value of values) {
    const n = toFiniteNumber(value);
    if (n !== null) return n;
  }

  return null;
}

function toFiniteNumber(value: unknown) {
  if (value === null || value === undefined || value === '') return null;
  const n = Number(String(value).replace(/,/g, '').replace('%', ''));
  return Number.isFinite(n) ? n : null;
}

function stockPriceText(item: StockBaseItem) {
  const value = stockPrice(item);
  return value === null ? '--' : value.toFixed(value >= 100 ? 2 : 3);
}

function stockRateText(item: StockBaseItem) {
  const value = stockRateValue(item);
  return value === null ? '--' : signedPercent(value);
}

function stockPercentText(value: unknown) {
  const n = toFiniteNumber(value);
  return n === null ? '--' : signedPercent(n);
}

function stockSharesText(item: StockBaseItem) {
  const value = stockShares(item);
  return value === null ? '--' : value.toFixed(value % 1 === 0 ? 0 : 2);
}

function stockMoneyText(value: unknown) {
  const n = toFiniteNumber(value);
  return n === null ? '--' : maskByPrivacy(formatMoney(n), privacyMode.value, 1);
}

function stockSignedMoneyText(value: unknown) {
  const n = toFiniteNumber(value);
  return n === null ? '--' : maskByPrivacy(signedMoney(n), privacyMode.value, 1);
}

function stockItemKey(item: StockBaseItem, scope: string) {
  return `${scope}-${stockMarket(item)}-${stockCode(item)}-${String(item.id || '')}`;
}

function confirmModal(content: string) {
  return new Promise<boolean>((resolve) => {
    uni.showModal({
      title: '确认操作',
      content,
      success: (result) => resolve(!!result.confirm),
      fail: () => resolve(false)
    });
  });
}

async function loadProfile() {
  if (!sessionState.username || profileLoading.value) return;
  if (profileLoadedAt.value > 0 && Date.now() - profileLoadedAt.value < PROFILE_PAGE_CACHE_TTL) return;

  profileLoading.value = true;
  try {
    const profile = await getProfile(sessionState.username);
    const username = pickUsername(profile, sessionState.username);
    const displayName = pickDisplayName(profile, username);
    const avatar = pickAvatar(profile) || sessionState.avatarDataUrl || sessionState.avatarUrl;
    saveSession({
      username,
      displayName,
      avatarDataUrl: avatar,
      loginTime: sessionState.loginTime || Date.now()
    });
    profileLoadedAt.value = Date.now();
  } finally {
    profileLoading.value = false;
  }
}

function openProfile() {
  uni.navigateTo({ url: '/pages/profile/index' });
}

function requireLogin() {
  if (sessionState.username) return true;

  uni.showToast({ title: LOGIN_REQUIRED_TIP, icon: 'none', duration: 2200 });
  setTimeout(() => navigateToLogin(), 500);
  return false;
}

function navigateToLogin() {
  uni.navigateTo({
    url: '/pages/login/index',
    fail: () => {
      uni.redirectTo({ url: '/pages/login/index' });
    }
  });
}

function normalizePrivacyMode(value: unknown): PrivacyMode {
  const n = Number(value);
  return n === 0 || n === 1 || n === 2 || n === 3 ? n : 2;
}

function togglePrivacyMode() {
  const next = ((privacyMode.value + 1) % 4) as PrivacyMode;
  privacyMode.value = next;
  setStorage(PRIVACY_KEY, next);
}

function displayMoney(value: unknown, requiredMode: PrivacyMode, sign: boolean) {
  return maskByPrivacy(sign ? signedMoney(value) : formatMoney(value), privacyMode.value, requiredMode);
}

function archiveMoney(value: unknown) {
  const n = Number(value);
  return Number.isFinite(n) ? formatMoney(n) : '--';
}

function displayPercent(value: unknown, requiredMode: PrivacyMode) {
  return maskByPrivacy(signedPercent(value), privacyMode.value, requiredMode);
}

function signedPlainPercent(value: unknown) {
  return signedPercent(value);
}

function displayFundCost(fund: FundView) {
  if (privacyMode.value !== 0) return '****';
  return fund.costValue && fund.costValue > 0 ? formatMoney(fund.costValue) : '未设置';
}

function numericOrDash(value: unknown, digits = 2) {
  const n = Number(value);
  return Number.isFinite(n) ? n.toFixed(digits) : '--';
}

function fundNavLabel(fund: FundView) {
  const fields = getFundNavFields(fund);
  if (fields.nav !== null && fields.estimate !== null) return '净值/估值';
  if (fields.nav !== null) return '净值';
  if (fields.estimate !== null) return '估值';
  return '净值/估值';
}

function fundNavText(fund: FundView) {
  const fields = getFundNavFields(fund);
  const navText = fields.nav === null ? '' : fields.nav.toFixed(4);
  const estimateText = fields.estimate === null ? '' : fields.estimate.toFixed(4);

  if (navText && estimateText) return `${navText} / ${estimateText}`;
  if (navText) return navText;
  if (estimateText) return estimateText;
  return '--';
}

function fundDeviationValue(fund: FundView) {
  return getFundNavFields(fund).deviation;
}

function fundDeviationText(fund: FundView) {
  const value = fundDeviationValue(fund);
  return value === null ? '--' : signedPercent(value);
}

function getFundNavFields(fund: FundView | FundTodayItem) {
  const source = fund as Record<string, unknown>;
  const nav = firstKnownNavNumber(
    source.todayNav,
    source.TodayNav,
    source.nav,
    source.Nav,
    source.netValue,
    source.NetValue,
    source.latestNav,
    source.latestNetValue,
    source.unitNetValue,
    source.dwjz,
    source.DWJZ,
    source.actualNav,
    source.actualNetValue
  );
  const estimate = firstKnownNavNumber(
    source.estimate,
    source.valuation,
    source.estimatedNav,
    source.estimateNav,
    source.valuationValue,
    source.currentEstimate,
    source.gsz,
    source.GSZ
  );
  const estimateRate = firstKnownPercentNumber(
    source.estimateRate,
    source.valuationRate,
    source.gszzl,
    source.GSZZL,
    source.estimatedRate
  );
  const deviation = firstKnownPercentNumber(
    source.deviation,
    source.estimateDeviation,
    source.valuationDeviation,
    source.navDeviation,
    source.diffRate,
    source.DiffRate,
    source.premiumRate
  );

  return { nav, estimate, estimateRate, deviation };
}

function firstKnownNavNumber(...values: unknown[]) {
  for (const value of values) {
    const n = toFiniteNumber(value);
    if (n !== null) return n;
  }

  return null;
}

function firstKnownPercentNumber(...values: unknown[]) {
  for (const value of values) {
    const n = toFiniteNumber(value);
    if (n !== null) return n;
  }

  return null;
}

function firstKnown(...values: unknown[]) {
  for (const value of values) {
    if (value !== null && value !== undefined && value !== '') return value;
  }

  return null;
}

function confidenceToneClass(tone: 'high' | 'medium' | 'low') {
  if (tone === 'high') return 'confidence-high';
  if (tone === 'medium') return 'confidence-medium';
  return 'confidence-low';
}

function todayTrendPoints(fund: FundView) {
  const rows = Array.isArray(fund.data) ? fund.data : [];
  const points = rows
    .map((point, index) => {
      if (Array.isArray(point)) {
        return {
          time: parsePointTime(point[0], index),
          value: Number(point[1])
        };
      }
      if (point && typeof point === 'object') {
        const row = point as Record<string, unknown>;
        return {
          time: parsePointTime(firstKnown(row.time, row.date, row.datetime, row.day), index),
          value: Number(firstKnown(row.rate, row.currentRate, row.value, row.close, row.gsz, row.gszzl))
        };
      }
      return {
        time: index,
        value: Number(point)
      };
    })
    .filter((point) => Number.isFinite(point.value))
    .sort((a, b) => a.time - b.time)
    .map((point) => point.value);

  return points.length > 1 ? points : [];
}

function trendPointCount(fund: FundView) {
  return todayTrendPoints(fund).length;
}

function shouldRenderFundTrend(fund: FundView, index: number) {
  return index < DEFAULT_RENDERED_TREND_COUNT || Boolean(expandedTrendKeys.value[fund.viewKey]);
}

function expandFundTrend(fund: FundView) {
  if (!fund.viewKey) return;
  expandedTrendKeys.value = {
    ...expandedTrendKeys.value,
    [fund.viewKey]: true
  };
}

function fundTrendCanvasId(fund: FundView) {
  const raw = `${fund.viewKey || fund.code || 'fund'}`;
  const safe = raw.replace(/[^a-zA-Z0-9_-]/g, '-');
  return `fund-trend-${safe}`;
}

function parsePointTime(value: unknown, fallback: number) {
  if (value instanceof Date) return value.getTime();
  const text = String(value || '').replace(/\//g, '-');
  const time = Date.parse(text);
  return Number.isFinite(time) ? time : fallback;
}

function chooseImage() {
  return new Promise<string | null>((resolve, reject) => {
    uni.chooseImage({
      count: 1,
      sourceType: ['album', 'camera'],
      success: (result) => resolve(result.tempFilePaths?.[0] || null),
      fail: (error) => {
        const message = String((error as { errMsg?: unknown })?.errMsg || '');
        if (/cancel/i.test(message)) {
          resolve(null);
          return;
        }
        reject(error);
      }
    });
  });
}

async function startFundOcr() {
  if (!requireLogin() || ocrBusy.value) return;

  try {
    const filePath = await chooseImage();
    if (!filePath) return;

    ocrBusy.value = true;
    uni.showLoading({ title: '基金解析中', mask: true });
    const result = await previewFundOcr(sessionState.username, filePath);
    if (result.success === false) {
      throw new Error(String(result.message || 'OCR 识别失败'));
    }

    ocrItems.value = Array.isArray(result.items) ? result.items : [];
    ocrDiagnostics.value = Array.isArray(result.diagnostics) ? result.diagnostics : [];
    ocrProfitUpdateState.value = String(result.profitUpdateState || '');
    ocrPreviewVisible.value = true;
    uni.showToast({ title: `识别到 ${ocrItems.value.length} 条基金`, icon: 'none' });
  } catch (error) {
    console.warn('[fund:ocr-preview]', error);
    uni.showToast({ title: getErrorMessage(error, 'OCR 识别失败'), icon: 'none' });
  } finally {
    ocrBusy.value = false;
    uni.hideLoading();
  }
}

async function confirmOcrImport() {
  if (!requireLogin() || ocrConfirming.value || ocrItems.value.length === 0) return;

  ocrConfirming.value = true;
  try {
    const result = await confirmFundOcr({
      username: sessionState.username,
      items: ocrItems.value
    });
    if (result.success === false) {
      throw new Error(String(result.message || 'OCR 导入失败'));
    }

    uni.showToast({ title: result.message || '导入成功', icon: 'none' });
    closeOcrPreview();
    await loadFunds(true);
  } catch (error) {
    console.warn('[fund:ocr-confirm]', error);
    uni.showToast({ title: getErrorMessage(error, 'OCR 导入失败'), icon: 'none' });
  } finally {
    ocrConfirming.value = false;
  }
}

function closeOcrPreview() {
  ocrPreviewVisible.value = false;
}

async function openFundHistory(fund: FundView) {
  if (!requireLogin()) return;
  if (!fund.code) {
    uni.showToast({ title: '基金代码缺失，无法读取历史', icon: 'none' });
    return;
  }

  historyModal.value = {
    show: true,
    loading: true,
    code: fund.code,
    name: fund.name || fund.code,
    rows: []
  };

  try {
    const rows = await getFundArchives(sessionState.username, fund.code, 365);
    historyModal.value.rows = Array.isArray(rows) ? rows : [];
  } catch (error) {
    console.warn('[fund:history]', error);
    uni.showToast({ title: getErrorMessage(error, '历史读取失败'), icon: 'none' });
  } finally {
    historyModal.value.loading = false;
  }
}

function closeFundHistory() {
  historyModal.value.show = false;
}

function formatDate(value: unknown) {
  if (!value) return '--';
  return String(value).slice(0, 10);
}

function historyRowKey(row: FundArchiveRow, index: number) {
  return `${row.fundCode || historyModal.value.code || 'fund'}-${formatDate(row.recordDate)}-${index}`;
}

function ocrPick(item: OcrImportPreviewItem, camelKey: string, pascalKey: string) {
  return (item as Record<string, unknown>)[camelKey] ?? (item as Record<string, unknown>)[pascalKey];
}

function ocrText(item: OcrImportPreviewItem, camelKey: string, pascalKey: string) {
  const value = ocrPick(item, camelKey, pascalKey);
  return value === null || value === undefined ? '' : String(value);
}

function ocrNumber(item: OcrImportPreviewItem, camelKey: string, pascalKey: string) {
  return moneyDash(ocrPick(item, camelKey, pascalKey), false);
}

function ocrParticipatesTodayText(item: OcrImportPreviewItem) {
  const value = ocrPick(item, 'participatesToday', 'ParticipatesToday');
  return value === false ? '否' : '是';
}

function ocrItemKey(item: OcrImportPreviewItem, index: number) {
  return `${ocrText(item, 'code', 'Code') || ocrText(item, 'name', 'Name') || 'ocr'}-${index}`;
}

function getErrorMessage(error: unknown, fallback: string) {
  if (error instanceof Error && error.message) return error.message;
  if (error && typeof error === 'object' && 'errMsg' in error) {
    return String((error as { errMsg?: unknown }).errMsg || fallback);
  }
  return fallback;
}
</script>

<style lang="scss" scoped>
@import '../../styles/variables.scss';
@import '../../styles/mixins.scss';

.home-page {
  display: flex;
  flex-direction: column;
  gap: 30rpx;
  padding-top: 34rpx;
}

.dashboard-stale-bar {
  text-align: center;
  padding: 8rpx 0;
}

.dashboard-stale-text {
  font-size: 22rpx;
  color: rgba(255, 200, 60, 0.8);
  letter-spacing: 1rpx;
}

.home-header {
  align-items: center;
  min-height: 42rpx;
}

.compact-subtitle {
  margin-top: 0;
  font-size: 24rpx;
  font-weight: 800;
  color: var(--text-secondary);
}

.title-block {
  min-width: 0;
}

.action-card {
  display: flex;
  flex-wrap: nowrap;
  gap: 18rpx;
  align-items: center;
  justify-content: space-between;
  padding: 20rpx;
  background: var(--card-bg);
  border-color: var(--border-color);
}

.user-panel {
  flex: 1 1 210rpx;
  min-width: 0;
  display: flex;
  align-items: center;
  justify-content: center;
}

.account-button {
  width: 100%;
  min-width: 0;
  min-height: 76rpx;
  padding: 0 18rpx;
  border-radius: 999rpx;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12rpx;
  color: var(--text-primary);
  background: var(--control-bg);
  border: 1rpx solid var(--border-color);
}

.account-button.guest {
  color: var(--button-primary-text);
  background: var(--button-primary-bg);
  box-shadow: 0 14rpx 32rpx rgba(139, 92, 246, 0.14);
}

.avatar-img,
.avatar-fallback {
  width: 56rpx;
  height: 56rpx;
  border-radius: 50%;
  flex-shrink: 0;
  border: 2rpx solid var(--border-color);
  box-shadow: 0 14rpx 34rpx rgba(90, 167, 255, 0.2), 0 0 18rpx rgba(139, 124, 246, 0.14);
}

.avatar-fallback {
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--button-primary-text);
  font-size: 28rpx;
  font-weight: 900;
  background: $rainbow-gradient;
}

.account-copy {
  min-width: 0;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
}

.account-title,
.account-subtitle {
  max-width: 170rpx;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.account-title {
  font-size: 23rpx;
  font-weight: 900;
  line-height: 1.1;
}

.account-subtitle {
  margin-top: 5rpx;
  color: var(--text-muted);
  font-size: 18rpx;
  line-height: 1.1;
}

.account-button.guest .account-subtitle {
  color: rgba(255, 255, 255, 0.84);
}

.privacy-button,
.secondary-action,
.modal-close {
  border-radius: 999rpx;
  color: var(--text-secondary);
  background: var(--control-bg);
  border: 1rpx solid var(--border-color);
}

.action-buttons {
  flex: 1 1 250rpx;
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 0;
  min-width: 230rpx;
  max-width: 300rpx;
}

.ocr-button {
  @include primary-gradient;
  min-height: 80rpx;
  border-radius: 999rpx;
  font-size: 24rpx;
  font-weight: 900;
}

.privacy-button {
  flex: 0 0 138rpx;
  min-height: 72rpx;
  padding: 0 12rpx;
  font-size: 22rpx;
  font-weight: 900;
  line-height: 72rpx;
}

.asset-switch {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12rpx;
  padding: 12rpx;
  border-radius: 999rpx;
}

.asset-switch-btn {
  min-height: 72rpx;
  border-radius: 999rpx;
  color: var(--text-muted);
  background: transparent;
  border: 1rpx solid transparent;
  font-size: 26rpx;
  font-weight: 900;
  line-height: 72rpx;
}

.asset-switch-btn.active {
  color: var(--text-primary);
  background: $rainbow-gradient;
  box-shadow: 0 16rpx 42rpx rgba(139, 92, 246, 0.2);
}

.notice-card {
  padding: 22rpx 26rpx;
  color: var(--text-muted);
  font-size: 22rpx;
  line-height: 1.55;
}

.mode-pane {
  display: flex;
  flex-direction: column;
  gap: 30rpx;
}

.hero-card {
  padding: 38rpx;
  background:
    radial-gradient(circle at 18% 0%, rgba(255, 95, 162, 0.22), transparent 34%),
    radial-gradient(circle at 86% 4%, rgba(56, 189, 248, 0.18), transparent 34%),
    linear-gradient(135deg, rgba(14, 25, 50, 0.9), rgba(35, 42, 91, 0.68));
  box-shadow: 0 24rpx 64rpx rgba(3, 7, 18, 0.2), 0 0 34rpx rgba(139, 92, 246, 0.11);
}

.hero-top,
.fund-head,
.fund-foot,
.list-head,
.report-line,
.rank-row,
.modal-head,
.modal-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20rpx;
}

.hero-sub,
.list-subtitle,
.modal-subtitle,
.confidence-reason {
  display: block;
  margin-top: 8rpx;
  color: var(--text-muted);
  font-size: 22rpx;
}

.hero-money {
  display: block;
  margin-top: 22rpx;
  color: var(--text-primary);
  font-size: 64rpx;
  line-height: 1.12;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 18rpx;
  margin-top: 34rpx;
}

.summary-cell,
.fund-metric {
  min-width: 0;
  box-sizing: border-box;
  padding: 20rpx;
  border-radius: 34rpx;
  background:
    radial-gradient(circle at 22% 0%, rgba(255, 95, 162, 0.06), transparent 32%),
    rgba(12, 20, 39, 0.34);
  border: 1rpx solid rgba(191, 219, 254, 0.1);
}

.glow-cell {
  box-shadow: inset 0 0 0 1rpx rgba(255, 255, 255, 0.12), 0 0 28rpx rgba(139, 92, 246, 0.12);
}

.metric-label {
  color: var(--text-muted);
  font-size: 22rpx;
}

.metric-value {
  display: inline-flex;
  max-width: 100%;
  margin-top: 10rpx;
  color: var(--text-primary);
  font-size: 30rpx;
  font-weight: 900;
  white-space: nowrap;
  word-break: keep-all;
  overflow: hidden;
  text-overflow: ellipsis;
}

.nav-value {
  display: block;
  width: 100%;
  font-size: 28rpx;
  line-height: 1.25;
  white-space: nowrap;
  word-break: keep-all;
}

.nav-deviation {
  display: block;
  white-space: nowrap;
  font-weight: 900;
}

.feature-grid,
.top-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 18rpx;
}

.feature-card,
.rank-card,
.fund-card {
  padding: 32rpx;
}

.feature-title {
  display: block;
  color: var(--text-primary);
  font-size: 28rpx;
  font-weight: 900;
}

.feature-subtitle {
  display: block;
  margin-top: 10rpx;
  min-height: 64rpx;
  color: var(--text-muted);
  font-size: 22rpx;
  line-height: 1.45;
}

.feature-number {
  display: block;
  margin: 18rpx 0;
  font-size: 40rpx;
}

.report-line {
  padding-top: 12rpx;
  color: var(--text-muted);
  font-size: 22rpx;
}

.report-line text:last-child {
  max-width: 190rpx;
  text-align: right;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-weight: 900;
}

.warning-text {
  color: #fbbf24;
}

.confidence-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12rpx;
  margin-top: 16rpx;
  padding: 14rpx;
  border-radius: 24rpx;
  background: rgba(12, 20, 39, 0.32);
}

.confidence-copy {
  min-width: 0;
}

.confidence-copy text:first-child {
  display: block;
  max-width: 190rpx;
  color: var(--text-secondary);
  font-size: 22rpx;
  font-weight: 900;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.confidence-copy text:last-child {
  display: block;
  margin-top: 4rpx;
  color: var(--text-muted);
  font-size: 20rpx;
}

.confidence-score,
.reliability {
  font-weight: 900;
}

.confidence-high {
  color: #22c55e;
}

.confidence-medium {
  color: #fbbf24;
}

.confidence-low {
  color: $profit-red;
}

.small-section-title {
  font-size: 28rpx;
}

.rank-row {
  margin-top: 18rpx;
  color: var(--text-secondary);
  font-size: 22rpx;
}

.rank-row text:first-child {
  max-width: 190rpx;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.muted-empty {
  margin-top: 18rpx;
  color: var(--text-muted);
  font-size: 22rpx;
}

.fund-card {
  background:
    linear-gradient(90deg, rgba(255, 95, 162, 0.5), rgba(139, 92, 246, 0.45), rgba(56, 189, 248, 0.42)) top left / 100% 6rpx no-repeat,
    radial-gradient(circle at 14% 0%, rgba(255, 95, 162, 0.12), transparent 34%),
    radial-gradient(circle at 92% 8%, rgba(56, 189, 248, 0.12), transparent 34%),
    linear-gradient(145deg, rgba(34, 49, 86, 0.58), rgba(17, 27, 52, 0.46));
}

.fund-title {
  min-width: 0;
}

.fund-name {
  display: block;
  color: var(--text-primary);
  font-size: 31rpx;
  font-weight: 900;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.fund-code {
  display: block;
  margin-top: 8rpx;
  color: var(--text-muted);
  font-size: 22rpx;
}

.fund-status {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 10rpx;
}

.status-pill {
  min-height: 42rpx;
  padding: 0 16rpx;
  border-radius: 999rpx;
  color: var(--text-secondary);
  font-size: 20rpx;
  line-height: 42rpx;
  font-weight: 900;
}

.status-pill.settled {
  color: #bbf7d0;
  background: rgba(16, 185, 129, 0.16);
}

.status-pill.holiday {
  color: #fde68a;
  background: rgba(245, 158, 11, 0.16);
}

.status-pill.tracking {
  color: #dbeafe;
  background: rgba(59, 130, 246, 0.16);
}

.rate {
  font-size: 34rpx;
}

.calibration-strip {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16rpx;
  margin-top: 22rpx;
  padding: 18rpx;
  border-radius: 20rpx;
  color: #c4b5fd;
  background: rgba(139, 92, 246, 0.12);
  font-size: 22rpx;
}

.calibration-strip text:last-child {
  max-width: 360rpx;
  color: var(--text-muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.pending-trade-strip {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16rpx;
  margin-top: 18rpx;
  padding: 16rpx 18rpx;
  border-radius: 20rpx;
  border: 1rpx solid rgba(251, 191, 36, 0.28);
  color: #fbbf24;
  background: rgba(251, 191, 36, 0.1);
  font-size: 22rpx;
}

.pending-trade-strip text:last-child {
  max-width: 420rpx;
  color: var(--text-muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.trend-panel,
.history-chart-block {
  margin-top: 22rpx;
  padding: 18rpx;
  border-radius: 32rpx;
  background:
    radial-gradient(circle at 20% 0%, rgba(255, 95, 162, 0.08), transparent 30%),
    rgba(12, 20, 39, 0.28);
  border: 1rpx solid rgba(191, 219, 254, 0.1);
}

.trend-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16rpx;
  margin-bottom: 12rpx;
  color: var(--text-muted);
  font-size: 22rpx;
}

.trend-head text:first-child {
  color: var(--text-secondary);
  font-weight: 900;
}

.fund-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 18rpx;
  margin-top: 26rpx;
}

.accent-green {
  background: rgba(16, 185, 129, 0.1);
}

.accent-blue {
  background: rgba(79, 172, 254, 0.1);
}

.metric-hint {
  display: block;
  margin-top: 6rpx;
  color: var(--text-muted);
  font-size: 20rpx;
}

.fund-foot {
  margin-top: 24rpx;
  padding-top: 22rpx;
  border-top: 1rpx solid rgba(148, 163, 184, 0.16);
}

.history-button {
  min-width: 98rpx;
  height: 48rpx;
  padding: 0 18rpx;
  border-radius: 999rpx;
  color: #bae6fd;
  background: rgba(56, 189, 248, 0.14);
  border: 1rpx solid rgba(56, 189, 248, 0.28);
  font-size: 22rpx;
  font-weight: 900;
}

.expand-trend-button {
  width: 100%;
  height: 112rpx;
  border-radius: 24rpx;
  color: #dbeafe;
  background:
    linear-gradient(135deg, rgba(255, 95, 162, 0.08), rgba(139, 92, 246, 0.12), rgba(56, 189, 248, 0.08)),
    rgba(15, 23, 42, 0.28);
  border: 1rpx solid rgba(255, 255, 255, 0.1);
  font-size: 24rpx;
  font-weight: 900;
}

.stock-mode {
  gap: 24rpx;
}

.stock-search-card,
.stock-list-card,
.stock-chart-card {
  padding: 30rpx;
  background:
    radial-gradient(circle at 12% 12%, rgba(90, 167, 255, 0.14), transparent 34%),
    linear-gradient(145deg, rgba(34, 49, 86, 0.58), rgba(17, 27, 52, 0.5));
}

.stock-section-head,
.stock-card-head,
.stock-search-row,
.stock-result-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18rpx;
}

.stock-search-row {
  margin-top: 22rpx;
}

.stock-search-input,
.holding-form input,
.stock-ocr-grid input {
  min-width: 0;
  min-height: 76rpx;
  box-sizing: border-box;
  padding: 0 22rpx;
  border-radius: 999rpx;
  color: var(--text-primary);
  background: rgba(15, 23, 42, 0.44);
  border: 1rpx solid rgba(191, 219, 254, 0.12);
  font-size: 24rpx;
}

.stock-search-input {
  flex: 1;
}

.input-placeholder {
  color: rgba(180, 198, 232, 0.48);
}

.stock-primary-button {
  @include primary-gradient;
  width: 132rpx;
  min-height: 76rpx;
  border-radius: 999rpx;
  font-size: 24rpx;
  font-weight: 900;
}

.stock-result-list {
  margin-top: 22rpx;
}

.stock-result-row {
  flex-wrap: wrap;
  padding: 20rpx 0;
  border-top: 1rpx solid rgba(148, 163, 184, 0.12);
}

.stock-main {
  min-width: 0;
  flex: 1;
}

.stock-name {
  display: block;
  color: var(--text-primary);
  font-size: 28rpx;
  font-weight: 900;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.stock-code {
  display: block;
  margin-top: 8rpx;
  color: var(--text-muted);
  font-size: 21rpx;
}

.stock-market-data {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 6rpx;
  min-width: 118rpx;
}

.stock-market-data.right {
  align-items: flex-end;
}

.stock-market-data .finance-number {
  color: var(--text-primary);
  font-size: 30rpx;
  font-weight: 900;
  white-space: nowrap;
}

.stock-rate {
  font-size: 22rpx;
  font-weight: 900;
  white-space: nowrap;
}

.stock-inline-actions,
.stock-actions {
  width: 100%;
  display: flex;
  flex-wrap: wrap;
  gap: 12rpx;
}

.stock-inline-actions button,
.stock-actions button {
  min-width: 116rpx;
  min-height: 58rpx;
  padding: 0 18rpx;
  border-radius: 999rpx;
  color: #dbeafe;
  background: rgba(59, 130, 246, 0.14);
  border: 1rpx solid rgba(96, 165, 250, 0.2);
  font-size: 22rpx;
  font-weight: 900;
  line-height: 58rpx;
}

.stock-actions button.danger,
.stock-inline-actions button.danger {
  color: #fecaca;
  background: rgba(248, 113, 113, 0.12);
  border-color: rgba(248, 113, 113, 0.22);
}

.stock-card {
  margin-top: 20rpx;
  padding: 24rpx;
  border-radius: 30rpx;
  background: rgba(12, 20, 39, 0.32);
  border: 1rpx solid rgba(191, 219, 254, 0.1);
}

.compact-stock-card {
  padding-bottom: 20rpx;
}

.stock-metric-grid,
.stock-ocr-grid,
.holding-form {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14rpx;
  margin-top: 20rpx;
}

.stock-metric-grid view,
.stock-ocr-grid view,
.holding-form view {
  min-width: 0;
  padding: 16rpx;
  border-radius: 22rpx;
  background: rgba(15, 23, 42, 0.38);
}

.stock-metric-grid text,
.stock-ocr-grid text,
.holding-form text {
  display: block;
  font-size: 22rpx;
}

.stock-metric-grid text:first-child,
.stock-ocr-grid text:first-child,
.holding-form text:first-child {
  color: var(--text-muted);
}

.stock-metric-grid text:last-child,
.stock-ocr-grid text:last-child {
  margin-top: 8rpx;
  color: var(--text-primary);
  font-weight: 900;
  white-space: nowrap;
}

.kline-tabs {
  display: flex;
  gap: 10rpx;
  margin-top: 22rpx;
}

.kline-tab {
  flex: 1;
  min-height: 58rpx;
  border-radius: 999rpx;
  color: var(--text-muted);
  background: rgba(191, 219, 254, 0.07);
  border: 1rpx solid rgba(191, 219, 254, 0.1);
  font-size: 22rpx;
  font-weight: 900;
  line-height: 58rpx;
}

.kline-tab.active {
  color: var(--text-primary);
  background: linear-gradient(135deg, rgba(59, 130, 246, 0.8), rgba(124, 58, 237, 0.74));
  border-color: rgba(191, 219, 254, 0.24);
}

.stock-trend-panel {
  margin-top: 20rpx;
}

.stock-ocr-row {
  padding: 22rpx 0;
  border-top: 1rpx solid rgba(148, 163, 184, 0.14);
}

.stock-ocr-grid input {
  width: 100%;
  min-height: 58rpx;
  margin-top: 8rpx;
  padding: 0 12rpx;
  border-radius: 16rpx;
  font-size: 22rpx;
}

.holding-editor {
  width: 100%;
  padding: 30rpx;
}

.modal-mask {
  position: fixed;
  inset: 0;
  z-index: 90;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 34rpx;
  box-sizing: border-box;
  background: rgba(2, 6, 23, 0.82);
}

.ocr-modal,
.history-modal {
  width: 100%;
  max-height: 82vh;
  padding: 30rpx;
  display: flex;
  flex-direction: column;
}

.history-modal {
  height: 82vh;
}

.modal-close {
  width: 56rpx;
  height: 56rpx;
  padding: 0;
  font-size: 42rpx;
  line-height: 54rpx;
}

.ocr-scroll {
  max-height: 58vh;
  margin-top: 24rpx;
}

.history-loading {
  padding: 70rpx 0;
  color: var(--text-muted);
  text-align: center;
  font-size: 24rpx;
}

.history-list {
  max-height: 34vh;
  margin-top: 20rpx;
}

.history-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18rpx;
  padding: 18rpx 0;
  border-top: 1rpx solid rgba(148, 163, 184, 0.14);
}

.history-date {
  display: block;
  color: var(--text-primary);
  font-size: 24rpx;
  font-weight: 900;
}

.history-sub {
  display: block;
  margin-top: 8rpx;
  color: var(--text-muted);
  font-size: 21rpx;
}

.history-values {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 6rpx;
}

.small-rate {
  font-size: 22rpx;
  font-weight: 900;
  white-space: nowrap;
}

.ocr-row {
  padding: 22rpx 0;
  border-top: 1rpx solid rgba(148, 163, 184, 0.14);
}

.ocr-row-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18rpx;
}

.ocr-name {
  min-width: 0;
  color: var(--text-primary);
  font-size: 28rpx;
  font-weight: 900;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ocr-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12rpx;
  margin-top: 18rpx;
}

.ocr-grid view {
  padding: 14rpx;
  border-radius: 16rpx;
  background: rgba(15, 23, 42, 0.4);
}

.ocr-grid text {
  display: block;
  font-size: 22rpx;
}

.ocr-grid text:first-child {
  color: var(--text-muted);
}

.ocr-grid text:last-child {
  margin-top: 6rpx;
  color: var(--text-primary);
  font-weight: 900;
}

.ocr-warning,
.diagnostics text {
  display: block;
  margin-top: 14rpx;
  color: #fbbf24;
  font-size: 22rpx;
}

.ocr-reason {
  display: block;
  margin-top: 8rpx;
  color: var(--text-muted);
  font-size: 22rpx;
}

.ocr-update-state {
  text-align: center;
  margin-bottom: 16rpx;
}

.update-badge {
  font-size: 22rpx;
  padding: 4rpx 16rpx;
  border-radius: 8rpx;
  font-weight: 600;
}

.update-badge.updated {
  background: rgba(16, 185, 129, 0.15);
  color: #10b981;
}

.update-badge.not-updated {
  background: rgba(245, 158, 11, 0.15);
  color: #f59e0b;
}

.inner-empty {
  margin: 0;
}

.modal-actions {
  margin-top: 22rpx;
}

.secondary-action,
.confirm-button {
  width: 48%;
  min-height: 82rpx;
  font-size: 26rpx;
  font-weight: 900;
}

button[disabled] {
  opacity: 0.62;
}
</style>
