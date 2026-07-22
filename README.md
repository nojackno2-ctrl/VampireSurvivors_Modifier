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
- 全域熱鍵預設停用，必須在 Trainer 頁手動啟用；`Ctrl+Shift+F12` 會緊急中斷 Trainer 並嘗試還原所有原始值。
- 僅限單機與本地遊玩使用；成就與遊戲內 `CheatCodeUsed` 狀態的影響由使用者自行承擔。

## Trainer 全域熱鍵

啟用後使用 `Ctrl+Shift+F1` 至 `F5` 切換無敵、快速寶箱、最高寶箱、傷害倍率與遊戲速率；`Ctrl+Shift+F12` 為緊急中斷並還原。功能熱鍵只有在已辨識版本、確認單人狀態並安全附加後才會執行。

## 遊戲更新與資料抽取

Trainer 不假設不同遊戲版本使用相同偏移。啟動時會同時計算 `GameAssembly.dll`、`UnityPlayer.dll` 與 `global-metadata.dat` 的 SHA-256，三者必須精確符合 `data/offsets.json` 內同一個版本 Profile，才會採用該 Profile 專屬的指標鏈、AOB、原始位元組與 patch。未知版本、混搭檔案或尚未完成實機驗證的 Profile 一律拒絕附加。

本專案以目前 Steam 最新版為基準，之後隨遊戲最新版持續更新；不為缺少原始遊戲檔案、無法驗證的舊版本建立推測性 Profile。`offsets.json` 可並列目前與未來版本的 Profile，因此遊戲更新時不必改動 Trainer 核心架構；但每一版都必須重新抽取、分析並逐項驗證，不會沿用上一版的安全結論。遊戲改版後，先用 Il2CppDumper 對自己的 `GameAssembly.dll` 與 `global-metadata.dat` 產生 `dump.cs`。完整 dump 不得加入 repo；只可執行：

```powershell
dotnet run --project VSModifier.IdExtractor -- <dump.cs> data\ids\unlocks.json
```

工具會抽出版本對應的角色、武器、關卡、Arcana、成就等事實性 ID。PowerUp 陣列以重複 ID 表示等級，抽取工具刻意不覆蓋，避免「全解鎖」反而降低既有等級。Trainer 偏移仍須另行分析並逐項實機驗證；完成後新增一筆帶有三檔雜湊的 Profile，不能只替換舊版本的雜湊。

## 版權與隱私

本專案不包含 poncle 的遊戲檔案、metadata、Il2CppDumper 完整輸出、遊戲素材或個人 SaveData，亦與 poncle 無關。

本專案程式碼採 [MIT License](LICENSE) 授權。
