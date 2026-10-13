# WinMerge EDI 規則管理器

讓不同 EDI 使用不同的比對規則，例如忽略流水號。可建立多個「組」，每組放多條規則；只處理比對用的內容，不修改 EDI 原檔。

## 1. 第一次使用

電腦需有 **Windows x64、支援原始路徑選組的 WinMerge 2.16.58.2 修正版、.NET 10 Desktop Runtime x64 與 .NET 10 Runtime x64**。

官方原版 WinMerge 的單檔比對只傳入暫存路徑，無法保證依目錄選組。本版須搭配隨附修正版；原版執行中或指定的配套不符時，管理器會在套用前提示。建置與使用方式見 [WinMerge 修正版操作說明](docs/original-path-host.md)。

1. 拿到已建置程式時，請保留整個程式資料夾，包含 `Plugin` 子資料夾。
2. 雙擊 `WinMergeEdiPrediffer.Manager.exe`，按「開啟設定」載入交接的 `profiles.json`。
3. 第一次套用前關閉原版與修正版 WinMerge，按「套用到 WinMerge」，再開啟隨附的 WinMerge 修正版比對檔案。

若只有原始碼，先安裝 **.NET SDK 10.0.401**、**Visual Studio C++ x64 建置工具**（WinMerge 修正版另需 MFC 與 Windows SDK），在專案資料夾開啟 PowerShell 執行：

```powershell
dotnet build .\WinMergeEdiPrediffer.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw '建置失敗' }
Start-Process .\src\WinMergeEdiPrediffer.Manager\bin\Release\net10.0-windows\WinMergeEdiPrediffer.Manager.exe
```

交接程式時，複製整個 `src\WinMergeEdiPrediffer.Manager\bin\Release\net10.0-windows` 資料夾，另附 `profiles.json`。

## 2. 設定與套用

1. 上方按「新增組」，勾選「啟用」，填入組名稱、優先序與路徑正則。
2. 選定組後，下方按「新增規則」，填入「尋找」與「取代」。同組可重複新增，用「上移／下移」調整順序。
3. 使用範例正則時，規則也要勾選「啟用」與「正則」；普通文字替換則取消「正則」。需要不分大小寫時勾選「忽略大小寫」。
4. 按「測試預覽」（Ctrl+T），填入完整檔案路徑，再載入檔案，確認選到的組及替換結果。
5. 按「套用到 WinMerge」，查看狀態列與比對結果；需要時按 F5 重新比對。

**Ctrl+S 只儲存設定，還要按「套用到 WinMerge」才會生效。** 修改設定後，請重新開啟預覽。

## 3. 設定範例

「正則」是文字比對的寫法。以下可直接貼進管理器，再改成自己的檔名與資料夾。

### 建立三個組

各組勾選「啟用」；預設組可先不放規則。

| 組名稱 | 優先序 | 預設組 | 完整路徑＋檔名正則 |
| --- | --- | --- | --- |
| EDI 850 | 10 | 不勾 | `\\A_850\.edi$` |
| EDI 860 | 20 | 不勾 | `\\A_860\.edi$` |
| 其他檔案 | 999 | 勾選 | `.*` |

前兩組匹配任何資料夾下的指定檔名。若 850 只處理指定資料夾，改用：

```text
^D:\\ProjectGit\\Brad\\EDI\\(old|new)\\A_850\.edi$
```

這會匹配 `D:\ProjectGit\Brad\EDI\old\A_850.edi` 與 `new\A_850.edi`。路徑中的 `\` 要寫成 `\\`。

**優先序小的組先比對，預設組最後；每個檔案只用第一個符合的組。** 新組的路徑預設為 `.*`（任何檔案），請按需要修改。組內啟用的規則由上至下執行。

### 同組新增兩條規則

選擇 EDI 850 組，新增下面兩條規則，兩條都勾選「啟用」與「正則」。此範例用於以 `*` 分隔欄位、以 `~` 結束段落的 EDI。

```text
規則 1：忽略 ST02 流水號，保留 ST01 類型
尋找：(^|~)(ST\*[^*~\r\n]+\*)[^*~\r\n]*
取代：$1$2<IGNORE>

規則 2：忽略 SE02 流水號，保留 SE01 數量
尋找：(^|~)(SE\*[^*~\r\n]+\*)[^*~\r\n]*
取代：$1$2<IGNORE>
```

例如 `ST*850*0001~SE*16*0001~` 比對時會變成 `ST*850*<IGNORE>~SE*16*<IGNORE>~`。`$1$2` 代表保留找到的前段內容；原檔不變。規則逐行處理，取代內容不可加入換行。

完整 JSON 範例在 `examples\profiles.example.json`；日常設定使用管理器即可。

## 4. 常見問題與資料保留

- **改了沒生效：** 確認組與規則已啟用、按過「套用到 WinMerge」，再按 F5。其他比對分頁需切換後各按 F5；自動刷新支援 WinMerge 2.16.58.2。
- **要求關閉 WinMerge：** 升級 DLL 或修復安裝時需關閉一次；日常改組與規則不需重開，擴充功能視窗也可保持開啟。
- **外掛清單只有一個 EDI 外掛：** 正常，多個組都在管理器設定。
- **交接或搬動：** 保留 `profiles.json` 與整個程式資料夾；搬到新位置後重新套用。設定與安裝備份位於 `%APPDATA%\WinMergeEdiPrediffer\backups`，請保留。
- **停止使用：** 關閉管理器即可；已套用的規則仍會供 WinMerge 使用。
