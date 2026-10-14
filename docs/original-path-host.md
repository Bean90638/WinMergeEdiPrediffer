# 官方 WinMerge 相容性與移轉

目前使用官方 WinMerge x64；CI 以官方 2.16.58.2 ZIP 驗證，不修改主程式。

## 從單一 Router 升級

1. 關閉 WinMerge，保留原本 `profiles.json`。
2. 使用新版管理器載入設定，按「套用到 WinMerge」。
3. 管理器備份舊安裝清單及其擁有的檔案，移除舊 Router 入口，產生每組的獨立外掛。
4. 開啟官方 WinMerge，使用自動預差異化。可比對兩個檔案、資料夾，或從資料夾結果開啟檔案差異。

不再需要 `PluginOriginalFilePath`、`PrivateBuild` 標記、修正版主程式或 MFC 建置配套。自訂官方安裝位置可設定 `WINMERGE_EDI_WINMERGE_PATH`。

## 選組與更新

WinMerge 用原始完整路徑匹配 `PluginFileFilters`，外掛只執行入口對應的規則組，不從暫存路徑再次選組。各組篩選器排除前面已命中的組，維持由上至下第一個命中的語意。

只改內容規則時發布共用 JSON；已載入的物件讀取新規則，不覆寫 DLL。改組名稱、路徑、順序、啟用狀態或新增／刪除組時，管理器更新共用 JSON，僅為新組新增入口，再通知官方 WinMerge 重新載入外掛並重設自動選組；不覆寫或刪除已載入的 DLL，也不重開程序。已刪除的入口保留為停用，等 WinMerge 關閉後下次套用清理。若擴充功能等對話視窗開著，先關閉該視窗再按套用，不需關閉比對或主程式。

路徑語法限制與轉換見 README。測試預覽採用相同轉換條件；內容正則仍是 .NET。

## 驗證與診斷

GitHub Actions 下載官方 ZIP 並核對固定 SHA-256，驗證單檔、資料夾、多規則、可見差異及原檔 SHA-256 不變。原生 COM 驗證涵蓋暫存路徑、未儲存緩衝區、獨立規則組和熱更新；另以同一官方程序與視窗驗證新增、刪除、路徑、順序及啟用狀態變更。

設定 `WINMERGE_EDI_DIAGNOSTICS_PATH` 為可寫 JSONL 檔案路徑可啟用診斷。記錄處理路徑、入口選定的組 ID、`host-selected`／`disabled`、變動狀態和設定／輸出指紋，不記錄 EDI 內容。官方介面不提供原始路徑，因此 `originalPath` 為 null。

```powershell
dotnet run --project tests/WinMergeEdiPrediffer.Verification -c Release -- --com-payload work/verification-payload --winmerge-path C:\Tools\WinMerge\WinMergeU.exe --report artifacts/verification.json
```
