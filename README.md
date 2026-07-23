# VSModifier

Vampire Survivors 的 C# WPF 存檔修改器與外部記憶體 Trainer。專案遵守一項硬性原則：**不修改或新增遊戲安裝資料夾內的任何檔案**。

公開原始碼：[nojackno2-ctrl/VampireSurvivors_Modifier](https://github.com/nojackno2-ctrl/VampireSurvivors_Modifier)

> 目前正在依 `DESIGN.md` 開發。存檔 checksum 算法已使用本機實際 SaveData 唯讀驗證；Trainer 功能在版本偏移與實機行為完成驗證前不會標示為可用。

## 使用 Visual Studio

1. 使用支援 .NET 10 的 Visual Studio 開啟 `VSModifier.sln`（新版 Visual Studio 亦可開啟 `VSModifier.slnx`）。
2. 將 `VSModifier.App` 設為啟始專案。
3. 選擇 x64 或 Any CPU 後建置並執行。

需要建立可散布資料夾時，在 `VSModifier.App` 上按右鍵選擇「發佈」，使用內建的 `win-x64-folder` Profile。此設定為 Release、x64、framework-dependent，因此目標電腦必須安裝 .NET 10 Desktop Runtime；發布目錄會包含 `README.md`、`LICENSE` 與版本化 data，並排除 PDB。Trainer Profile 尚未完成實機驗證時不得把整體 Trainer 宣稱為可用。

命令列驗證：

```powershell
dotnet build VSModifier.sln
dotnet run --project VSModifier.Tests
dotnet publish VSModifier.App -p:PublishProfile=win-x64-folder
```

## AI 代理接手

其他 AI 代理開始工作前必須先讀取 [AGENTS.md](AGENTS.md) 與 [AI_HANDOFF.md](AI_HANDOFF.md)，再檢查目前分支、`git status`、`git diff` 與近期 Commit。`AI_HANDOFF.md` 是即時專案記憶，重要修改、失敗嘗試、實機證據與下一步都必須立即更新。

## 安全原則

- 存檔寫入前自動建立完整歷史備份。
- 遊戲執行中拒絕寫入存檔。
- 寫回採 UTF-8 無 BOM，並重算已驗證的 SHA-256 checksum。
- Trainer 僅使用外部行程記憶體讀寫，不注入 DLL。
- 附加後以 100ms 間隔持續監控線上狀態；偵測線上會話或任一背景寫入失敗時，自動停止全部鎖值、還原所有值與 code patch，並中斷 Trainer。
- 全域熱鍵預設停用，必須在 Trainer 頁手動啟用；`Ctrl+Shift+F12` 會緊急中斷 Trainer 並嘗試還原所有原始值。
- 僅限單機與本地遊玩使用；成就與遊戲內 `CheatCodeUsed` 狀態的影響由使用者自行承擔。

金蛋頁可針對角色逐一修改每個獨立屬性；攻擊、防禦、生命、冷卻、範圍等已知屬性都有中文選項，實際存檔中由新版遊戲新增的數值屬性也會自動加入選單。每次只改所選屬性，其他加成保持原值；衍生的 `total` 會自動重算，不提供手動破壞一致性的入口。

## Trainer 全域熱鍵

啟用後使用 `Ctrl+Shift+F1` 至 `F5` 切換無敵、快速寶箱、最高寶箱、傷害倍率與遊戲速率；`Ctrl+Shift+F12` 為緊急中斷並還原。功能熱鍵只有在已辨識版本、確認單人狀態並安全附加後才會執行。

## 遊戲更新與資料抽取

Trainer 不假設不同遊戲版本使用相同偏移。啟動時會同時計算 `GameAssembly.dll`、`UnityPlayer.dll` 與 `global-metadata.dat` 的 SHA-256，三者必須精確符合 `data/offsets.json` 內同一個版本 Profile，才會採用該 Profile 專屬的指標鏈、AOB、原始位元組與 patch。Profile 載入時還會驗證 AOB、RIP-relative 範圍、patch 等長性與必要功能集合；未知版本、混搭檔案、結構錯誤或尚未完成實機驗證的 Profile 一律拒絕附加。

本專案以目前 Steam 最新版為基準，之後隨遊戲最新版持續更新；不為缺少原始遊戲檔案、無法驗證的舊版本建立推測性 Profile。`offsets.json` 可並列目前與未來版本的 Profile，因此遊戲更新時不必改動 Trainer 核心架構；但每一版都必須重新抽取、分析並逐項驗證，不會沿用上一版的安全結論。遊戲改版後，先用 Il2CppDumper 對自己的 `GameAssembly.dll` 與 `global-metadata.dat` 產生 `dump.cs`。完整 dump 不得加入 repo；只可執行：

```powershell
dotnet run --project VSModifier.IdExtractor -- <dump.cs> steam-current-YYYY-MM-DD data\ids\unlocks.json
```

工具會把角色、武器、關卡、Arcana、成就等事實性 ID 寫入指定 `profileId`，並保留其他版本資料。一鍵全解鎖只會在三檔指紋命中同一個遊戲 Profile 時使用對應 ID 表，而且採安全合併：保留既有順序、重複等級與未收錄 ID，只追加缺少項目。PowerUp 陣列以重複 ID 表示等級，抽取工具刻意不產生這兩欄；皮膚的 `UnlockedSkins`／`UnlockedSkinsV2` 是 dictionary，不會誤當陣列覆寫，可由進階 JSON 編輯器處理。Trainer 偏移仍須另行分析並逐項實機驗證；完成後新增一筆帶有三檔雜湊的 Profile，不能只替換舊版本的雜湊。

### Trainer 開發驗證工具

未驗證 Profile 永遠不能由正式 WPF 附加。偏移維護者必須先進入單人關卡，執行唯讀診斷並確認 `onlineSession` 成功解析且值為 `0`：

```powershell
dotnet run --project VSModifier.Tests -- --inspect-trainer-read-only
```

只有在上述條件成立後，才可使用下列開發專用命令逐項驗證。每次必須明確輸入目前三檔指紋命中的 `profile-id`，只允許一個功能，持續時間限制為 100–5000ms；工具全程每 100ms 檢查線上 guard，並在 `finally`／工作階段釋放時還原及讀回驗證原始值或原始程式碼位元組。這些命令不會出現在正式 WPF：

```powershell
dotnet run --project VSModifier.Tests -- --verify-trainer-value <profile-id> <feature-key> <set|multiply|add> <value> <duration-ms>
dotnet run --project VSModifier.Tests -- --verify-trainer-patch <profile-id> <feature-key> <duration-ms>
```

實際遊戲效果仍須由測試者觀察並記錄；單純寫入與還原成功不能自動把 Profile 改成 `verified: true`。最高寶箱 patch 必須另以實際開箱及遊戲 log 證明。

## 版權與隱私

本專案不包含 poncle 的遊戲檔案、metadata、Il2CppDumper 完整輸出、遊戲素材或個人 SaveData，亦與 poncle 無關。

本專案程式碼採 [MIT License](LICENSE) 授權。
