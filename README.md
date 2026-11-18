# MusicStaffViewer 五線譜檢視器

Windows 桌面 MusicXML 五線譜檢視器，採用 .NET 10、WinUI 3、AlphaTab .NET 與 AlphaSkia。樂譜在本機以 C# 原生處理與繪製，不使用 WebView2 或 JavaScript，適合在練習、教學與排練時快速閱讀 MusicXML 樂譜。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/mark216tw/win-musicstaff-01)](https://github.com/mark216tw/win-musicstaff-01/releases/latest)

## 功能特色

- 以「檔案」選單開啟 `.musicxml`、`.xml`、`.mxl` 或拖放檔案；主畫面側欄顯示最愛樂譜並依名稱排序。
- 以 lazy partial 渲染與虛擬化清單顯示大型總譜；僅繪製可見的系統並預先載入下一個系統。樂譜頂端顯示原譜標題與作曲家資訊，但不列入系統數與導覽。
- 可將目前樂譜的所有系統排版成多頁 PDF，紙張可選 A4、A3 或 Letter，系統會保留原始相對寬度；第一頁會包含譜面標題區塊。
- 固定 1600 DIP 版面，支援 20%–400% 縮放（每次 5%）、Ctrl+滾輪縮放、符合視窗寬度、四周留白、重新載入及按住左鍵上下左右拖曳；縮放時樂譜保持畫面置中，高倍縮放時會自動提高可見系統的繪製解析度。
- 樂譜顯示品質可選標準／高／最高；PDF 品質可選 2×／3×／4×，匯出解析度不受目前縮放影響。
- 全螢幕專注模式（F11）會隱藏選單、側欄與工具列。
- 繁體中文介面；跟隨系統／淺色／深色顯示模式，樂譜顏色同步跟隨；黃色護眼、經典藍、森林綠、暮色紫、中性灰五款主題。
- 樂譜顯示比例預設符合畫面寬度，也可改用自訂縮放；每行可選擇 4–10 小節，音符間距可選緊湊／標準／寬鬆。
- 偏好設定採緊湊表單，按「套用」後才保存；最近檔案和視窗位置自動保存。
- 最愛樂譜清單可收合成窄邊條，可標示目前樂譜並以右鍵移除單筆；開啟的樂譜可用工具列星號加入或移出最愛。重排或重新載入會保留捲動位置。
- 可切換顯示的聲部（至少保留一個），並依 MusicXML 段落／排練記號提供目錄；`Ctrl+G` 可前往指定小節，`Ctrl+PageUp`／`Ctrl+PageDown` 可上／下一個系統。
- 每個樂譜會記住縮放、聲部選擇與閱讀位置，重新開啟時自動續讀；狀態列顯示目前系統與小節範圍。
- 單一實例、拖放檔案、載入／空白／錯誤狀態及本機檔案日誌；第二次啟動可帶入樂譜檔案。

## 下載與安裝

請至 [Releases](https://github.com/mark216tw/win-musicstaff-01/releases/latest) 下載對應架構的 zip：

| 檔案 | 適用裝置 |
| --- | --- |
| `MusicStaffViewer-<版本>-win-x64.zip` | Intel／AMD x64 電腦 |
| `MusicStaffViewer-<版本>-win-arm64.zip` | Windows on Arm（ARM64）裝置 |

本專案**不提供安裝程式**。解壓縮後直接執行 `MusicStaffViewer.exe` 即可，程式不會寫入系統目錄或登錄檔。

**系統需求**

- Windows 10 1809（build 17763）以上；建議 Windows 11。
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（x64 或 ARM64 版）。
- Windows App SDK 執行元件已包含在發佈檔案中，不需另外安裝。
- 發佈檔為非 Self-Contained（framework-dependent）版本，因此必須先安裝 .NET 10 Desktop Runtime。

> 想從原始碼自行建置或打包，請參閱 [docs/BUILD_AND_RELEASE.md](docs/BUILD_AND_RELEASE.md)。

## 使用方式

1. 啟動 `MusicStaffViewer.exe`。
2. 按 `Ctrl+O` 或由「檔案 → 開啟 MusicXML」選擇樂譜，也可直接把檔案拖進視窗。
3. 使用工具列調整縮放、每行小節數、聲部與目錄；`F11` 進入全螢幕專注模式。
4. 由「檔案 → 匯出 PDF」輸出目前樂譜。

完整操作說明與疑難排解請參閱 [docs/USER_GUIDE.md](docs/USER_GUIDE.md)。

### 快捷鍵

| 快捷鍵 | 功能 |
| --- | --- |
| Ctrl+O | 開啟 MusicXML |
| Ctrl+, | 偏好設定 |
| Ctrl+=／Ctrl+數字鍵盤 + | 放大 |
| Ctrl+-／Ctrl+數字鍵盤 - | 縮小 |
| Ctrl+0 | 100% 縮放 |
| Ctrl+G | 前往指定小節 |
| Ctrl+PageUp／Ctrl+PageDown | 上／下一個系統 |
| 滑鼠滾輪 | 上下移動樂譜 |
| Ctrl+滑鼠滾輪 | 以畫面中心縮放 |
| 按住左鍵拖曳 | 上下左右移動樂譜 |
| F5 | 重新載入 |
| F11 | 切換全螢幕 |
| Esc | 離開全螢幕 |

## 從原始碼建置

需要 Windows 10 1809 以上、.NET 10 SDK。開發與測試使用 `Debug`：

```powershell
dotnet build MusicStaffViewer.sln -c Debug -p:Platform=x64
dotnet test MusicStaffViewer.sln -c Debug -p:Platform=x64
dotnet run --project src/MusicStaffViewer/MusicStaffViewer.csproj -c Debug -p:Platform=x64
```

應用程式為未封裝桌面程式；執行檔位於 `src/MusicStaffViewer/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/`。

設定檔：`%LocalAppData%\MusicStaffViewer\settings.json`；日誌：`%LocalAppData%\MusicStaffViewer\logs\`。

## 專案文件

| 文件 | 內容 |
| --- | --- |
| [docs/USER_GUIDE.md](docs/USER_GUIDE.md) | 使用指南、操作流程與疑難排解 |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | 系統架構、技術選型與渲染管線 |
| [docs/DESIGN.md](docs/DESIGN.md) | 系統設計、模組職責、資料模型與狀態流程 |
| [docs/BUILD_AND_RELEASE.md](docs/BUILD_AND_RELEASE.md) | 建置、測試、發佈與 GitHub Release 流程 |
| [docs/DEV_LOG.md](docs/DEV_LOG.md) | 開發歷程紀錄 |
| [CHANGELOG.md](CHANGELOG.md) | 版本變更紀錄 |
| [RELEASE_NOTES.md](RELEASE_NOTES.md) | 給使用者的版本說明 |
| [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) | 第三方元件授權 |

## 已知限制

- AlphaTab 依視窗設定的固定版面寬度自動排系統，不保證重現 MusicXML 原始印刷分頁。
- PDF 仍以點陣系統影像輸出（最高 4×），尚未改為向量繪製；紙張邊界固定為 40 pt。PDF 第一頁包含譜面標題區塊，後續頁面只放實際樂譜系統；標題與作曲家也會寫入 PDF 中繼資料。
- 小節跳轉以 1–N 的小節序號為準，不採用 MusicXML 的自訂小節文字；目錄只顯示樂譜中實際存在的段落／排練記號。
- 切換聲部需要重新排版，大型總譜可能需要數秒。
- 目前未提供 MIDI 播放、樂譜編輯或列印；沒有 MSIX 安裝檔。
- 尚未在 Windows 10 1809 實機測試；開發與驗證環境為 Windows 11。ARM64 版本已完成建置與靜態檢查，尚待 ARM64 實機驗證。

## 授權

本專案採用 [MIT License](LICENSE)，著作權人為 mark216tw。

第三方元件授權（AlphaTab、AlphaSkia、Bravura、SkiaSharp、Windows App SDK 等）請參閱 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
