# 股票 OCR 预览数值显示修复

## 作用

股票 OCR 预览阶段不再只显示股票名称，同时显示并允许修正：

- 持股
- 成本价
- 成本金额
- OCR 市值
- OCR 盈亏
- OCR 收益率

确认写库时会把 `shares / costPrice / costAmount` 一起提交到后端。

## 覆盖文件

```text
wwwroot/index.html
```

## 使用

覆盖后重新部署前端。已经错误入库的股票需要先删除后重新 OCR，或者在 OCR 预览中手动修正后再确认写库。
