# WinMerge EDI 預差異化規則管理器

C# WinForms 與 x64 COM DLL。所有 EDI 共用一個固定外掛，在 C# 內依完整路徑與檔名選組；每組可新增多條正則／文字取代規則。預差異化只改比對用的內容，不寫回原始 EDI。

## 開啟與建置

專案：D:\ProjectGit\Brad\WinMergeEdiPrediffer

開發需要 Windows x64、.NET SDK 10.0.401、Visual Studio MSVC x64 C++ 工具及 64 位元 WinMerge。C++ 只建置原生 COM factory 轉接 DLL；引擎、COM 物件、管理器均使用 C#。不需 MIDL、Windows SDK 或 regsvr32。執行管理器需要 .NET 10 Desktop Runtime x64，COM 外掛需要 .NET 10 Runtime x64。

~~~powershell
Set-Location 'D:\ProjectGit\Brad\WinMergeEdiPrediffer'
dotnet build .\WinMergeEdiPrediffer.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw '建置失敗' }
Start-Process .\src\WinMergeEdiPrediffer.Manager\bin\Release\net10.0-windows\WinMergeEdiPrediffer.Manager.exe
~~~

也可直接開啟上述 Manager.exe。無環境變數或常駐服務。關閉管理器即停止編輯工具，載入中的外掛由 WinMerge 結束時釋放。

## 日常操作

1. 上方新增或選擇組，設定名稱、啟用、優先序、預設組與完整路徑正則。
2. 選定組後，在下方按「新增規則」。設定尋找、取代、啟用、正則與大小寫；用上移／下移調整順序。底部大輸入區可編輯尋找與取代。
3. 按「測試預覽」或 Ctrl+T，輸入完整路徑及內容。可載入檔案並選擇編碼，查看命中組、實際選組與逐條規則結果。
4. 按「套用到 WinMerge」，先儲存並驗證，再發布 JSON，要求刷新目前比對分頁。

新增／刪除組、正則、路徑、優先序或啟用狀態都只更新 JSON，不修改 DLL 或重新讀取外掛。日常套用不跳完成視窗，結果顯示在狀態列。

首次從舊多外掛架構升級、更新 COM 程式或修復配套檔時，才需關閉 WinMerge 後套用一次。只按 Ctrl+S 儲存原始 JSON 不會發布，請按「套用到 WinMerge」。

自動刷新命令限定 WinMerge 2.16.58.2。日常套用可保留「擴充功能」視窗，不重新載入它仍持有的外掛物件。通知是非同步的，狀態列表示已送出刷新要求，完成應以比對結果確認。其他版本提示手動 F5。多分頁時只刷新目前作用中的分頁，其他分頁切換後按 F5。

預覽使用開啟時的設定快照，修改後重新開啟預覽。「開啟」與「另存新檔」可載入或匯出 JSON。

## 如何選組

- 新組路徑正則預設 .*，匹配任何檔案。
- 啟用的專屬組按優先序由小到大檢查，同優先序按表格順序。
- 每個檔案只用第一個命中組，該組所有啟用規則由上至下執行，不疊加其他組。
- 最多啟用一個預設組，路徑必須為 .*，固定最後檢查。
- 未命中或沒有啟用規則時，內容保持原樣。

專屬組若保留 .*，可能先攔到其他 EDI；請設定專用路徑或適當優先序。

## 路徑與內容正則

兩者皆使用 C#/.NET Regex。路徑匹配完整路徑與檔名，預設忽略大小寫；內容正則逐行處理，大小寫由規則設定。

路徑支援 \d、\w、\s、群組、反向參照、(?i) 與分號，不會轉大寫或交由 WinMerge 分割。每次匹配有 2 秒逾時限制。

管理器路徑欄位範例：

~~~text
^D:\\ProjectGit\\Brad\\EDI\\(old|new)\\A_850\.edi$
~~~

這會匹配 old 與 new 中的 A_850.edi。同一檔名可依不同目錄分組。管理器輸入正則本身，JSON 額外跳脫由程式處理；手動編輯 JSON 時每個反斜線需再跳脫一次。

內容保留 CR、LF、CRLF，不能跨行比對、搬移行、增刪換行。產生換行會回報錯誤。正則取代可使用 $1、$2 與具名群組；取消正則時視為普通文字，$1 不展開。

預設 EDI 規則只忽略 ST02，保留 ST01：

~~~text
尋找：(^|~)(ST\*[^*~\r\n]+\*)[^*~\r\n]*
取代：$1$2<IGNORE>
~~~

ST*850*0001~ 變為 ST*850*<IGNORE>~。SE02 不被這條規則忽略，需另增規則並確認預覽。

## 固定外掛與熱更新

WinMerge 清單固定只有一個 EDI 預差異器，所有組與規則在規則管理器內維護。

~~~text
%APPDATA%\WinMerge\MergePlugins\
  EdiPrediff.install.json
  EdiPrediff.rules.json
  EdiPrediff_Router.comhost.dll
  EdiPrediff_Router\
    WinMergeEdiPrediffer.Plugin.comhost.dll
    WinMergeEdiPrediffer.Plugin.dll
    WinMergeEdiPrediffer.Plugin.runtimeconfig.json
    WinMergeEdiPrediffer.Plugin.deps.json
    WinMergeEdiPrediffer.Plugin.profile.json
~~~

所有組共用同一部署。WinMerge 掃描最上層 DLL，因此 managed DLL 必須留在子目錄。保留完整配套檔，不能只複製單一 DLL。

原生轉接 DLL 將 factory 轉給子目錄內的標準 .NET COM host；兩個入口嵌入型別資訊，C# 提供 IDispatch.GetTypeInfo，不需 COM 註冊。

PrediffBufferW 只有內容、大小與變更旗標；WinMerge 在呼叫前透過 PluginVariables 另外提供路徑，C# 依此選組。路徑只用於當次呼叫，缺少完整路徑會回報錯誤，避免沿用上一檔案的组。一般文字 EDI 流程收到原始完整路徑；同時使用解包裝器或前置外掛時可能是暫存路徑，需確認該流程。

固定 Schema 3 profile 指向共用規則。LiveProfileReader 每次 COM 呼叫讀取 EdiPrediff.rules.json，以文字判斷更新，變動才解析及驗證，時間戳相同也可偵測。發布為原子置換，讀取允許檔案被置換。無效／缺少規則會回報 COM 錯誤，不默默忽略差異。

固定名稱、.* 篩選器及自動旗標不隨組變動。日常只發布 JSON 與安裝清單，再要求刷新；不重新載入 DLL 或重設自動選組。刪除組不殘留每組 DLL，全部組停用時內容原樣返回。

升級先備份並清除舊清單內的入口與配套檔，保留其他外掛。安裝啟用自動預差異化，停用內建 PrediffLineFilter 的自動選用以免先攔 EDI，仍可手動選它；相關 WinMerge 設定先備份。

## 資料、備份及停止

| 位置 | 用途 |
| --- | --- |
| 專案 profiles.json | 使用者目前規則，可加入 Git 管理 |
| examples\profiles.example.json | 初始範例 |
| %APPDATA%\WinMergeEdiPrediffer\backups | 規則、外掛、WinMerge 設定備份 |
| MergePlugins\EdiPrediff.install.json | 自有檔案清單 |
| MergePlugins\EdiPrediff.rules.json | 最後一次套用的完整規則 |
| Manager\bin\Release\net10.0-windows | 管理器、相依檔及外掛載荷 |

保留 profiles.json、備份、安裝清單及外掛配套檔。管理器路徑記錄於已套用規則，供外掛設定按鈕使用；搬動專案後重新套用。匯出其他位置使用「匯出 COM 外掛」。關閉管理器即可停止，沒有背景服務。

## 驗證及排除問題

~~~powershell
Set-Location 'D:\ProjectGit\Brad\WinMergeEdiPrediffer'
dotnet build .\WinMergeEdiPrediffer.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw '建置失敗' }
& .\tests\WinMergeEdiPrediffer.Verification\bin\Release\net10.0-windows\WinMergeEdiPrediffer.Verification.exe --com-payload .\src\WinMergeEdiPrediffer.Plugin\bin\Release\net10.0-windows
if ($LASTEXITCODE -ne 0) { throw '驗證失敗，查看完整例外' }
~~~

71 項檢查涵蓋原生 factory／IDispatch／BYREF VARIANT、路徑 setter、同物件多組路由、規則順序、逐行處理、JSON、備份與舊部署遷移。鎖住 DLL、配套與固定 profile 後，驗證增刪組、停用、路徑及優先序更新均不改固定檔案；另驗證錯誤回報、復原及升級限制。模擬修復途中無法發布安裝清單，確認原有檔案回復且原先遺失的配套檔不會殘留。WinMerge 的實際刷新須另以介面結果驗證。

| 現象 | 處理 |
| --- | --- |
| 改規則沒生效 | 按套用、查看狀態列及結果；其他分頁切換後 F5，確認選用 Router 或自動預差異器 |
| 版本需手動刷新 | 按 F5，自動通知限定 2.16.58.2 |
| 要求關閉 WinMerge | COM 程式升級／修復，關閉後套用一次，日常修改不需 |
| 清單只有一個 EDI 外掛 | 正常行為，組與規則在管理器維護 |
| 無法覆寫 DLL 或建置 EXE | 關閉 WinMerge 或管理器後再升級／建置 |
| 外掛未出現 | 確認 x64 WinMerge、.NET 10 x64 Runtime 及完整部署 |
| WinMergeScript entry point not found | 重新套用，最上層只留 comhost.dll，managed DLL 在子目錄 |
| 缺少 MSVC | 需要 Visual Studio C++ x64 工具；非預設位置可設定 WINMERGE_EDI_MSVC_PATH 為 Hostx64\x64 工具目錄 |
| 路徑不符 | 預覽完整路徑，檢查反斜線、優先序及 .*；解包裝時確認路徑來源 |
| 設定無法儲存 | 查看錯誤指出的組／規則位置，未完成規則可先停用 |
| 0xe0434352 | .NET 例外代碼，從 PowerShell 執行驗證取得完整例外，不能只由代碼推斷原因 |

## 程式結構

| 目錄 | 責任 |
| --- | --- |
| src/WinMergeEdiPrediffer.Core | 設定、驗證、路徑選組、逐行引擎、部署與備份 |
| src/WinMergeEdiPrediffer.Manager | WinForms 編輯／預覽／套用、設定與刷新 |
| src/WinMergeEdiPrediffer.Plugin | C# COM、IDispatch、PluginVariables |
| tools/TypeLibraryBuilder | OLE 型別資訊與小型原生轉接 DLL |
| tests/WinMergeEdiPrediffer.Verification | 規則與原生 COM 驗證 |

參考：[WinMerge 文件](https://manual.winmerge.org/en/Plugins.html)、[PluginVariables 與預差異化流程](https://github.com/WinMerge/winmerge/blob/v2.16.58.2/Src/FileTransform.cpp)、[外掛屬性呼叫](https://github.com/WinMerge/winmerge/blob/v2.16.58.2/Src/Plugins.cpp)、[.NET COM hosting](https://learn.microsoft.com/en-us/dotnet/core/native-interop/expose-components-to-com)、[COM 屬性命名](https://learn.microsoft.com/en-us/windows/win32/api/oaidl/nf-oaidl-icreatetypeinfo-setfuncandparamnames)。
