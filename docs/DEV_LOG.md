# 開發紀錄

最新紀錄放最上面。每次開發工作都要更新，不要事後補寫。

---

## 2026-11-18 - v1.15.0

### 目標
補齊專案文件與 MIT License，將專案提交到 GitHub 公開儲存庫，並發佈 x64／ARM64 非 Self-Contained zip（不提供安裝檔）。

### 變更
- 新增 `LICENSE`（MIT，著作權人 mark216tw）。
- 改寫 `README.md`：補上下載方式、系統需求、文件導覽、授權與 ARM64 說明。
- 新增 `docs/USER_GUIDE.md`、`docs/ARCHITECTURE.md`、`docs/DESIGN.md`、`docs/BUILD_AND_RELEASE.md`、`docs/README.md`。
- 更新 `CHANGELOG.md`、`RELEASE_NOTES.md`、`THIRD-PARTY-NOTICES.md` 與本紀錄。
- `MusicStaffViewer.csproj`：新增 `ARM64` 平台與 `win-arm64` RuntimeIdentifier，`PlatformTarget` 依平台決定；預設仍為 x64。
- `Directory.Packages.props`／`MusicStaffViewer.Rendering.csproj`：`AlphaSkia`／`AlphaSkia.Native.Windows` 升級至 4.0.157，以取得 `win-arm64` 原生程式庫。
- 版本提升為 1.15.0，`AssemblyVersion`／`FileVersion` 同步為 1.15.0.0。

### 問題與解法
- AlphaSkia.Native.Windows 3.5.147 只有 win-x64／win-x86／win-arm，ARM64 發佈輸出缺少 `libalphaskia.dll`，樂譜繪製會失敗 → 檢查 NuGet 後發現 4.0.157 已提供 `win-arm64`，升級套件並確認渲染測試全數通過。
- 直接升級 AlphaSkia 可能與 AlphaTab 1.9.0 的橋接不相容 → 保留 AlphaTab 1.9.0，僅升級 AlphaSkia，並以 `MusicStaffViewer.Rendering.Tests` 的實際排版與 PDF 測試驗證。
- 發佈需求為非 Self-Contained，但未封裝 WinUI 需要 Windows App SDK 執行元件 → 採 `SelfContained=false`、`WindowsAppSDKSelfContained=true`，使用者只需安裝 .NET 10 Desktop Runtime。
- ARM64 無法在目前的 x64 開發機執行 → 以 PE header 檢查 `MusicStaffViewer.exe`、`libalphaskia.dll`、`libSkiaSharp.dll`、Bootstrap DLL 均為 ARM64。

### 待辦／已知限制
- 尚未在 ARM64 實機啟動與實際開啟樂譜驗證。
- 尚未在 Windows 10 1809 實機驗證。
- PDF 仍為點陣輸出，未改為向量繪製。

---

## 2026-11-18 - v1.14.2

### 目標
修正最愛清單目前樂譜左側同時出現兩條線，並讓 App 圖示的音符更顯眼。

### 變更
- `ScorePage.xaml`：在最愛清單覆寫 `ListViewItemSelectionIndicatorVisualEnabled=False`，關閉系統選取指示條，只保留目前樂譜的色條。
- `Assets/app.ico`：重新繪製多尺寸圖示，音符放大並置中，加上陰影與深色描邊，同時降低五線譜線條的存在感。
- 版本提升為 1.14.2；`AssemblyVersion`／`FileVersion` 不變。

### 問題與解法
- 1.14.1 保留選取後，系統選取指示條與 `IsCurrent` 色條會同時出現在同一個項目 → 以主題資源覆寫關閉系統指示條，選取底色與鍵盤焦點框仍保留。
- 原圖示的音符僅約 44% 高度且偏左，小尺寸不易辨識 → 改以向量繪製，音符約占 60% 高度並置中，加上陰影／描邊後在 16px 仍可看出音符輪廓。
- 圖示以 16～64 的 BMP 幀與 128／256 的 PNG 幀組成，維持與原圖示相同的組態。

### 待辦／已知限制
- 尚未在 Windows 10 1809 以工作列、開始功能表實際檢視新圖示各尺寸的呈現。
- 尚未驗證不同 shell 縮放比例下的圖示選取結果。

---

## 2026-11-18 - v1.14.1

### 目標
修正最愛樂譜清單以鍵盤上下鍵切換時，焦點框一直跳回第一筆的問題。

### 變更
- `ScoreViewModel.RefreshFavoritesAsync`：清單結構（路徑、標題、作曲家）相同時，只就地更新 `IsCurrent` 有變化的項目，不再 `Clear` 後重新加入。
- `ScoreViewModel`：新增 `CurrentPath` 唯讀屬性，以及 `FavoritesChanging`／`FavoritesChanged` 事件，讓檢視層能在最愛清單更新期間忽略隨之而來的選取變更。
- `ScorePage.OnFavoriteSelected`：開啟後保留選取在剛開啟的最愛項目；鍵盤操作時將焦點還原到該項目；忽略目前樂譜與清單重新整理造成的選取事件。
- 版本提升為 1.14.1；`AssemblyVersion`／`FileVersion` 不變。

### 問題與解法
- 每次開啟最愛樂譜後，`RefreshFavoritesAsync` 因為 `IsCurrent` 改變而 `Clear()`＋重新加入全部項目，ListView 的容器被銷毀，鍵盤焦點退回索引 0 → 改為僅替換 `IsCurrent` 有變化的單筆項目，保留容器與焦點。
- 原本開啟後會把 `SelectedItem` 設為 `null`，選取與焦點不一致 → 改為保持在剛開啟的項目，並在鍵盤操作後以 `FocusState.Keyboard` 還原焦點。
- 保留選取後，清單重新整理（例如切換最愛、從其他方式開啟樂譜）可能誘發 `SelectionChanged` 造成重複載入 → 以 `_suppressFavoriteSelection` 與 `CurrentPath` 比對過濾。

### 待辦／已知限制
- 尚未在 Windows 10 1809 實機以鍵盤連續切換最愛樂譜驗證焦點行為。
- 移除最愛或排序改變時仍會重建集合，該情境下焦點由系統預設處理。

---

## 2026-11-18 - v1.14.0

### 目標
讓匯出的 PDF 第一頁包含樂譜名稱，並修正回到樂譜最前面時標題區塊被捲出畫面的問題。

### 變更
- `PdfExporter.WriteSystemsAsync`：新增可選 `PdfSystem? header`，在系統之前先繪製一次標題區塊。
- `ScoreViewModel.ExportPdfAsync`：以目前的匯出工作階段呼叫 `RenderHeaderPartialAsync`，將頁首 PNG 與邏輯尺寸傳給 PDF 匯出；`maxWidth` 一併納入頁首寬度。
- `ScoreViewModel.GetSystemOffset`：第 1 個系統改回傳內容頂端 `0`，回到第 1 系統或續讀到最前面時會顯示標題；移除不再使用的 `_firstSystemTop`。
- 測試：新增帶頁首的 PDF 匯出案例。
- 版本提升為 1.14.0；`AssemblyVersion`／`FileVersion` 不變。

### 問題與解法
- PDF 原本只繪製系統 PNG，標題僅存在於 PDF 中繼資料 → 改以目前工作階段繪製標題 partial，並排在系統之前，只在第一頁出現。
- 前往第 1 系統原本會捲到包含頁首高度的第一個系統位置，導致頁首被推到畫面外 → 第 1 系統改以內容頂端計算。
- 高解析度與 PDF 用的工作階段各自持有不同的 partial id，沿用 `RenderHeaderPartialAsync` 以工作階段自己的頁首 id 繪製。

### 待辦／已知限制
- 尚未在 Windows 10 1809、125%／150% DPI 實機確認 PDF 第一頁標題與系統的實際排版。
- 頁首仍沿用目前的樂譜樣式；深色譜面匯出的頁首與系統會是深底影像。

---

## 2026-11-18 - v1.13.2

### 目標
修正 1.13.1 之後樂譜標題與譜面之間仍出現過大間距的問題，並讓標題更貼近第一個系統。

### 變更
- `ScoreViewModel.ApplyLayout`：`previousBottom` 改由頁首底部起算，避免第一個系統的 top gap 重複加上頁首高度。
- `AlphaTabScoreSession`：新增 `ScoreInfoBottomPadding`；排版時將頁首高度減去 `17 / rasterScale`，並把系統 Y 同步上移同值。
- `AlphaTabScoreSession.RenderPartialCore`：新增底部裁切量，頁首繪製時裁掉 AlphaTab 內建的 17 像素下緣留白。
- 測試：驗證第一個系統頂端等於頁首底部，且頁首 PNG 高度與 layout 頁首高度一致。
- 版本提升為 1.13.2；`AssemblyVersion`／`FileVersion` 不變。

### 問題與解法
- 1.13.1 的 `ApplyLayout` 以頁首頂端作為第一個系統的 `previousBottom`，使第一個系統多出一個頁首高度的間距（實際樂譜約 89 DIP）→ 改由頁首底部起算。
- AlphaTab 的 `_layoutAndRenderScoreInfo` 會在標題文字後固定加上 17 像素留白 → 排版與繪製同步裁掉，且系統座標一起上移，避免畫面與捲動位置不一致。
- 以實際 `staffs/` 樂譜量測：修正後頁首底部與第一個系統頂端相差 0，頁首影像剩餘的下緣空白主要來自最後一行文字的行高。

### 待辦／已知限制
- 尚未在 Windows 10 1809、125%／150% DPI 實機確認裁切後的視覺間距。
- 若個別樂譜仍覺得標題太遠，可再依實際字型行高調整裁切量。

---

## 2026-11-18 - v1.13.1

### 目標
修正 1.13.0 之後樂譜最上方不再顯示標題的回歸，並讓「目錄」選單在點選項目後自動收起。

### 變更
- `AlphaTabScoreSession`：將 AlphaTab 的 ScoreInfo partial 從系統清單分離為獨立 `Header`；系統仍只保留與 `Layout.Systems` 幾何相符的 partial，調弦、和弦圖與頁尾維持排除。
- `AlphaTabScoreSession`：新增 `RenderHeaderPartialAsync`，以目前工作階段自己的 header id 繪製；`RenderPartialCore` 允許頁首 id。
- `ScoreLayoutResult`：新增 `Header` 欄位。
- `ScoreViewModel`：新增頁首影像與尺寸轉發屬性；`ApplyLayout` 建立頁首、系統位移與 `_systemBottoms` 改為包含頁首座標；載入、縮放、高解析度升級、重解碼、重排與關檔同步頁首。
- `ScorePage.xaml`：`ScrollViewer` 內容改為 `StackPanel`，上方新增 `ScoreHeader` 影像區與載入指示，下方維持系統 `ItemsRepeater`。
- `ScorePage.xaml.cs`：目錄項目選取後呼叫 `OutlineButton.Flyout?.Hide()`。
- 測試：新增頁首存在／可繪製、頁首不計入系統清單、無標題時沒有頁首等案例。
- 版本提升為 1.13.1；`AssemblyVersion`／`FileVersion` 不變。

### 問題與解法
- 1.13.0 為修正系統數與聲部切換，只保留 StaffSystem 幾何相符的 partial，連帶把 AlphaTab 的標題 partial 也排除，造成標題消失。
- 由 AlphaTab IL 確認排版順序為 ScoreInfo → Tunings → ChordDiagrams → Systems → BottomScoreInfo，且 ScoreInfo 只在有標題／副標題／作曲家／版權文字時建立；因此取第一個 system 之前的第一個 partial 作為頁首，其餘 pre-system partial 仍不顯示。
- 高解析度工作階段的 partial id 與基礎工作階段不同，頁首改以工作階段內的 `RenderHeaderPartialAsync` 繪製，避免跨工作階段 id 查找失敗。

### 待辦／已知限制
- 尚未在 Windows 10 1809、125%／150% DPI 與實際 `staffs/` 樂譜確認頁首高度、留白與縮放後的外觀。
- 頁首幾何依賴 AlphaTab pre-system partial 順序；升級 AlphaTab 時需重新驗證。
- PDF 仍只包含實際樂譜系統，不含頁首標題（與 1.13.0 相同）。

---

## 2026-11-18 - v1.12.2

### 目標
匯出 PDF 成功後的訊息提供「開啟 PDF」按鈕，讓使用者不必自行到資料夾尋找檔案。

### 變更
- `ScorePage.xaml`：`ExportBar` 加入 `InfoBar.ActionButton`「開啟 PDF」。
- `ScorePage.xaml.cs`：匯出完成後記錄 `StorageFile`；按鈕以 `Process.Start`（`UseShellExecute`）透過系統預設檢視器開啟，成功後關閉訊息列；檔案不存在或開啟失敗時顯示錯誤。
- `ScoreViewModel`：新增 `ReportExportOpenError`，使用獨立錯誤字串，避免誤用「匯出 PDF 失敗」訊息。
- `Resources.resw`：新增 `ExportPdfOpen`、`ExportPdfOpenName`、`ExportPdfOpenError`、`ExportPdfOpenMissing`。
- 版本提升為 1.12.2；CHANGELOG、RELEASE_NOTES 更新。

### 問題與解法
- 專案為未封裝模式（`WindowsPackageType=None`），採用 `Process.Start` 加 `UseShellExecute` 呼叫預設關聯程式；若日後改為 MSIX 封裝，需改用 `Windows.System.Launcher.LaunchFileAsync`。

### 待辦／已知限制
- 尚未在多種 PDF 關聯程式與 Windows 10 1809 實測。

---

## 2026-11-18 - v1.13.0

### 目標
補上長譜閱讀所需的導覽、聲部顯示與續讀位置。

### 變更
- Rendering：`ScoreLayoutResult` 新增 `Tracks`、`SelectedTrackIndices`、`MasterBarCount`、`Sections`；`AlphaTabScoreSession` 支援指定聲部、`RelayoutAsync` 重用已解析樂譜，並取得每個系統的小節範圍。
- 系統清單只保留實際 StaffSystem，排除標題、調弦、和弦圖與頁尾 partial；`RenderedPartial` 回傳 PNG 與小節範圍。
- Core：新增 `ScoreViewState`，`AppSettings.ScoreViewStates` 保存每個樂譜的縮放、聲部、TopSystem、TopMeasure 與系統內位移；載入時驗證、去重並限制 200 筆。
- ViewModel：聲部切換、`RelayoutAsync`、`GetSystemOffset`、`ResolveSystemForMeasure`、續讀狀態儲存與還原；狀態列顯示目前小節範圍。
- ScorePage：工具列「聲部」與「目錄」Flyout、`Ctrl+G` 對話框、`Ctrl+PageUp`／`Ctrl+PageDown`、1 秒 debounce 儲存閱讀位置；MainWindow 根層處理導覽快捷鍵。
- 資源、README、CHANGELOG、RELEASE_NOTES 更新；版本 1.13.0。
- 測試：新增聲部選擇、非法聲部、系統小節範圍、重用樂譜重排、ViewState 正規化／去重／上限等。

### 問題與解法
- AlphaTab 1.9.0 的 `PartialLayoutFinished`／`PartialRenderFinished` 小節索引固定為 -1 → 以 reflection 讀取 `ScoreRenderer.Layout.Systems` 的 `FirstBarIndex`／`LastBarIndex`，再依 Y 與 Height 對應 partial。
- 標題 partial 讓系統數多 1，聲部切換也比對到錯誤影像 → 只保留與 StaffSystem 幾何相符的 partial。
- `RenderScore` 的聲部參數是 track index，且需傳 `IList<double>`；切換聲部後以已載入的 Score 重新排版。
- CheckBox 的 TwoWay `IsChecked` 在事件中可能尚未更新，且 UI Automation 的 Toggle 不一定觸發 Click → 改用 Click + `Tag` 索引 + `await Task.Yield()`，再從視覺狀態同步模型。
- 「符合寬度」完成前就還原捲動會用錯縮放 → 改由 `TryApplyPendingFitWidth` 成功後才觸發續讀還原。
- 續讀位移改存邏輯單位（除以目前縮放），換縮放或聲部後仍能還原。

### 待辦／已知限制
- 尚未在 Windows 10 1809 與 125%／150% DPI 實機確認聲部 Flyout、目錄與續讀位置。
- `Ctrl+PageUp`／`Ctrl+PageDown` 尚未在實機鍵盤確認（自動化工具無法可靠送出 Page 鍵）；原生 Page 鍵捲動仍保留。
- 系統小節範圍依賴 AlphaTab 內部 `Layout.Systems`，升級 AlphaTab 時需重新驗證。
- 大型總譜切換聲部仍需重新排版數秒，尚未加入專用進度指示。

---

## 2026-11-18 - v1.12.1

### 目標
微調主畫面空狀態與工具列星號的視覺對齊。

### 變更
- `ScorePage.xaml`：空狀態標題「開啟 MusicXML 以顯示五線譜」加上 `HorizontalAlignment="Center"` 與 `TextAlignment="Center"`，在 420 寬區塊中置中。
- `ScorePage.xaml`：最愛星號的 `AppBarElementContainer` 加上 `VerticalAlignment="Center"`，星號按鈕與 `FontIcon` 也都設 `VerticalAlignment="Center"`，讓 40×40 按鈕在 48 DIP 的 CommandBar 列中垂直置中。
- 版本提升為 1.12.1。

### 問題與解法
- 空狀態標題原本會隨 `StackPanel` 寬度（由 `EmptyHint` 的 `MaxWidth="420"` 撐開）靠左排列；補上 `TextAlignment` 後才真正置中。
- 星號一開始只在 `Button` 設 `VerticalAlignment` 無效：`AppBarElementContainer` 的高度等於按鈕本身（40 DIP），在 48 DIP 的工具列列中仍靠上，實測比縮小圖示高約 4 DIP。改讓容器一起置中後，以 UI Automation 與螢幕擷取比對，星號墨跡中心與縮小／放大圖示同為 205 px（125% DPI），因此移除先前新增的 `TranslateTransform`。

### 待辦／已知限制
- 已在 125% DPI 實測對齊；其他縮放比例與 Windows 10 1809 尚未確認。

---

## 2026-11-18 - v1.12.0

### 目標
將主畫面左側的「最近開啟」改為「最愛樂譜」，只顯示使用者明確加入最愛的樂譜，並依樂譜名稱排序。

### 變更
- `AppSettings`：新增 `FavoriteFiles`；`UserSettingsService` 讀取時去重、不因檔案暫時不存在而刪除，並新增 `Favorite`／`Unfavorite` 靜態方法。
- `ScoreViewModel`：新增 `FavoriteScores`、`IsCurrentFavorite`、`RefreshFavoritesAsync`（依 Title 以目前文化排序）、`ToggleFavoriteAsync`、`RemoveFavoriteAsync`；開檔、關檔、啟動時同步最愛清單。
- `ScorePage`：側邊欄改綁最愛清單，工具列縮小按鈕左側新增星號切換鈕、空清單提示與右鍵移除；收合側邊欄時一併隱藏清單。星號以黃色實心（已加入）與黃色空心（未加入）區分狀態。
- `Resources.resw`：新增最愛相關字串並更新側邊欄提示。
- `MainWindow` 的「檔案 > 最近開啟」維持不變。
- 文件與版本：README、CHANGELOG、RELEASE_NOTES 更新；版本提升為 1.12.0。

### 問題與解法
- 最愛清單需要顯示樂譜名稱，但檔案可能暫時不存在；改為在設定中保留路徑、顯示時只讀取存在的檔案，避免外接磁碟未連接就遺失最愛紀錄。
- `List<string>.Contains` 若要忽略大小寫需使用 `Enumerable.Contains` 的多載；`Favorite`／`Unfavorite` 統一以 `StringComparer.OrdinalIgnoreCase` 比對。
- `AppBarToggleButton` 的 checked 狀態會顯示藍色背景／邊框，與黃色星星不搭；改用 `AppBarButton` 後，又因預設寬度與標籤版面讓星星在滑鼠移入的底色方塊中偏左、偏上。最後以 `AppBarElementContainer` 包住 40×40 的透明 `Button`，讓圖示在滑鼠移入的底色方塊中置中，tooltip 與無障礙名稱同步切換。

### 待辦／已知限制
- 執行中插拔外接磁碟時，最愛清單不會即時更新，需重新開啟樂譜或重新啟動程式。
- 尚未實機確認工具列星號在高 DPI 與窄視窗下的外觀。

---

## 2026-11-18 - v1.11.1

### 目標
修正縮小樂譜時，樂譜視窗的 scrollbar 跟著內容往左移動的問題，讓 scrollbar 固定在視窗最右側。

### 變更
- `MainWindow.xaml`：`PageHost` 加上 `HorizontalContentAlignment="Stretch"` 與 `VerticalContentAlignment="Stretch"`，主內容區固定撐滿視窗。
- 文件：CHANGELOG、RELEASE_NOTES 更新；版本提升為 1.11.1（`AssemblyVersion`／`FileVersion` 不變）。

### 問題與解法
- 根因：`PageHost` 是 `ContentControl`，WinUI 預設 `HorizontalContentAlignment` 為 `Left`，其 template 的 `ContentPresenter` 會依內容 desired size 排版而不撐滿視窗。`ScorePage` 內的 `ItemsRepeater` 寬度綁 `ContentWidth`，縮小時整頁（含 `ScrollViewer`）跟著變窄，位於 `ScrollViewer` 右緣的垂直 scrollbar 就往左跑。
- 解法：讓 `PageHost` 內容水平與垂直都撐滿，`ScrollViewer` 右緣固定等於視窗右緣，scrollbar 不再隨樂譜縮放移動；內容較窄時仍由 `ItemsRepeater` 的置中屬性在可用寬度內置中。

### 待辦／已知限制
- 尚未實機確認縮小／放大與拖動視窗大小時的實際外觀。

---

## 2026-11-18 - v1.11.0

### 目標
修正縮小後「符合寬度」無反應、縮放時樂譜位置跳動，並簡化主畫面工具列、把最近開啟收合按鈕移到區塊內。

### 變更
- `ScoreDisplayOptions`／`SettingsPage`：最小縮放由 50% 降為 20%，預設縮放 Slider 同步調整；`ScoreDisplayOptionsTests` 更新預期值。
- `ScoreViewModel`：`FitWidth` 只要目標與目前縮放不同就套用，不再受 `IsFitWidth` 條件影響。
- `ScorePage`：新增 `GetAvailableViewportWidth`（`ViewportWidth`，無效時退回 `ActualWidth`）供點擊、`SizeChanged`、timer 與 pending 共用；新增 `ZoomCentered`，所有按鈕、快捷鍵與 Ctrl+滾輪縮放都以畫面中心換算 `ChangeView` 位移；`ItemsRepeater` 加 `VerticalAlignment="Center"`。
- `ScorePage`：工具列移除開啟、重新載入、最近開啟與 100% 按鈕，只留縮小、放大、符合寬度；相關 automation／tooltip／`IsEnabled` 與事件一併移除。
- `ScorePage`：最近開啟表頭新增 32×32 收合按鈕，`ApplySidebarState` 控制展開 280 DIP／收合 44 DIP 窄邊條，全螢幕退出時還原。
- `Resources.resw`：移除不再使用的按鈕字串，`SidebarToggleName` 改為「收合或展開最近開啟清單」。
- 文件：README、CHANGELOG、RELEASE_NOTES 更新；版本提升為 1.11.0。

### 問題與解法
- 符合寬度失效：`FitWidth` 會把目標縮放夾在最小 50%，視窗較窄或樂譜較寬時需要的縮放低於 50%，被夾住後與目前值相同而不動作；只有目前縮放高於目標時才看得到變化。降最小縮放並讓目標不同就套用後恢復正常。
- 縮放位置跳動：按鈕與快捷鍵只改內容寬度、不修正捲動位置，且 Ctrl+滾輪以游標為錨點。改以視窗中心換算水平／垂直位移，並讓內容小於視窗時垂直置中。

### 待辦／已知限制
- 尚未實機確認 44 DIP 窄邊條在 125%／150% 縮放下的按鈕對齊。
- 尚未在 Windows 10 1809 與超寬螢幕確認符合寬度與置中縮放的實際外觀。

---

## 2026-11-18 - v1.10.2

### 目標
修正啟動程式時視窗出現「F11」快捷鍵提示的問題。

### 變更
- `MainWindow.xaml`：`Root` 加上 `KeyboardAcceleratorPlacementMode="Hidden"`。
- 版本提升為 1.10.2。

### 問題與解法
- F11 全螢幕加速鍵掛在 `Root`，預設 `KeyboardAcceleratorPlacementMode` 為 Auto，視窗取得焦點時會自動顯示快捷鍵提示 tooltip。
- `ScorePage` 原本已設為 Hidden，`MainWindow` 的 Root 漏設；補上後不再顯示提示，F11 功能不受影響。

### 待辦／已知限制
- 尚未實機確認啟動時提示完全不再出現。

---

## 2026-11-18 - v1.10.1

### 目標
調整滑鼠操作：縮放改回 Ctrl+滾輪，單獨滾輪恢復上下捲動，保留左鍵四向拖曳。

### 變更
- `ScorePage.xaml.cs`：`OnScorePointerWheelChanged` 恢復 Ctrl 判斷；未按 Ctrl 時不攔截事件，交由 `ScrollViewer` 上下捲動。
- 按住左鍵四向拖曳與符合寬度時的垂直拖曳維持不變。
- 版本提升為 1.10.1。

### 問題與解法
- 事件仍掛在 `ScoreRepeater`，只有在 Ctrl 按下時才設 `e.Handled`，因此單獨滾輪可正常捲動，Ctrl+滾輪也不會觸發捲動。

### 待辦／已知限制
- Ctrl+滾輪需在樂譜內容上才會縮放；內容兩側留白與系統間隙仍維持捲動行為。
- 尚未實機確認 Ctrl+滾輪縮放與單獨滾輪捲動的切換體感。

---

## 2026-11-18 - v1.10.0

### 目標
調整滑鼠操作：滾輪直接縮放五線譜，按住左鍵可上下左右拖曳。

### 變更
- `ScorePage.xaml`：`ScoreRepeater` 新增 `PointerWheelChanged`；移除建構子中掛在 `ScoreScrollViewer` 的處理常式。
- `ScorePage.xaml.cs`：滾輪不再要求 Ctrl，維持 5% 級距並以游標位置為縮放中心；拖曳新增垂直軸、起始位置與四向位移，起拖條件放寬為水平或垂直任一可捲動。
- 版本提升為 1.10.0。

### 問題與解法
- 符合寬度時 `ScrollableWidth` 為 0，原本的起拖條件讓樂譜完全無法拖曳 → 改為水平或垂直可捲動即可，無可捲動的軸傳 `null` 給 `ChangeView`。
- 掛在 `ScrollViewer` 的滾輪處理可能在它先捲動後才執行 → 改由樂譜內容攔截並設 `e.Handled = true`，避免縮放與垂直捲動同時發生。

### 待辦／已知限制
- 滾輪需在樂譜內容上才會縮放；內容兩側留白區域仍維持捲動行為。
- 尚未實機確認觸控板雙指捲動改為縮放後的操作體感。

---

## 2026-11-17 - v1.9.0

### 目標
新增可調整的螢幕樂譜品質與 PDF 匯出品質，讓使用者依裝置效能選擇解析度。

### 變更
- `QualityLevel`：新增標準／高／最高三級與預設值（高）。
- `ScoreDisplayOptions`：新增螢幕基礎／上限倍率與 PDF 倍率對應；標準 1.5×／2.5×、高 2×／4×、最高 3×／4×，PDF 為 2×／3×／4×。
- `AppSettings`／`UserSettingsService`：新增 `ScoreQuality`、`PdfQuality` 並驗證非法值回退「高」。
- `ScoreViewModel`：螢幕倍率改由品質設定決定；PDF 匯出依品質選擇或建立足夠解析度的工作階段，完成後釋放。
- `SettingsPage`／`Resources.resw`：新增「樂譜品質」與「PDF 品質」選單與說明；切換樂譜品質會重新排版。
- 測試：品質對應表、預設值與非法值回退、1.5×／4× 版面穩定性、倍率上下限。
- 版本提升為 1.9.0。

### 問題與解法
- PDF 品質原本取決於使用者是否曾縮放到足以建立高解析度工作階段 → 匯出時依 PDF 品質另建工作階段，並以系統索引繪製。
- 品質基礎倍率改為設定值後，`ScorePartialViewModel` 不再寫死 2× 預設，避免尚未繪製時解碼尺寸誤判。

### 待辦／已知限制
- 「最高」以 3× 基礎影像排版，大型總譜記憶體約為現行的 2.25 倍。
- 「標準」上限 2.5×，高倍縮放時會比「高」模糊。
- AlphaTab 工作階段倍率上限仍為 4×；尚未在 4K／超寬螢幕與 Windows 10 1809 實機驗證品質差異。

---

## 2026-11-17 - v1.8.1

### 目標
修正放大或符合寬度後出現「繪製失敗：Specified argument was out of the range of valid values. (Parameter 'id')」的問題。

### 變更
- 問題原因：alphaTab 的 partial id 是每次排版產生的隨機 GUID；高解析度工作階段建立後，畫面仍以基礎工作階段的 id 呼叫高解析度工作階段，導致 id 查找失敗。
- `AlphaTabScoreSession`：新增以系統索引繪製的 `RenderPartialAsync(int)`，於同一個 worker 內解析該工作階段自己的 partial id；抽出 `RenderPartialCore` 避免巢狀排隊造成死鎖。
- `ScorePartialViewModel`：加入 `Index` 與 `BaseY`。
- `ScoreViewModel`：可見系統、預取、高解析度升級與 PDF 匯出都改以系統索引繪製；高解析度版面比對加入垂直位置。
- 測試：新增跨工作階段以索引繪製的案例，涵蓋第一個與最後一個系統及越界索引。
- 版本提升為 1.8.1。

### 問題與解法
- 高解析度工作階段與基礎工作階段各自持有不同的隨機 partial id，僅靠 id 無法跨工作階段對應；改以兩個工作階段共同的系統順序對應，並以既有的幾何比對確保版面一致。

### 待辦／已知限制
- 尚未在 4K／超寬螢幕實機確認高倍縮放與 PDF 匯出的畫質與效能。

---

## 2026-11-17 - v1.8.0

### 目標
新增可調整的「音符間距」偏好設定，讓不同時值音符之間的距離更緊湊。

### 變更
- `NoteSpacing`：新增緊湊／標準／寬鬆三種間距預設。
- `ScoreDisplayOptions`：新增 `SpacingRatio` 上下限 1.2–2.0、三種預設值與 `GetSpacingRatio`、`ClampSpacingRatio`。
- `AppSettings`：新增 `NoteSpacing`，預設緊湊；`UserSettingsService` 載入時驗證非法值。
- `AlphaTabScoreSession`：`LoadAsync` 新增 `NoteSpacing` 參數，套用 `Display.SpacingRatio`。
- `ScoreViewModel`：基礎與高解析度工作階段都使用相同音符間距，避免版面不一致。
- `SettingsPage`：新增「音符間距」選單與說明；變更後重新排版並保存。
- 測試：驗證間距對應、設定預設／非法值，以及緊湊與寬鬆會產生不同的系統影像。
- 版本提升為 1.8.0。

### 問題與解法
- 目前以固定 `BarsPerRow` 排版，每行都會左右對齊到頁寬；`spacingRatio` 只改變不同時值音符的相對寬度，不會改變整行寬度或平均音符距離。實際整體疏密仍需搭配「每行小節數」調整。
- `StretchForce` 在固定小節數並左右對齊時，會因權重正規化而互相抵銷，因此未採用。

### 待辦／已知限制
- 尚未在實際樂譜與 Windows 10 1809 上確認緊湊／寬鬆的可感受差異。
- 若仍覺間距不足，後續可考慮提高每行小節數上限或改為不強制撐滿頁寬。

---

## 2026-11-09 - v1.7.0

### 目標
執行 P0 與 P1 優化：修正關閉／開檔／單一實例／非同步事件穩定性，改善高倍縮放與 PDF 輸出品質，並處理分頁間距、系統色彩重排、小螢幕與縮放上限。

### 變更
- `ScoreViewModel`：關閉樂譜後重建最近清單；開檔開始與失敗時清除舊標題、作曲家與路徑；新增外部開檔去重。
- `App`：加入 `TaskScheduler.UnobservedTaskException`、`AppDomain.UnhandledException`；`fatal.log` 超過 1 MB 時輪替並保護寫入；關閉流程加入例外保護。
- `MainWindow`：常用非同步事件統一以 `GuardAsync` 記錄例外；系統色彩變更只在明暗真正改變時重排；最小視窗寬度降為 900。
- `ScorePage`：PDF 先寫入 `.tmp` 再原子取代；新增最近開啟清單收合按鈕；開新檔時重設符合寬度狀態。
- `AlphaTabScoreSession`：`LoadAsync` 支援 1.0–4.0 的 raster scale；高解析度工作階段沿用相同邏輯版面。
- `ScoreViewModel`：縮放超過約 160% 時建立高解析度工作階段，延遲 150 ms 合併請求，僅重繪可見系統；DPI 變化會影響目標解析度。
- `PdfExporter`：新增 `PdfSystem` 與 `PdfExportOptions`，保留各系統自然相對寬度，支援 A4／A3／Letter，並在可用時使用高解析度來源。
- `ScorePartialViewModel`：加入 `TopGap` 與 `SourceRasterScale`；分頁系統間距依 `Y` 保留。
- `AppSettings`／`SettingsPage`：新增 PDF 紙張大小；自訂縮放上限改為 400%。
- 測試：新增 raster scale 版面穩定性、PDF 自然寬度／A3 與設定紙張大小驗證。
- 版本提升為 1.7.0。

### 問題與解法
- 高倍縮放模糊：固定 2 倍 PNG 被放大 → 依縮放與 DPI 建立 2.5–4 倍工作階段，只重繪可見系統；測試確認 2 倍與 3 倍的邏輯版面一致。
- 關閉樂譜後最近清單仍亮：只清 `LastScorePath` 不會更新 `IsCurrent` → 關閉後重建最近清單。
- 開檔失敗殘留舊資訊：`LoadScoreAsync` 開頭未重設狀態 → 在開始與失敗時清空標題、作曲家、版面與路徑。
- 單一實例重複開檔：管道與啟用事件都會開同一檔 → `OpenFromExternalAsync` 以載入中／目前路徑去重。
- PDF 損壞檔：直接寫入目標檔案 → 先寫暫存檔，成功後 `File.Move` 取代。
- 系統色彩重排：強調色變更也會觸發 `ColorValuesChanged` → 追蹤上次明暗狀態，只在實際切換時重排。
- 分頁間距：`StackLayout` 只用高度堆疊 → 依 `partial.Y` 計算 `TopGap` 並納入目前系統位置。

### 待辦／已知限制
- PDF 仍為高解析度點陣輸出，尚未改為向量；紙張邊界固定 40 pt。
- 高解析度工作階段會增加記憶體與繪製時間，需在 4K／超寬螢幕與大型總譜實機確認體感。
- 尚未在 Windows 10 1809 實機驗證小螢幕、DPI 切換與高解析度縮放。
- 尚未加入 `.editorconfig`、CI、ViewModel／UI 自動化測試與套件簽章。

---

## 2026-11-08 - v1.6.0

### 目標
在不改變使用流程的前提下，改善大譜流暢度、開檔延遲、記憶體、I/O 與單一實例的穩定性。

### 變更
- `ScoreViewModel`：目前系統改用累積高度陣列與二分搜尋；最近清單避免重複重建；樂譜影像依顯示尺寸解碼，縮放差距達 20% 時重新解碼；啟動時優先使用命令列帶入的樂譜，避免同時載入上次樂譜。
- `ScorePage`：啟動路徑直接傳入 `InitializeAsync`，不再於初始化後重複開檔；視窗大小變更加入 120ms timer 合併符合寬度更新；`PropertyChanged` 只處理相關屬性；快取 `ResourceLoader`。
- `FileLoggerProvider`：改為常駐 `StreamWriter`、有界 channel、每日輪替與 30 天保留。
- `ScoreMetadataReader`：以 `LastWriteTimeUtc` 與檔案大小快取 metadata，並避免快取共用 `IsCurrent` 狀態。
- `UserSettingsService`：`LoadAsync` 與 `SaveAsync` 共用同一把鎖，儲存改用 `File.Replace`（不支援時退回 `File.Move`）。
- `MainWindow`：系統色彩變更加 300ms 合併；共用 `App.SystemUISettings`。
- `App`：單一實例管道名稱加入使用者與 session id，並加上 `PipeOptions.CurrentUserOnly`；快取 `UISettings`。
- 新增 metadata 快取失效與 `IsCurrent` 隔離測試。
- 版本提升為 1.6.0。

### 問題與解法
- 大譜每次捲動都線性掃描所有系統 → 預先建立累積高度，查詢改二分搜尋。
- 逐行開檔寫日誌成本高 → 常駐 writer 並以有界佇列避免磁碟慢時無限成長。
- 連續調整視窗或系統主題變更會重複重排 → 以 timer 合併後只做最後一次。

### 待辦
- 尚未在 Windows 10 1809 與 4K／超寬螢幕實機驗證捲動與縮放體感。
- 影像解碼尺寸調校（1.25 倍餘裕與 20% 重解門檻）可在實機上再微調。

---

## 2026-11-08 - v1.5.0

### 目標
把縮放改為 5% 級距，讓樂譜與視窗邊緣保留間隔，並修正「符合寬度」在寬螢幕上看起來偏窄的問題。

### 變更
- `ScoreDisplayOptions`：新增縮放上下限、5% 級距與邊緣間隔常數，以及 `ClampZoom`、`NextZoom`。
- `ScoreViewModel`：縮放按鈕與重設改用 5% 級距；符合寬度改用內容寬度計算並放寬上限；系統位置計算扣除頂部間隔。
- `ScorePage`：Ctrl+滾輪縮放改用 5% 級距；樂譜清單四周加上 16 DIP 邊距。
- `AlphaTabScoreSession`：以實際 partial 邊界重新計算內容寬度並扣掉頁面左右留白。
- `SettingsPage`：預設縮放滑桿每次調整 5%。
- 新增 `ScoreDisplayOptionsTests` 驗證 5% 對齊與上下限。
- 版本提升為 1.5.0。

### 問題與解法
- 符合寬度原本以 AlphaTab 總版面寬度計算，包含頁面左右留白，且上限只有 200%，在寬螢幕會留下大片空白 → 改以實際 partial 內容邊界計算，並把上限提高到 400%。
- 5% 級距若直接加減，符合寬度產生的非 5 倍數值會出現 78% → 83% 這種不整齊的級距 → 改為對齊下一個 5% 倍數（78% → 80%／75%）。

### 待辦
- 尚未在 Windows 10 1809 實機驗證縮放級距與四周留白。
- 尚未在 4K／超寬螢幕人工確認符合寬度的實際外觀。

---

## 2026-11-08 - v1.4.0

### 目標
改善閱讀流程與介面回饋：顯示縮放狀態、支援滑鼠滾輪縮放與全螢幕、保留重排後的捲動位置、強化最近開啟清單與選單操作，並降低 PDF 匯出的記憶體用量。

### 變更
- `ScoreViewModel`：新增縮放百分比、持續符合寬度、目前系統、作曲家、匯出進度、關閉樂譜、移除單筆最近開啟、錯誤清除與重載事件。
- `ScorePage`：工具列顯示縮放百分比與快捷鍵提示、Ctrl+滾輪縮放、錯誤列可關閉、拖放提示遮罩與不支援格式訊息、最近清單標示目前樂譜及右鍵移除、狀態列顯示作曲家與系統位置、全螢幕時隱藏側欄與工具列。
- `MainWindow`：改用原生 `MenuBar`，第一層選單可用滑鼠移動自動切換；新增重新載入、關閉樂譜、全螢幕（F11／Esc）；匯出期間停用選單項目。
- `App`：解析啟動參數與第二次啟動的檔案，交給既有視窗開啟。
- `ThemeService`：標題列按鈕顏色跟隨主題；`PdfExporter` 新增串流匯出 API，逐張載入與繪製系統。
- 版本提升為 1.4.0。

### 問題與解法
- 自訂 `Button` + `MenuFlyout` 的選單在開啟後移到另一個第一層按鈕不會自動切換 → 改為 WinUI 原生 `MenuBar`，由控制項處理 hover 切換。
- `Windows.UI.Colors` 在目前 WinUI 組態無法解析 → 改用 `Microsoft.UI.Colors.Transparent`。
- 建置時輸出 DLL 被執行中的 `MusicStaffViewer` 鎖定 → 先關閉執行中的程式再建置。
- AppInstance 重新導向只會帶回視窗，不會帶回第二個執行個體的命令列檔案 → 另以具名管道把檔案路徑傳給第一個執行個體，並保留 activation 檔案事件作為備援。
- 持續符合寬度若直接在 `SizeChanged` 觸發縮放可能反覆回呼 → 只在目標縮放與目前值不同時才套用。
- PDF 原本一次持有所有 PNG → 改為逐張解碼、繪製後釋放，並以 `IProgress<int>` 回報進度。

### 待辦
- 尚未在 Windows 10 1809 實機測試全螢幕、標題列配色與第二次啟動帶檔。
- 尚未人工驗證 MenuBar 在長選單與鍵盤操作下的外觀。

---

## 2026-11-08 - v1.3.0

### 目標
縮小偏好設定畫面，讓新開樂譜預設符合寬度，並將每行小節數擴充為 4–10。

### 變更
- 參考 MyDraw，偏好設定改用 ContentDialog 原生標題與單欄 `ComboBox.Header` 表單，內容寬度 320–400 DIP、高度上限 520 DIP。
- 一般偏好設定改為草稿，按「套用」才一次保存並執行必要的主題套用或譜面重繪；取消不修改設定。
- 新增 `InitialZoomMode`，預設 `FitWidth`，自訂模式才顯示預設縮放 Slider。
- 新開、拖放及啟動恢復樂譜會在 ScrollViewer 完成布局後符合寬度；重新載入與重排保留目前縮放。
- 每行小節數共用範圍改為 4–10，預設 6，避免 UI、設定與 Rendering 邊界不一致。
- 版本提升為 1.3.0。

### 問題與解法
- 載入完成時 ScrollViewer 可能尚無有效 viewport → 以 pending 狀態配合 DispatcherQueue 與 `SizeChanged`，取得有效寬度後才套用符合寬度。
- 原偏好設定同時有 ContentDialog 內距、頁面 Margin 與卡片 Padding → 移除卡片與頁內標題，改由原生對話框負責標題和間距。
- 即時套用會讓取消無法還原且 Slider 持續寫檔 → 改用草稿值與 PrimaryButton deferral，保存失敗時留在對話框並還原模型。

### 待辦
- 尚未在 Windows 10 1809 實機測試緊湊偏好設定與初始符合寬度。

---

## 2026-11-08 - v1.2.0

### 目標
加入可設定的每行小節數，改善偏好設定與關於對話框，並修正樂譜縮放、提示與水平瀏覽問題。

### 變更
- `AppSettings` 新增預設每行小節數，範圍 4–8、預設 6；AlphaTab 透過 `BarsPerRow` 套用排版。
- 樂譜工具列右側新增目前樂譜的小節數選單，重排時保留縮放且不回寫全域預設。
- 偏好設定改為外觀、樂譜顯示與資料管理三區，移除開啟日誌資料夾按鈕。
- 重整關於頁的產品、版本、授權與隱私資訊層級；偏好設定與關於對話框置中顯示。
- 樂譜超出畫面寬度時可按住內容左右拖曳。
- 版本提升為 1.2.0。

### 問題與解法
- 頁面級 `KeyboardAccelerator` 會由 WinUI 自動顯示 Ctrl+O 提示 → 將頁面的 `KeyboardAcceleratorPlacementMode` 設為 `Hidden`，保留明確的按鈕工具提示。
- 符合寬度混用固定邏輯寬度與預設縮放倍率，重載後會再次縮小 → 改用實際布局寬度與 `ScrollViewer.ViewportWidth`，且明確區分預設縮放和符合寬度。
- 一般滑鼠拖曳不會觸發 `ScrollViewer` 平移 → 在樂譜內容捕捉滑鼠指標，依起始水平位移呼叫 `ChangeView`。
- WinUI 3 指標事件使用 `Microsoft.UI.Input.PointerPoint`，不是舊 UWP 的 `Windows.UI.Input.PointerPoint` → 改用 Windows App SDK 命名空間。
- 直接設定 `ContentDialog.MinWidth/MaxWidth` 會限制 Popup 遮罩並使內容貼向左側 → 尺寸改由內部頁面的 `MaxWidth` 自適應，保留全視窗遮罩讓 WinUI 範本置中內容。

### 待辦
- 尚未在 Windows 10 1809 實機測試對話框置中與滑鼠拖曳。
- PDF 版面採固定 A4 與邊界，尚未提供紙張大小或邊界設定。

---

## 2026-11-08 - v1.1.0

### 目標
新增系統選單、移除主畫面樂譜庫、提供 PDF 匯出，並將 `staffs/` 排除在版本控制之外。

### 變更
- MainWindow 改以自訂選單列（`Button` + `MenuFlyout`）呈現「檔案／設定」；檔案含開啟 MusicXML、最近開啟子選單、匯出 PDF、關於。
- 設定與關於改以 `ContentDialog` 呈現；偏好設定以 Root 的 `KeyDown` 處理 Ctrl+, 全域快捷鍵。
- ScorePage 移除搜尋與樂譜庫，只保留最近開啟清單。
- ScoreViewModel 移除 `ScoreCatalogService` 與搜尋邏輯，僅自動開啟上次樂譜。
- 新增 `PdfExporter`，使用 SkiaSharp 將所有系統 PNG 排成多頁 A4 PDF。
- 清除最近開啟改為 `Flyout` 確認，避免對話框嵌套。
- `staffs/` 加入 `.gitignore`，移除 app 與測試專案的建置複製設定。
- 版本提升為 1.1.0。

### 問題與解法
- `KeyboardAccelerator` 使用 OEM 逗號鍵（`VirtualKey` 188）會在啟動數秒後造成 `Microsoft.UI.Xaml.dll` 延遲崩潰（0xC000027B） → 改以 Root 的 `KeyDown` 搭配 `InputKeyboardSource` 判斷 Ctrl+,，不再使用 OEM 鍵的 `KeyboardAccelerator`。
- 設定改為 ContentDialog 後，原本的清除確認 ContentDialog 會造成巢狀對話框 → 改成 Button 的 Flyout 確認。
- 重複開啟設定／關於時，重用同一個 UserControl 會出現「Element is already the child of another element」 → 對話框關閉後將 `Content` 設為 null。
- Rendering 測試原本依賴 `staffs/` 真實檔案 → 改為測試內產生最小 MusicXML，PDF 測試改用 AlphaSkia 產生 PNG。
- SkiaSharp 與 AlphaSkia 各自帶原生程式庫 → 兩者並存無衝突，PDF 僅在 Rendering 專案使用 SkiaSharp。
- `dotnet format` 發現既有的 RenderWorker 物件初始化格式問題 → 執行 format 修正。

### 待辦
- 尚未在 Windows 10 1809 實機測試選單、對話框與 PDF 匯出。
- PDF 版面採固定 A4 與邊界，尚未提供紙張大小或邊界設定。

---

## 2026-11-08 - v1.0.0

### 目標
依已通過的 AlphaTab Spike 建立純 C# WinUI 3 樂譜檢視器，以 lazy partial 呈現 `staffs/` 的 MusicXML。

### 變更
- 建立 solution、共用版本設定、Core／Rendering／WinUI 專案與測試。
- Core 實作 XML／JSON wrapper 讀取、樂譜目錄、設定與視窗邊界；Rendering 實作 AlphaTab lazy partial 與深色譜面。
- WinUI 3 加入樂譜庫、搜尋、最近檔案、拖放／開啟檔案、縮放、五款主題、設定與關於頁、視窗狀態、單一實例、圖示與日誌。
- Rendering smoke test 已涵蓋 7 首 MusicXML（含 4.3 MB 命運交響曲與 2 首 JSON wrapper），首個譜面 partial 均成功輸出；深色背景像素不透明。
- 加入 16 個 PNG partial 的 LRU 快取與下一系統預載；以資源字串顯示目前已繪製數量，按需產生圖像。
- `README.md`、`CHANGELOG.md`、`RELEASE_NOTES.md`、`THIRD-PARTY-NOTICES.md`：記錄功能、建置方式與授權。

### 問題與解法
- Spike 發現兩個副檔名為 `.musicxml` 的檔案實為 JSON wrapper → 正式讀取服務需辨識並擷取 `musicxml` 欄位。
- WinUI XAML `AppBarButton Icon="FitPage"` 可編譯但執行時拋出無效圖示例外 → 改用有效的 `FullScreen` Symbol，並加入啟動例外記錄。
- WinUI 資源 `x:Uid` 的屬性鍵不能直接以點號傳給 `ResourceLoader.GetString` → 自動化名稱改用獨立的資源鍵。
- `ItemsRepeater` 的 `x:Bind` DataTemplate 在 `ElementPrepared` 時 `DataContext`／`Tag` 為 null，造成只排版不繪圖 → 以事件的 `Index` 取回 partial 並將其存到 element.Tag，解除實體化時釋放影像。
- PRI 資源鍵不能同時作為 `x:Uid` scope 與純字串資源 → 自動化名稱另設獨立鍵。
- 7 首 smoke test 曾因 `1-allegro` 同時匹配兩首曲目而失敗 → 測試改用完整唯一檔名片段，重新執行 8/8 通過。
- `KeyboardAccelerator Key="187"` 可編譯但執行時無法解析 VirtualKey → 改用程式碼建立主鍵盤 `Ctrl+=`／`Ctrl+-` 快捷鍵。
- DI 關閉 Host 時會再次 Dispose 已由 ViewModel 釋放的 transient AlphaTab session → `DisposeAsync` 加上冪等保護。
- Host 關閉時亦會再次 Dispose 手動釋放的 ScoreViewModel，造成已釋放 CTS 呼叫 Cancel → ViewModel 的 `DisposeAsync` 加上冪等保護。
- 固定邏輯寬度 1600 DIP、2× 點陣之 lazy 服務以 UI Automation 選取命運交響曲與鋼琴三重奏並捲動到結尾，分別排出 42／63 個系統；交響曲 UI 行程峰值約 472 MB，未整張拼接而只繪製可見系統。
- 替換 merged theme dictionary 後既有控制項未即時更新顏色 → 改為在 `App.xaml` 定義共用 `SolidColorBrush`，由 `ThemeService` 依五款主題與明暗模式直接更新 `Color`；截圖像素驗證經典藍／森林綠／深色確實不同。
- 讀取 JSON wrapper 的 UTF-8 BOM 偵測在 XML 分支未重設串流位置，造成 XML 標題回退成檔名 → XML 解析前重設 `Position = 0`，並補上 BOM 測試。

### 驗證
- `dotnet build`（Debug）零錯誤、零警告；`dotnet test` 共 17/17 通過；`dotnet format --verify-no-changes` 通過。
- UI Automation 測試設定頁的五款主題選擇、顯示模式、黑底樂譜、關於頁、單一實例與視窗狀態保存，並確認退出後沒有新的 fatal log。
- UI Automation 驗證七首樂譜清單、可見部分的圖像載入與大量總譜捲動，命運交響曲排版後首張圖迅速出現。

### 待辦／已知限制
- 未在 Windows 10 1809 實機測試，也沒有 `.mxl` 範例檔做匯入測試；AlphaTab 仍支援該格式。
- UI 透過 UI Automation、渲染輸出與像素分析驗證；完整畫面及七首曲目的人工目視檢查仍待使用者執行。
- AlphaTab 會重排系統，不重現原版印刷分頁；不提供 MIDI 播放、編輯、列印或 MSIX 安裝檔。

---

## 2026-11-07 - v0.1.0-alpha（AlphaTab Spike）

### 目標
驗證是否能以純 C#（AlphaTab .NET + Skia）取代 WebView2 + OSMD，渲染 `staffs/` 的 MusicXML。

### 變更
- 於 `%TEMP%\opencode\alphatab-spike` 建立拋棄式 console Spike（未進版控）。
- 套件：`AlphaTab` 1.9.0、`AlphaSkia.Native.Windows` 3.5.147，目標 `win-x64`。
- 實測 3 首：
  - 莫札特-土耳其進行曲
  - 莫札特-G大調鋼琴三重奏
  - 韋瓦第-四季-春
- 輸出：`sheet.png`、`partial-XXX.png`、`view-XX.png`、`result.json`。

### 結果
| 曲目 | Tracks | Partials | 尺寸 | 載入 | 渲染 | 峰值記憶體 | 5 線譜群 |
| --- | ---: | ---: | --- | ---: | ---: | ---: | ---: |
| 土耳其進行曲 | 1 | 21 | 1600×4421 | 666 ms | 1049 ms | 125.6 MB | 36 |
| 鋼琴三重奏 | 3 | 64 | 1600×22714 | 1171 ms | 3004 ms | 377.3 MB | 244 |
| 四季-春 | 5 | 26 | 1600×10997 | 729 ms | 1441 ms | 218.0 MB | 102 |

- 3/3 成功，無錯誤、無空白 partial。
- 每首均有黑色音符／文字連通元件，最低黑色墨水比例 0.008 以上。
- 譜線、音符、譜號、文字與多譜表系統皆有實際像素內容。

### 問題與解法
- AlphaTab NuGet 不含原生 Skia DLL → 加 `AlphaSkia.Native.Windows` 並設定 `RuntimeIdentifier=win-x64`。
- .NET SVG 後端依賴 Bravura CSS 字型，WinUI 直接顯示會缺字 → Spike 改用 Skia PNG。
- `piano-trio`（及 `piano-sonata-16`）的 `.musicxml` 實際是 JSON wrapper，內含 `musicxml` 字串 → 載入前偵測 `{` 並取出內嵌 MusicXML。
- 初次五線譜偵測失敗：AlphaTab 預設譜線為灰色（約 165），`<128` 門檻看不到 → 改以 `<200` 分析後確認 5 線譜完整。
- 鋼琴三重奏總高 22,714 px，正式版不能整張塞進單一 Image，需 partial 虛擬化。
- `Display.Scale = 1.5` 會改變 layout 寬度（Width 以 DIP 計），土耳其進行曲由 21 個 partial 變成 31 個、總高 9580 px；正式縮放需同步調整 `Width` 與 `Scale`，或改用影像縮放。
- 譜線預設為灰色（約 165），可用 `Display.Resources.StaffLineColor` 調整。

### 待辦／已知限制
- 尚未由人眼確認實際排版品質；本次以像素與五線譜結構分析驗證。
- 未測試 4.3 MB 命運交響曲與 `.mxl`。
- AlphaTab 採連續系統排版，不保證 MusicXML 印刷分頁。
- 譜線顏色、字型、間距仍需在正式版調整。
- 若正式採用，需在 WinUI 3 以虛擬化清單呈現 partial，並支援 JSON wrapper。

### 結論
AlphaTab 純 C# 路徑在 3 首上可穩定渲染且效能可接受；待視覺確認後再決定是否取代 OSMD。

---

## YYYY-MM-DD - vX.Y.Z

### 目標
（這次要做什麼、為什麼）

### 變更
- （檔案／功能）

### 問題與解法
- （錯誤訊息 → 最後怎麼解）

### 待辦／已知限制
- （尚未完成或未驗證的部分）
