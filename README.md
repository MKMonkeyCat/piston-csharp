# Piston Csharp

繁體中文說明文件。

專案網址: https://github.com/MKMonkeyCat/piston-csharp.git
（複製並以 `git clone` 取得原始碼）

## 簡介

這是 piston-csharp 倉庫，包含範例應用 `MyApp`、核心函式庫 `Piston.Core` 及其單元測試專案 `Piston.Core.Tests`。專案示範了一個用於程式碼執行/包裝的客戶端庫與相關測試資料。

## 倉庫主要結構

- `MyApp/` — 範例控制台程式（可執行範例）
- `Piston.Core/` — 核心函式庫實作
- `Piston.Core.Tests/` — 單元測試
- `LICENSE` — 授權說明

## 先決條件

- 已安裝 .NET SDK（建議使用 .NET 6.0 或更新版本）。

## 建置與測試

在倉庫根目錄執行：

```bash
dotnet build EngineerMan.Piston.slnx
dotnet test
```

執行範例應用：

```bash
dotnet run --project MyApp
```

單獨執行測試專案：

```bash
dotnet test ./Piston.Core.Tests/Piston.Core.Tests.csproj
```

## 貢獻

歡迎開 issue 或提出 pull request。請在提交前確保相關單元測試通過。

## 授權

請參閱倉庫根目錄的 LICENSE 檔案。
