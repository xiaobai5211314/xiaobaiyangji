-- 兜底 SQL：如果 EF Migration 没有自动应用，可以在生产库 guzhi/实际数据库中手动执行。
-- 执行前请先备份数据库。

CREATE TABLE IF NOT EXISTS FundValuationEstimates (
    Id INT NOT NULL AUTO_INCREMENT,
    Username VARCHAR(50) NOT NULL,
    FundCode VARCHAR(20) NOT NULL,
    FundName VARCHAR(160) NOT NULL,
    TradeDate DATETIME(6) NOT NULL,
    EstimatedRate DOUBLE NOT NULL,
    EstimatedAssets DOUBLE NOT NULL,
    Source VARCHAR(40) NOT NULL,
    EstimatedAt DATETIME(6) NOT NULL,
    RawPayloadJson LONGTEXT NULL,
    PRIMARY KEY (Id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS FundValuationCalibrations (
    Id INT NOT NULL AUTO_INCREMENT,
    Username VARCHAR(50) NOT NULL,
    FundCode VARCHAR(20) NOT NULL,
    FundName VARCHAR(160) NOT NULL,
    TradeDate DATETIME(6) NOT NULL,
    EstimatedRate DOUBLE NOT NULL,
    ActualRate DOUBLE NOT NULL,
    ErrorRate DOUBLE NOT NULL,
    EstimatedAssets DOUBLE NOT NULL,
    ActualAssets DOUBLE NOT NULL,
    CorrectionOffset DOUBLE NOT NULL,
    SampleCount INT NOT NULL,
    Confidence VARCHAR(20) NOT NULL,
    CreatedAt DATETIME(6) NOT NULL,
    UpdatedAt DATETIME(6) NOT NULL,
    PRIMARY KEY (Id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS UserUiStates (
    Id INT NOT NULL AUTO_INCREMENT,
    Username VARCHAR(50) NOT NULL,
    StateKey VARCHAR(80) NOT NULL,
    StateJson LONGTEXT NOT NULL,
    UpdatedAt DATETIME(6) NOT NULL,
    PRIMARY KEY (Id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS UserInsightSnapshots (
    Id INT NOT NULL AUTO_INCREMENT,
    Username VARCHAR(50) NOT NULL,
    SnapshotType VARCHAR(40) NOT NULL,
    SnapshotDate DATETIME(6) NOT NULL,
    PayloadJson LONGTEXT NOT NULL,
    CreatedAt DATETIME(6) NOT NULL,
    UpdatedAt DATETIME(6) NOT NULL,
    PRIMARY KEY (Id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE UNIQUE INDEX IX_FundValuationEstimate_User_Code_Date ON FundValuationEstimates (Username, FundCode, TradeDate);
CREATE INDEX IX_FundValuationEstimate_User_Date ON FundValuationEstimates (Username, TradeDate);
CREATE UNIQUE INDEX IX_FundValuationCalibration_User_Code_Date ON FundValuationCalibrations (Username, FundCode, TradeDate);
CREATE INDEX IX_FundValuationCalibration_User_Code ON FundValuationCalibrations (Username, FundCode);
CREATE UNIQUE INDEX IX_UserUiState_User_Key ON UserUiStates (Username, StateKey);
CREATE UNIQUE INDEX IX_UserInsightSnapshot_User_Type_Date ON UserInsightSnapshots (Username, SnapshotType, SnapshotDate);

-- 如果上方索引语句因索引已存在报错，忽略即可；表结构仍可用。
