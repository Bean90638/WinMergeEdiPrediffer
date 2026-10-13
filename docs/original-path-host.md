# WinMerge 修正版操作說明

本工具以 WinMerge **2.16.58.2** 的選用外掛屬性 `PluginOriginalFilePath` 修正來源路徑選組，保留單一 Router、C#/.NET 正則及 JSON 熱更新。官方原版 2.16.58.2 並未提供此屬性，單獨更新外掛不能完成修復。

## 使用已建置配套

1. 保留管理器整個資料夾、`Plugin` 子目錄及隨附的 `WinMerge` 資料夾。
2. 首次升級前先儲存未儲存的內容、關閉目前 WinMerge 與舊管理器；不要強制結束有未儲存內容的程序。
3. 使用管理器載入原本的 `profiles.json`，確認 850／860 組已啟用，按「套用到 WinMerge」。
4. 開啟隨附的 `WinMerge\WinMergeU.exe`；資料夾比對後再開啟對應單檔，檢查 ST02 是否都依同一組規則忽略。
5. 日常修改規則仍按「套用到 WinMerge」，作用中分頁刷新；其他分頁切換後按 F5。

配套與建置腳本不會自動取代 `C:\Program Files\WinMerge` 的官方安裝。若選擇更新既有安裝位置，須先備份原執行檔並取得 Windows 管理員權限。預設仍使用管理器原本的每使用者外掛目錄，所以原版與修正版不要同時使用這套 Router。管理器會在套用前檢查執行中的程序。

### 自訂修正版位置

管理器優先使用環境變數 `WINMERGE_EDI_WINMERGE_PATH`，其次使用管理器旁的 `WinMerge\WinMergeU.exe`，最後檢查官方安裝位置。自訂路徑必須是帶有 `EDI Original Path API 1` 版本標記的 x64 修正版。

```powershell
$env:WINMERGE_EDI_WINMERGE_PATH = 'D:\Tools\WinMergePathRouting\WinMergeU.exe'
Start-Process .\src\WinMergeEdiPrediffer.Manager\bin\Release\net10.0-windows\WinMergeEdiPrediffer.Manager.exe
```

## 開啟診斷

先建立可寫的記錄目錄，再於啟動 WinMerge 的同一個 PowerShell 設定：

```powershell
$env:WINMERGE_EDI_DIAGNOSTICS_PATH = 'D:\Logs\edi-routing.jsonl'
Start-Process $env:WINMERGE_EDI_WINMERGE_PATH
```

每次預差異處理記錄處理路徑、原始路徑、選定組 ID、原因、內容是否變動及設定 SHA-256 指紋。內容本身不記錄。未設定環境變數時不產生記錄；記錄目的地不能寫入時，比對仍照常執行。

- `selected`：已依原始路徑選組。
- `no-match`：已知來源沒有命中啟用中的組。
- `no-original-path`：host 明確表示這是沒有檔案來源的新文件，不套用預設組。
- `missing-original-path`：host 未提供新介面，需改用修正版。

關閉診斷：關閉 WinMerge，移除該環境變數，再重新啟動。診斷檔會持續附加；保留需要的證據後可以刪除或換檔。

## 從原始碼建置

需有 .NET SDK 10.0.401、Visual Studio 2022 C++ x64 工具、**C++ MFC x64** 及 **Windows 10/11 SDK**，首次取得詞法／語法產生器另需 **7-Zip**。只有 ATL 或本專案的小型 COM bridge 建置工具並不足以編譯完整 WinMerge。

```powershell
.\tools\Build-PatchedWinMerge.ps1 -FetchSource
dotnet build .\WinMergeEdiPrediffer.slnx -c Release
```

腳本只處理指定版本的工作區原始碼及輸出資料夾，不安裝 WinMerge、不修改登錄、不簽章或發布。第一次以 `-FetchSource` 取得官方原始碼與依賴；既有版本會檢查 commit 及修補狀態。

已有 SDK／MFC 的隔離解壓目錄時，可用 `-WindowsSdkDirectory` 指向包含 `Include`、`Lib`、`bin` 的 SDK 根目錄，`-MfcDirectory` 指向包含 `include`、`lib` 的 `atlmfc` 根目錄。腳本只暫時設定子程序建置環境，不修改系統環境變數。

輸出位於 `artifacts\WinMergePathRouting`，包含執行檔、建置記錄、上游 commit、SHA-256、GPL 授權與修補。WinMerge 的其他執行配套與資源應取自相同官方版本；原始碼在 `work\upstream\winmerge`，修補在 `patches\winmerge-v2.16.58.2-original-path.patch`。

## 驗證

```powershell
dotnet run --project .\tests\WinMergeEdiPrediffer.Verification -c Release -- --com-payload .\src\WinMergeEdiPrediffer.Plugin\bin\Release\net10.0-windows --winmerge-path .\artifacts\WinMergePathRouting\WinMergeU.exe --report .\artifacts\original-path-verification.json
```

驗證會建立隔離資料，執行原生 COM 測試，並以修正版 WinMerge 的非互動模式產生單檔／資料夾報告及路徑診斷。不改動目前使用者的設定或 EDI。測試證據位於 `work\winmerge-integration-*`。

## 停止、保留與排錯

- 停止使用：正常儲存並關閉 WinMerge 及管理器即可。
- 保留 `profiles.json`、管理器完整資料夾與 `%APPDATA%\WinMergeEdiPrediffer\backups`；不要把工作設定或升級備份當作建置暫存刪除。
- 顯示不支援原始路徑：確認開啟的是隨附修正版，關閉仍在執行的官方原版；不要將路徑改成 `.*` 掩蓋問題。
- 缺少 `afxwin.h`：需要 MFC，ATL 元件不能替代。
- 缺少 `Windows.h`、`kernel32.lib` 或 SDK 建置工具：需補齊完整 x64 SDK 配套。
- 內容仍有差異：查看診斷的原始路徑與組 ID，確認該組及規則已啟用並已套用。850 範例只忽略 ST02；860 另外設定 SE02 才會忽略 SE02。


## 2026-10-13 驗證結果

- C# 管理器、外掛及驗證程式 Release 建置成功，0 警告、0 錯誤。
- WinMerge 2.16.58.2 x64 修正版已從指定官方 commit 建置成功；上游程式仍有編譯器警告，保留完整建置記錄。
- 共 113 項檢查通過，包含原生 COM 與真正 WinMerge 執行檔的 850／860 單檔及資料夾比對；實際 EDI 檔案從 D:\ProjectGit\Brad\EDI 唯讀複製到隔離目錄驗證。ST02（860 另含 SE02）被忽略，ST01 控制案例仍回報不同。
- 真正單檔呼叫的處理路徑為 `t0_wmdoc`／`t1_wmdoc` 暫存檔，原始路徑仍分別為 `new\850`、`old\850`、`new\860`、`old\860` 下的檔案；原始 SHA-256 不變。
- 未儲存內容與同一物件的 JSON 熱更新由原生 COM 測試覆蓋；本次未在使用者正在操作的視窗執行編輯或 F5。
- 修正版配套位於 `artifacts\WinMergeEdiPrediffer-fixed`，壓縮檔為 `artifacts\WinMergeEdiPrediffer-fixed.zip`。本機已在備份後更新既有 WinMerge 安裝位置及每使用者 EDI 外掛，原本捷徑可繼續使用。

- 安裝後另以實際使用者設定驗證自動選外掛，不指定預差異器：850 單檔、860 單檔與資料夾比對均回報相同，ST01 控制案例回報不同，原始 EDI SHA-256 保持不變。安裝與驗證記錄位於 `artifacts\installed-version-report.json` 及 `artifacts\installed-version-verification.json`。

另可在驗證命令加上 `--edi-fixtures D:\ProjectGit\Brad\EDI`，使用 `new/old` 下的 `850/A_850.edi` 與 `860/A_860.edi` 真實檔案副本，並檢查來源檔案 SHA-256 保持不變。
