# 股票 OCR 坐标列解析修复

## 覆盖文件

- `Services/BaiduOcrService.cs`
- `Services/StockOcrParserService.cs`
- `Controllers/StockController.cs`

## 修复点

1. 股票 OCR 改用百度“含位置信息”的高精度 OCR 接口。
2. 后端读取 `words_result[].location.left/top/width/height`。
3. 解析股票持仓时不再只按文字顺序猜，而是根据表格列坐标识别：
   - 市值
   - 盈亏 / 盈亏率
   - 持仓 / 可用
   - 成本 / 现价
   - 当日盈亏
4. 对你这类券商截图，`3.720` 会作为现价，`4.342` 会作为成本价；持有盈亏由 `市值 - 持仓 * 成本价` 校验。

## 部署后操作

旧的错误股票记录已经写入数据库，需要先删掉再重新 OCR：

1. 股票页删除“胜利精密”错误记录。
2. 重新上传券商持仓截图。
3. OCR 预览里确认：
   - 持仓：600
   - 成本价：4.342
   - 市值：2232.00
   - 盈亏：约 -373

## 验证日志

如果仍然识别不准，在服务器看 OCR 诊断：

```bash
sudo journalctl -u guzhi-assistant.service -n 200 --no-pager | grep -i -E "OCR|stock|error|exception"
```
