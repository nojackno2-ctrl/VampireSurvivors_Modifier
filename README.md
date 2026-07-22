# VSModifier

Vampire Survivors 的 C# WPF 存檔修改器與外部記憶體 Trainer。專案遵守一項硬性原則：**不修改或新增遊戲安裝資料夾內的任何檔案**。

> 目前正在依 `DESIGN.md` 開發。存檔 checksum 算法已使用本機實際 SaveData 唯讀驗證；Trainer 功能在版本偏移與實機行為完成驗證前不會標示為可用。

## 使用 Visual Studio

1. 使用支援 .NET 10 的 Visual Studio 開啟 `VSModifier.sln`（新版 Visual Studio 亦可開啟 `VSModifier.slnx`）。
2. 將 `VSModifier.App` 設為啟始專案。
3. 選擇 x64 或 Any CPU 後建置並執行。

命令列驗證：

```powershell
dotnet build VSModifier.sln
dotnet run --project VSModifier.Tests
```

## 安全原則

- 存檔寫入前自動建立完整歷史備份。
- 遊戲執行中拒絕寫入存檔。
- 寫回採 UTF-8 無 BOM，並重算已驗證的 SHA-256 checksum。
- Trainer 僅使用外部行程記憶體讀寫，不注入 DLL。
- 線上模式必須自動停用記憶體功能。
- 僅限單機與本地遊玩使用；成就與遊戲內 `CheatCodeUsed` 狀態的影響由使用者自行承擔。

## 版權與隱私

本專案不包含 poncle 的遊戲檔案、metadata、Il2CppDumper 完整輸出、遊戲素材或個人 SaveData，亦與 poncle 無關。
