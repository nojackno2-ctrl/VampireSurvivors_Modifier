# VSModifier

Vampire Survivors 的 C# WPF 存檔修改器與外部記憶體 Trainer。

專案遵守一項硬性原則：**不修改或新增遊戲安裝資料夾內的任何檔案**。

原始碼：[nojackno2-ctrl/VampireSurvivors_Modifier](https://github.com/nojackno2-ctrl/VampireSurvivors_Modifier)

---

## ⚠️ 開發中，目前不可使用

**本專案仍在開發階段，尚未釋出任何可用版本，請勿當成完成品下載使用。**

- 沒有提供正式發行檔（Release），也沒有安裝程式。
- 功能仍在變動，介面、設定與資料格式都可能在未通知的情況下改變。
- Trainer（記憶體功能）在對應遊戲版本完成實機驗證前，一律拒絕附加，因此在多數情況下**不會有任何作用**。
- 存檔修改流程雖已實作備份與檢查機制，但整體尚未經過足夠的長期測試，仍可能造成存檔異常。

如果你只是想要一個能直接使用的工具，請等待本專案正式標示為可用之後再回來。若你想協助測試或研究，請務必先自行備份完整存檔，並自行承擔風險。

---

## 這個專案在做什麼

- **存檔修改**：讀取本機 Vampire Survivors 存檔，修改金幣、解鎖狀態、角色金蛋屬性等內容後安全寫回。
- **外部記憶體 Trainer**：在遊戲執行中提供無敵、傷害倍率、遊戲速率、快速寶箱等即時功能，只使用外部行程記憶體讀寫，不注入 DLL。

### 存檔功能

- 各項旗標與清單編輯：`UnlockedCharacters`、`UnlockedWeapons`、`UnlockedStages`、`UnlockedArcanas`、`UnlockedHypers`、`Achievements`、`Secrets` 等。
- 一鍵套用對應遊戲版本的解鎖表，採安全合併：保留既有順序、重複等級與未收錄項目，只追加缺少的內容。
- 金蛋（Golden Eggs）可針對角色逐一調整單一屬性：攻擊力、護甲、最大生命、冷卻、範圍、投射物數量、幸運、詛咒、移動速度等皆有中文選項；新版遊戲新增的數值屬性也會自動出現在選單。每次只更動所選屬性，其他加成維持原值，衍生的 `total` 會自動重算。

### Trainer 功能

- 無敵、快速寶箱動畫、寶箱最高獎勵、傷害倍率、遊戲速率等。
- 全域熱鍵預設停用，需在 Trainer 頁手動啟用；啟用後以 `Ctrl+Shift+F1` 至 `F5` 切換各項功能，`Ctrl+Shift+F12` 為緊急中斷並還原。
- 功能熱鍵只有在版本已辨識、確認為單人狀態並安全附加後才會執行。

## 安全原則

- 存檔寫入前自動建立完整歷史備份。
- 遊戲執行中拒絕寫入存檔。
- 寫回採 UTF-8 無 BOM，並重算已驗證的 SHA-256 checksum。
- Trainer 僅使用外部行程記憶體讀寫，不注入 DLL。
- 附加後以 100ms 間隔持續監控線上狀態；偵測到線上會話或任一背景寫入失敗時，會自動停止所有鎖值、還原全部數值與 code patch，並中斷 Trainer。
- 僅限單機與本地遊玩使用；對成就與遊戲內 `CheatCodeUsed` 狀態造成的影響由使用者自行承擔。

## 遊戲版本對應

Trainer 不假設不同遊戲版本使用相同記憶體偏移。啟動時會同時計算 `GameAssembly.dll`、`UnityPlayer.dll` 與 `global-metadata.dat` 的 SHA-256，三者必須精確符合同一個版本 Profile，才會採用該 Profile 專屬的資料；載入時另會驗證特徵碼、位址範圍與 patch 長度。未知版本、混搭檔案、結構錯誤或尚未完成實機驗證的 Profile 一律拒絕附加。

本專案以目前 Steam 最新版為基準並持續跟進更新；不為缺少原始遊戲檔案、無法驗證的舊版本建立推測性 Profile。遊戲改版後，該版本必須重新分析並逐項驗證，不會沿用上一版的結論——這也是新版遊戲推出後 Trainer 會有一段時間無法使用的原因。

## 從原始碼建置

需要支援 .NET 10 的開發環境。

1. 使用 Visual Studio 開啟 `VSModifier.sln`（新版 Visual Studio 亦可開啟 `VSModifier.slnx`）。
2. 將 `VSModifier.App` 設為啟始專案。
3. 選擇 x64 或 Any CPU 後建置並執行。

命令列：

```powershell
dotnet build VSModifier.sln
dotnet run --project VSModifier.Tests
```

建置產物為 framework-dependent，執行的電腦必須安裝 .NET 10 Desktop Runtime。

## 版權與隱私

本專案不包含 poncle 的遊戲檔案、metadata、逆向工程輸出、遊戲素材或任何個人存檔，亦與 poncle 無關。

本專案程式碼採 [MIT License](LICENSE) 授權。
