# WinMerge 原始路徑選組問題與修正建議

確認日期：2026-10-13。

本文件記錄前次診斷與已採用的修正設計；實作、建置及操作方式請見 [WinMerge 修正版操作說明](original-path-host.md)。

## 結論

`\\850\\.*\.edi$` 能匹配位於 `850` 目錄及其子目錄、以 `.edi` 結尾的路徑。問題在於目前 Router 把 `PluginVariables` 當作原始路徑，而 WinMerge 單檔比對傳入的是編輯器內容的暫存檔路徑。

若要保留 `docs/requirements.md` 的「單一固定外掛、.NET 路徑正則、組別與路徑設定都只發布 JSON」需求，建議擴充 WinMerge，另外提供每側的原始路徑。這需要 WinMerge 與本外掛兩邊配合，不能只修本專案就宣稱完成。

若使用官方未修改的 WinMerge 是必要限制，可以採每組獨立外掛入口，但須明確接受正則相容性限制及選組設定變動時重新載入外掛。

## 1. 目前流程

資料夾內容比對（沒有先經過 Unpacker 轉換）：

`原始檔案 → PluginVariables = 檔案路徑 → Router 用 .NET 正則選組`

單檔比對（包含首次開啟與編輯後重新比對）：

`原始檔案／編輯器內容 → t0_wmdoc、t1_wmdoc 等暫存檔 → PluginVariables = 暫存路徑 → Router 選組`

單檔流程中，WinMerge 自動選外掛仍使用另一份原始檔名集合；但這份資訊沒有經由 `PluginVariables` 傳給 Router。

目前 `WinMergeScript.cs:105` 只檢查 `Path.IsPathFullyQualified`。暫存檔也有完整絕對路徑，因此檢查會通過，接著選不到專屬組、落到預設組或不處理。檢查「完整路徑」無法證明它是「原始路徑」。

資料夾流程也有例外：若先經過 Unpacker，`Filepath_Transform` 收到的可能已是解包暫存路徑。擴充原始路徑時必須保留解包前的來源，而不是沿用處理檔案的路徑。

## 官方原始碼證據

主要核對版本為本機安裝的 WinMerge **2.16.58.2**。

| 位置 | 已確認的行為 |
| --- | --- |
| [MergeDoc.cpp:384-415](https://github.com/WinMerge/winmerge/blob/v2.16.58.2/Src/MergeDoc.cpp#L384-L415) | 建立各側暫存檔，`SetPaths(..., true)` 傳給比對引擎；同時另外呼叫 `SetCompareFiles(m_filePaths)` 保留原始路徑。 |
| [DiffWrapper.cpp:788](https://github.com/WinMerge/winmerge/blob/v2.16.58.2/Src/DiffWrapper.cpp#L788) | `Prediffing(..., { strFileTemp[file] })` 把暫存路徑放進 variables。 |
| [FileTransform.cpp:799-802](https://github.com/WinMerge/winmerge/blob/v2.16.58.2/Src/FileTransform.cpp#L799-L802) | 將 `variables[0]` 原樣寫入外掛的 `PluginVariables`。 |
| [FullQuickCompare.cpp:85-109](https://github.com/WinMerge/winmerge/blob/v2.16.58.2/Src/CompareEngines/FullQuickCompare.cpp#L85-L109) | 資料夾流程保留 `tFiles`，但解包可能改變 `filepathUnpacked`，後者再傳入預差異處理。 |
| [DiffFileData.cpp:128-140](https://github.com/WinMerge/winmerge/blob/v2.16.58.2/Src/DiffFileData.cpp#L128-L140) | 使用呼叫端提供的 filepath 作為 variables，沒有額外保證其為解包前原始路徑。 |
| [FileTransform.cpp:691-733](https://github.com/WinMerge/winmerge/blob/v2.16.58.2/Src/FileTransform.cpp#L691-L733) | 自動選外掛依各側檔名分別選擇；選到不同外掛時以 targetFlags 限定各側，並非只能整組比較共用同一外掛。 |
| [Plugins.cpp:245-299](https://github.com/WinMerge/winmerge/blob/v2.16.58.2/Src/Plugins.cpp#L245-L299) | 檔案篩選以分號切開，且對正則與輸入轉大寫；與目前 .NET 路徑正則的使用方式不同。 |
| [Plugins.cpp:1058-1068](https://github.com/WinMerge/winmerge/blob/v2.16.58.2/Src/Plugins.cpp#L1058-L1068) | 自動選擇採第一個符合的外掛，本專案的組優先序並不會自動成為 WinMerge 的優先序。 |
| [MergeAppLib.idl:13-21](https://github.com/WinMerge/winmerge/blob/v2.16.58.2/Src/MergeAppLib.idl#L13-L21) | `PluginOnEvent` 可取得的主機介面包含設定、翻譯、對話框與記錄，沒有各側原始檔案路徑 API。 |

另外查閱 2026-10-13 取得的 upstream `master`：雖然比對呼叫已改用 `PluginPipelineContext`，仍設定 `pipelineContext.variables = { strFileTemp[file] }`，再將 `variables[0]` 寫入 `PluginVariables`；沒有從這個介面取得原始路徑的新行為。這是原始碼快照查核，未宣稱已實測該版本執行檔。

## 2. 建議調整後流程

`各側原始路徑 + 各側當下的編輯器內容 → WinMerge 額外傳入原始路徑 → Router 用現有 .NET 正則選組 → 執行該組最新 JSON 規則`

### WinMerge 端

1. 新增選用外掛屬性，例如 `PluginOriginalFilePath`。這是建議新增的名稱，並非現有官方 API。
2. 在載入時偵測外掛是否宣告該屬性；只有支援它的外掛才接收新資料。
3. 單檔比對使用 `CDiffWrapper::m_originalFile[file]`；資料夾比對使用解包前的 `tFiles[nIndex]`，把來源路徑明確傳遞到 `Prediffing`。
4. 每次執行預差異處理前，在現有外掛呼叫鎖內傳入該側路徑；左右／三側不能共用上一側的路徑狀態。
5. 保留既有 `PluginVariables`、內容來源及檔案處理語意，維持其他外掛相容性。

### 本專案端

1. `WinMergeScript.cs` 與 `tools/TypeLibraryBuilder/Program.cs` 宣告新屬性；Router 只以這份原始路徑選組。
2. 原始路徑狀態每次呼叫消耗並清除，包含 setter 失敗與處理失敗，避免沿用上一個檔案的組。
3. 管理器及文件明確說明支援的 WinMerge 條件。舊 host 沒有提供新屬性時，必須明確回報不支援原始路徑選組，不能把暫存路徑當成已成功取得來源。
4. 診斷記錄包含收到的處理路徑、原始路徑、選定組 ID、未命中／來源缺失原因及設定版本；不記錄 EDI 內容。記錄方式應可開關，失敗也不應影響比對。
5. 維持 `RuleEngine` 的啟用狀態、預設組最後、優先序、同優先序依設定順序，以及 JSON 熱更新。

## 只改外掛的替代方案

`WinMerge 用原始檔名選各組入口 → 該入口依組 ID 讀取 JSON → 共用現有 C# 內容規則引擎`

這個方向可避開 Router 依暫存路徑選組；官方原始碼也證實左右側能各自選不同入口。但它無法直接完整保留目前需求：

- 路徑匹配改交給 WinMerge 篩選機制，不能保證任意 .NET 正則等價。尤其目前已測試支援的分號、內嵌大小寫選項等，不可直接原樣當作外掛篩選字串。
- 優先序和預設組需產生互斥篩選（後組排除前組），不能只靠 DLL 名稱或載入順序。現有 `EffectiveFilter` 只能當舊方案參考，仍需檢查 WinMerge 正則相容性。
- 內容規則可以按組 ID 熱更新；新增／刪除組、路徑、優先序、啟用狀態則會改變入口或篩選，必須有重載與已開啟比較 pipeline 的重新選擇流程。F5 不能一概視為重新載入全部外掛資訊。
- 必須改寫「固定單一 host、所有選組設定只發布 JSON」的驗收條件，並驗證重載所需的操作。

因此，若既有需求都要保留，優先採額外原始路徑介面；若官方原版 WinMerge 是不可變更的限制，才採替代方案並明確縮限需求。

## 本次驗證與限制

- 專案 Release 建置成功：0 警告、0 錯誤。
- 現有驗證程式包含原生 COM 測試，共 **71 項通過**。
- 在工作區的隔離部署中，以同一原生 COM 物件、WinMerge 的 BYREF VARIANT 形式，針對原樣正則 `\\850\\.*\.edi$` 重現：

| 傳給 PluginVariables 的路徑 | 選組 | 內容結果 |
| --- | --- | --- |
| `D:\Example\850\sample.edi` | 850 | `ST*850*<IGNORE>~` |
| `D:\Example\850\Testing\sample.edi` | 850 | `ST*850*<IGNORE>~` |
| 模擬 `...\WinMerge\t0_wmdoc123.tmp` | 預設 | `ST*850*0001~`，未替換 |

證據檔為 `work/path-routing-audit/baseline-report.json` 與 `reproduction-report.json`；`work/` 已被 Git 忽略。

這次完成官方原始碼追蹤與原生 COM 重現，未操作正在使用的 WinMerge 視窗，也未建置或部署 WinMerge 修正版。此段描述前次診斷的驗證範圍；後續實作結果另記錄於操作說明。

### 實作後必要驗收

- 單檔首次開啟、資料夾比對、由資料夾開啟檔案及 F5 使用相同來源路徑選組。
- 同名不同目錄、左右不同組、三側不同組、重疊優先序及預設組都正確。
- 編輯尚未儲存的內容仍參與比對；路徑選組使用原始來源，不能為了拿路徑而重新讀取原檔內容。
- 解包前原始路徑、同步點重新比對、缺失來源與未儲存新檔有明確處理。
- 路徑正則、優先序、組啟用、新增／刪除組及內容規則皆可在同程序中套用並驗證實際差異變化。
- 忽略 ST02 且保留 ST01；原檔內容、SHA-256、行數與 CR/LF 保持不變。
- 新屬性不影響既有第三方外掛；舊 host 不誤報已支援。
