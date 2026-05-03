CREATE TABLE IF NOT EXISTS StockHoldings (
  Id INT NOT NULL AUTO_INCREMENT,
  Username VARCHAR(50) NOT NULL,
  StockCode VARCHAR(10) NOT NULL,
  Market VARCHAR(20) NOT NULL,
  StockName VARCHAR(120) NOT NULL,
  Shares DECIMAL(20,4) NOT NULL DEFAULT 0,
  CostPrice DECIMAL(20,4) NOT NULL DEFAULT 0,
  CostAmount DECIMAL(20,4) NOT NULL DEFAULT 0,
  LastPrice DECIMAL(20,4) NULL,
  LastRate DECIMAL(20,4) NULL,
  LastMarketValue DECIMAL(20,4) NULL,
  LastProfit DECIMAL(20,4) NULL,
  LastProfitRate DECIMAL(20,4) NULL,
  CreatedAt DATETIME(6) NOT NULL,
  UpdatedAt DATETIME(6) NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY IX_StockHolding_User_Code (Username, StockCode),
  KEY IX_StockHolding_User (Username)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS StockWatchItems (
  Id INT NOT NULL AUTO_INCREMENT,
  Username VARCHAR(50) NOT NULL,
  StockCode VARCHAR(10) NOT NULL,
  Market VARCHAR(20) NOT NULL,
  StockName VARCHAR(120) NOT NULL,
  SortOrder INT NOT NULL DEFAULT 0,
  CreatedAt DATETIME(6) NOT NULL,
  UpdatedAt DATETIME(6) NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY IX_StockWatch_User_Code (Username, StockCode)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS StockOcrImportBatches (
  Id INT NOT NULL AUTO_INCREMENT,
  Username VARCHAR(50) NOT NULL,
  Source VARCHAR(40) NOT NULL,
  Status VARCHAR(30) NOT NULL,
  RawText LONGTEXT NOT NULL,
  DiagnosticsJson LONGTEXT NOT NULL,
  CreatedAt DATETIME(6) NOT NULL,
  ConfirmedAt DATETIME(6) NULL,
  PRIMARY KEY (Id),
  KEY IX_StockOcrBatch_User_Created (Username, CreatedAt)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS StockOcrImportItems (
  Id INT NOT NULL AUTO_INCREMENT,
  BatchId INT NOT NULL,
  StockCode VARCHAR(10) NOT NULL,
  Market VARCHAR(20) NOT NULL,
  StockName VARCHAR(120) NOT NULL,
  RecognizedName VARCHAR(120) NOT NULL,
  Shares DECIMAL(20,4) NULL,
  CostPrice DECIMAL(20,4) NULL,
  CostAmount DECIMAL(20,4) NULL,
  MarketValue DECIMAL(20,4) NULL,
  FloatingProfit DECIMAL(20,4) NULL,
  FloatingProfitRate DECIMAL(20,4) NULL,
  Action VARCHAR(30) NOT NULL,
  Note VARCHAR(200) NOT NULL,
  PRIMARY KEY (Id),
  KEY IX_StockOcrImportItems_BatchId (BatchId),
  CONSTRAINT FK_StockOcrImportItems_StockOcrImportBatches_BatchId FOREIGN KEY (BatchId) REFERENCES StockOcrImportBatches(Id) ON DELETE CASCADE
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS StockQuoteSnapshots (
  Id INT NOT NULL AUTO_INCREMENT,
  StockCode VARCHAR(10) NOT NULL,
  Market VARCHAR(20) NOT NULL,
  StockName VARCHAR(120) NOT NULL,
  LatestPrice DECIMAL(20,4) NOT NULL,
  ChangeAmount DECIMAL(20,4) NOT NULL,
  ChangeRate DECIMAL(20,4) NOT NULL,
  OpenPrice DECIMAL(20,4) NOT NULL,
  HighPrice DECIMAL(20,4) NOT NULL,
  LowPrice DECIMAL(20,4) NOT NULL,
  PreviousClose DECIMAL(20,4) NOT NULL,
  Volume DECIMAL(20,4) NOT NULL,
  Amount DECIMAL(20,4) NOT NULL,
  QuoteTime DATETIME(6) NOT NULL,
  CreatedAt DATETIME(6) NOT NULL,
  PRIMARY KEY (Id),
  KEY IX_StockQuote_Code_Time (StockCode, QuoteTime)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS StockKLineCaches (
  Id INT NOT NULL AUTO_INCREMENT,
  StockCode VARCHAR(10) NOT NULL,
  Market VARCHAR(20) NOT NULL,
  Period VARCHAR(20) NOT NULL,
  PayloadJson LONGTEXT NOT NULL,
  RefreshedAt DATETIME(6) NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY IX_StockKLine_Code_Period (StockCode, Period)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
