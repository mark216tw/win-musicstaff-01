# 建置、測試與發佈指南

本文件說明如何從原始碼建置 MusicStaffViewer、執行測試，以及產生 GitHub Release 使用的 x64／ARM64 壓縮檔。

---

## 1. 開發環境

| 工具 | 版本 |
| --- | --- |
| Windows | 10 1809（17763）以上；開發環境為 Windows 11 |
| .NET SDK | 10.0 |
| Visual Studio | 2022 17.x（選用，需 Windows App SDK 工作負載） |
| Git | 任意近期版本 |
| GitHub CLI | 選用，用於建立 Release |

確認環境：

```powershell
dotnet --version
dotnet workload list
```

---

## 2. 還原與建置

```powershell
dotnet restore MusicStaffViewer.sln
dotnet build MusicStaffViewer.sln -c Debug -p:Platform=x64
```

- 開發與測試一律使用 `Debug`。
- `Directory.Build.props` 啟用 nullable、隱含 using，並將警告視為錯誤。
- 套件版本集中於 `Directory.Packages.props`，請勿在個別 `.csproj` 寫死版本。

### 2.1 執行

```powershell
dotnet run --project src/MusicStaffViewer/MusicStaffViewer.csproj -c Debug -p:Platform=x64
```

---

## 3. 測試

```powershell
dotnet test MusicStaffViewer.sln -c Debug -p:Platform=x64
dotnet format --verify-no-changes
```

- `MusicStaffViewer.Core.Tests` 驗證設定、檔案讀取、metadata、視窗狀態與顯示選項。
- `MusicStaffViewer.Rendering.Tests` 驗證 AlphaTab 排版、partial 繪製與 PDF 輸出。
- 送出變更前請確認測試全數通過且格式檢查無差異。

---

## 4. 版本管理

版本集中於 `Directory.Build.props`：

```xml
<Version>1.16.2</Version>
<AssemblyVersion>1.16.0.0</AssemblyVersion>
<FileVersion>1.16.0.0</FileVersion>
```

規則：

- 每次功能變更提升 `Version`（`主.次.修`）。
- `AssemblyVersion`／`FileVersion` 只在主次版變更時調整。
- 同步更新 `CHANGELOG.md`、`RELEASE_NOTES.md` 與 `docs/DEV_LOG.md`。

---

## 5. 發佈設定

本專案發佈為**未封裝、非 Self-Contained**：

| 屬性 | 值 | 說明 |
| --- | --- | --- |
| `SelfContained` | `false` | 不包含 .NET Runtime，使用者需安裝 .NET 10 Desktop Runtime |
| `WindowsAppSDKSelfContained` | `true` | Windows App SDK 執行元件隨程式附帶 |
| `WindowsPackageType` | `None` | 未封裝，免安裝 |
| `RuntimeIdentifier` | `win-x64`／`win-arm64` | 對應處理器架構 |
| `Platform` | `x64`／`ARM64` | 對應 MSBuild 平台 |

> 由於 `SelfContained=false`，使用者必須先安裝 .NET 10 Desktop Runtime。Windows App SDK 已自包含，不需另外安裝。

---

## 6. 產生發佈檔案

以下指令在專案根目錄執行，輸出到 `artifacts/`（已被 `.gitignore` 忽略）。

### 6.1 x64

```powershell
dotnet publish src/MusicStaffViewer/MusicStaffViewer.csproj `
  -c Release `
  -r win-x64 `
  -p:Platform=x64 `
  -p:SelfContained=false `
  -p:WindowsAppSDKSelfContained=true `
  -o artifacts/publish/win-x64
```

### 6.2 ARM64

```powershell
dotnet publish src/MusicStaffViewer/MusicStaffViewer.csproj `
  -c Release `
  -r win-arm64 `
  -p:Platform=ARM64 `
  -p:SelfContained=false `
  -p:WindowsAppSDKSelfContained=true `
  -o artifacts/publish/win-arm64
```

### 6.3 檢查輸出

發佈後確認：

- `MusicStaffViewer.exe` 的 PE 架構與目標一致（x64 為 `0x8664`、ARM64 為 `0xAA64`）。
- 原生程式庫存在且架構一致：
  - `libalphaskia.dll`
  - `libSkiaSharp.dll`
  - `Microsoft.WindowsAppRuntime.Bootstrap.dll`
- `THIRD-PARTY-NOTICES.md` 已複製到輸出目錄。

可用 PowerShell 快速檢查 PE 架構：

```powershell
function Get-PeMachine($path) {
    $fs = [IO.File]::OpenRead($path)
    $br = [IO.BinaryReader]::new($fs)
    $fs.Position = 0x3C
    $offset = $br.ReadInt32()
    $fs.Position = $offset + 4
    $machine = $br.ReadUInt16()
    $br.Close()
    switch ($machine) {
        0x8664 { 'x64' }
        0xAA64 { 'ARM64' }
        0x014C { 'x86' }
        default { '0x{0:X}' -f $machine }
    }
}

Get-PeMachine artifacts/publish/win-x64/MusicStaffViewer.exe
Get-PeMachine artifacts/publish/win-arm64/MusicStaffViewer.exe
```

### 6.4 精簡與打包

發佈目錄可能包含原生 PDB 偵錯符號，體積較大。打包前可移除：

```powershell
Get-ChildItem artifacts/publish -Recurse -Filter *.pdb | Remove-Item -Force
```

建立 Release 用 zip：

```powershell
New-Item -ItemType Directory -Force artifacts/release | Out-Null

Compress-Archive -Path artifacts/publish/win-x64/* `
  -DestinationPath artifacts/release/MusicStaffViewer-1.16.2-win-x64.zip -Force

Compress-Archive -Path artifacts/publish/win-arm64/* `
  -DestinationPath artifacts/release/MusicStaffViewer-1.16.2-win-arm64.zip -Force
```

zip 解壓後應可直接看到 `MusicStaffViewer.exe`，不要多包一層資料夾。

---

## 7. GitHub Release

### 7.1 建立儲存庫（首次）

```powershell
gh repo create mark216tw/win-musicstaff-01 --public `
  --description "Windows MusicXML 五線譜檢視器（WinUI 3 / .NET 10，x64/ARM64，MIT License）" `
  --source . --remote origin --push
```

### 7.2 提交與推送

```powershell
git add .
git commit -m "Release 1.16.2"
git push origin master
```

### 7.3 建立 Release 並附加檔案

```powershell
gh release create v1.16.2 `
  artifacts/release/MusicStaffViewer-1.16.2-win-x64.zip `
  artifacts/release/MusicStaffViewer-1.16.2-win-arm64.zip `
  --repo mark216tw/win-musicstaff-01 `
  --title "MusicStaffViewer 1.16.2" `
  --notes-file RELEASE_NOTES.md
```

> 本專案不提供 MSIX／安裝程式，Release 只附上兩種架構的 zip。

---

## 8. 常見問題

| 問題 | 處理方式 |
| --- | --- |
| 建置時找不到 Windows App SDK | 確認已安裝 .NET 10 SDK 與 Windows App SDK 元件，並執行 `dotnet restore` |
| ARM64 版缺少 `libalphaskia.dll` | 確認 `AlphaSkia`／`AlphaSkia.Native.Windows` 為 4.0.157 以上，且 `RuntimeIdentifier` 為 `win-arm64` |
| 使用者啟動時缺 .NET | 安裝 .NET 10 Desktop Runtime（x64 或 ARM64） |
| zip 解壓後無法啟動 | 確認沒有多包一層資料夾、執行檔與原生 DLL 在同一層 |
| Release 附件過大 | 移除 PDB、確認未包含 `obj/`、`bin/` 或其他暫存檔 |
| 推送被拒 | 確認目前分支與遠端分支一致，必要時先 `git pull --rebase` |

