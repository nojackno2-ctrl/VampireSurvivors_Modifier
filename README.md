# VSModifier

> 專為 **Vampire Survivors（吸血鬼倖存者）** 設計的 C# WPF 安全存檔修改器。
> v1.0.0 不會修改遊戲安裝目錄；Trainer 僅保留為停用且未驗證的開發預覽。

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D6?logo=windows)](https://www.microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![GitHub Repository](https://img.shields.io/badge/GitHub-VSModifier-181717?logo=github)](https://github.com/nojackno2-ctrl/VampireSurvivors_Modifier)

---

## 📖 專案簡介

**VSModifier** 是一款採用 .NET 10 與 WPF 技術打造、以備份與拒絕危險寫入為核心的《吸血鬼倖存者》存檔修改工具。

> [!IMPORTANT]
> **v1.0.0 的正式可用範圍是安全存檔編輯器。** 隨附的所有 Trainer Profile 都是 `verified: false`，正式版會 fail-closed 拒絕附加。此版本沒有 Trainer 功能或遊戲內效果的實機驗證聲明。

本專案保留兩條開發軌道：
1. **軌道 A — 安全存檔修改器（v1.0.0 正式支援）**：離線解析與編輯本機 `SaveData`，自動處理 SHA-256 校驗碼、防衝突備份與安全解鎖合併。
2. **軌道 B — 外部記憶體 Trainer（v1.0.0 停用）**：程式碼採外部行程讀寫與可逆記憶體修補設計，但目前 Profile 未驗證，正式版不允許附加。

---

## ✨ 核心功能亮點

### 🗃️ 軌道 A：存檔修改器 (Save Editor)
- **自動偵測存檔**：智慧掃描本機 Steam 儲存目錄，自動列出所有可用的 `SaveData` 存檔候選。
- **全方位資源調整**：支援修改金幣（`Coins`）、生涯累積金幣（`LifetimeCoins`）、封印數（`Seals`）、冒險之星（`AdventureStars`）等數值，並提供安全範圍限制與「一鍵最大化」。
- **版本化安全一鍵解鎖**：
  - 支援角色（`UnlockedCharacters`）、武器（`UnlockedWeapons`）、關卡（`UnlockedStages`）、卡牌（`UnlockedArcanas`）、超級模式（`UnlockedHypers`）、成就（`Achievements`）與秘密咒語（`Secrets`）等。
  - **原子安全合併演算法**：智慧比對版本解鎖表，僅追加缺少的項目，完整保留使用者原有的排序順序、重複升級等級（PowerUp Ranks）與未收錄自訂資料。
- **金蛋（Golden Eggs）精準客製化**：
  - 針對各角色個別調整單一屬性：攻擊力、護甲、最大生命、冷卻縮減、投射物數量、攻擊範圍、幸運、詛咒、移動速度等 20+ 種屬性（具備完整繁體中文選單）。
  - **動態屬性適配**：若未來遊戲版本新增屬性，將自動從存檔中動態讀取並呈現於介面。
  - **數值一致性**：每次修改單一屬性時，其餘加成不受影響，並自動重新精確計算衍生蛋總數（`total`）。
- **遊戲旗標管理**：
  - 檢視與一鍵重設官方作弊標記（`CheatCodeUsed`）。
  - 啟用內建「常駐快速寶箱動畫」（`AlwaysQuickTreasureAnim`）。
- **雙模式進階 JSON 編輯器**：
  - **樹狀結構檢視**：視覺化展開 150+ 個存檔頂層欄位與子節點，點選即可修改純量資料。
  - **原始 JSON 檢視**：支援直接查看與進階編輯，儲存時自動格式化並校驗。

### ⚡ 軌道 B：外部即時 Trainer（停用的開發預覽）

以下是保留於程式碼中的設計範圍，不是 v1.0.0 可用功能清單。所有隨附 Profile 均為 `verified: false`，正式版附加會被拒絕。
- **零 DLL 注入**：僅透過 Windows 原生 API 進行外部記憶體讀寫與 RAM 內部程式碼修補（Code Patch），關閉遊戲或中斷附加後立即完全恢復原始狀態，不殘留任何痕跡。
- **戰鬥與生存數值鎖定**：
  - 永久無敵（`Permanent Invulnerability`）
  - 傷害倍率（Power Multiplier，支援 1x ~ 1000x 自訂滑桿）
  - 冷卻縮減、投射物數量加成、攻擊範圍擴大、持續時間、移動速度、磁吸全圖吸取、復活次數、重新骰（Reroll/Skip/Banish）次數等。
- **遊戲系統與進程調控**：
  - **遊戲速率（Game Speed）**：精準調控 Unity `Time.timeScale`（0.1x ~ 10x），相容遊戲原生暫停機制。
  - **寶箱最高獎勵（Max Treasure Chest）**：原子複合 Patch，強制五件裝備最高獎勵與金幣結算上限。
  - **快速開箱動畫（Quick Treasure Anim）**：免等待直接跳過冗長開箱動畫。
  - **局內金幣與等級即時調整**：本局金幣、角色等級與經驗值單次寫入與唯讀即時監控。
- **進階可逆 Code Cave Hook**：
  - **升級指定獲得道具（Force Level-Up Item）**：內建 85+ 項升級道具/武器清單，升級時保證抽出所選目標。
  - **複製下次取得武器/飾品（Duplicate Next Weapon / Accessory）**：突破常規道具數量上限。
- **全域熱鍵系統**：
  - 支援背景全域快捷鍵操作（預設停用，需手動開啟）。
  - 提供 `Ctrl+Shift+F1` ~ `F5` 快捷開關各項主力功能。
  - 🚨 **`Ctrl+Shift+F12` 緊急中斷與全功能還原**：一鍵立即解除所有鎖值、還原 Code Cave 與 Patch。

---

## 🛡️ 安全防護與設計承諾

本專案在架構層面嚴格實施以下安全規範，確保使用者帳號與資料安全：

| 防護機制 | 實作說明 |
|---|---|
| **不修改遊戲安裝目錄** | 絕對不修改、替換或新增遊戲安裝目錄內的任何檔案（如 `GameAssembly.dll`、exe 等）。 |
| **自動歷史備份** | 每次寫入存檔前，皆自動將原始檔案備份至 `%LOCALAPPDATA%\VSModifier\backups`（保留時間戳記）。 |
| **遊戲執行防衝突保護** | 修改存檔時，若偵測到遊戲正在執行中，會**嚴格拒絕寫入**，防止存檔被遊戲結束時覆蓋損毀。 |
| **SHA-256 校驗碼重算** | 寫回存檔時採用 UTF-8 無 BOM 編碼，並精準計算遊戲專屬 64 字元 SHA-256 Checksum，確保遊戲讀取無誤。 |
| **三檔指紋雜湊驗證** | Trainer 啟動時必須同時精準匹配 `GameAssembly.dll`、`UnityPlayer.dll` 與 `global-metadata.dat` 的 SHA-256，未經驗證或混搭檔案一律拒絕附加。 |
| **100ms 線上防護 (Fail-Closed)** | 附加後每 100ms 持續監控連線會話狀態；若偵測為線上多人模式或任何寫入失敗，立即還原所有變更並強制離線中斷。 |
| **版本資料熱重新載入** | 修改器即時監聽 `data/offsets.json` 變動，支援免重啟更新特徵碼與偏移量。 |

---

## 🚀 快速上手使用說明

### 系統需求
- **作業系統**：Windows 10 / Windows 11 (64-bit x64)
- **執行環境**：官方 portable ZIP 已包含所需的 .NET 執行環境，不需另行安裝 .NET Desktop Runtime。
- **遊戲版本**：Steam 正版 Vampire Survivors (Unity IL2CPP x64)

---

### 步驟一：修改存檔 (Save Editing)

> [!IMPORTANT]
> **最佳實踐流程（避免 Steam 雲端存檔覆蓋）**：
> 1. 請保持 **Steam 用戶端開啟**，但 **關閉 Vampire Survivors 遊戲**。
> 2. 啟動 `VSModifier.App.exe`。
> 3. 程式會自動偵測存檔路徑（若有多個帳號可在下拉選單切換，或點擊「瀏覽」手動選取 `SaveData`）。
> 4. 在各分頁進行調整：
>    - **資源**：調整金幣或點擊「最大化常用資源」。
>    - **解鎖**：點擊「一鍵合併解鎖表」或個別勾選。
>    - **蛋**：選擇角色與屬性，輸入數值後點擊「套用屬性蛋」。
>    - **旗標**：重設作弊標記或啟用快速開箱。
>    - **進階 JSON**：檢視或自訂修改特定欄位。
> 5. 點擊「儲存修改」。狀態列顯示「儲存成功」後即可啟動遊戲，Steam 雲端會自動同步最新修改。

---

### Trainer 狀態

v1.0.0 不提供可操作的 Trainer。所有隨附 Profile 均保持 `verified: false`，因此正式版會以 fail-closed 模式拒絕附加。待未來完成特定遊戲版本的離線實機驗證後，才會另行說明可用範圍；目前請只使用存檔編輯功能。

---

## 🛠️ 遊戲改版與版本維護

當 Vampire Survivors 在 Steam 發布更新時：
1. **存檔修改器**：通常完全不受遊戲更新影響，可直接正常使用。
2. **Trainer 記憶體功能**：由於遊戲代碼編譯偏移（RVA）可能更動，修改器會依「三檔精確指紋」啟動防護，暫時拒絕附加，避免造成遊戲崩潰。
3. **更新偏移量**：
   - 專案會在 `data/offsets.json` 與 `data/ids/unlocks.json` 提供新版 Profile。
   - 使用者只需將更新後的 JSON 檔案覆蓋至 `data/` 目錄，修改器會自動熱重新載入（Hot Reload），無須重新編譯主程式。

---

## 💻 從原始碼建置 (Build from Source)

如果你希望自行編譯或參與開發：

### 必備環境
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022 (v17.12+) 或支援 .NET 10 的 IDE / CLI。

### 建置步驟

```powershell
# 複製儲存庫
git clone https://github.com/nojackno2-ctrl/VampireSurvivors_Modifier.git
cd VampireSurvivors_Modifier

# 編譯整個解決方案
dotnet build VSModifier.sln --configuration Release

# 執行自動化測試（24 項完整測試）
dotnet run --project VSModifier.Tests --configuration Release
```

### 發行獨立應用程式 (Publish)
使用與 CI / GitHub Release 相同的腳本建立 self-contained `win-x64` portable ZIP、驗證檔案版本、執行內容黑名單稽核並產生 `SHA256SUMS`：
```powershell
./scripts/build-release.ps1
```

---

## 📂 專案架構概覽

```text
VampireSurvivors_Modifier/
├── VSModifier.App/        # WPF 使用者介面、深色主題、全域熱鍵管理
├── VSModifier.Core/       # 存檔解析、SHA-256 校驗碼、安全備份、進程偵測
├── VSModifier.Memory/     # 外部進程記憶體讀寫、AOB 掃描、Code Cave Hook、線上防護
├── VSModifier.Tests/      # 自動化單元測試與安全回歸測試套件
├── VSModifier.IdExtractor/# 輔助工具：自 dump 資料抽取各版本 ID 與解鎖表
├── data/
│   ├── offsets.json       # 各遊戲版本記憶體偏移與指紋資料庫
│   └── ids/               # 解鎖表與升級強制道具 ID 清單
├── DESIGN.md              # 系統完整設計規格書
├── AGENTS.md              # AI 代理協作規則與安全規範
└── AI_HANDOFF.md          # 實作進度、工程紀錄與即時交接狀態
```

---

## ❓ 常見問題 (FAQ)

<details>
<summary><b>Q1: 為什麼修改存檔後進入遊戲沒有看到變更？</b></summary>
請確認修改存檔時「遊戲是否已完全關閉」。若遊戲在開啟狀態下修改，當您關閉遊戲時，遊戲會將記憶體中的舊存檔直接覆蓋寫入硬碟，導致修改失效。請遵循「關閉遊戲 → 修改並儲存 → 開啟遊戲」的流程。
</details>

<details>
<summary><b>Q2: 使用 Trainer 會導致 Steam 封號嗎？</b></summary>
v1.0.0 的 Trainer 已停用且未經遊戲內效果驗證，因此本版本不提供 Trainer 使用或帳號風險保證。請只使用安全存檔編輯功能，並自行評估修改存檔對成就與帳號資料的影響。
</details>

<details>
<summary><b>Q3: 為什麼 Trainer 顯示「尚未完成單人關卡實機驗證」或無法點擊附加？</b></summary>
這是 v1.0.0 的預期安全行為。所有隨附 Profile 都是 `verified: false`；在完成特定遊戲版本的單人離線實機驗證前，系統會保持 fail-closed 並拒絕附加。
</details>

<details>
<summary><b>Q4: 修改器會修改我的遊戲本體檔案嗎？</b></summary>
絕對不會。本專案不論是存檔修改還是 Trainer 記憶體修改，皆 100% 遵守「零遊戲檔案接觸」原則，不會覆蓋或在遊戲目錄建立任何 DLL 或資料檔。
</details>

---

## 📜 免責聲明與授權條款 (Disclaimer & License)

- **免責聲明**：
  - 本工具僅供個人離線娛樂、學習與研究使用。
  - 使用存檔修改或記憶體功能可能影響遊戲內的成就解鎖或產生 `CheatCodeUsed` 標記，相關風險由使用者自行承擔。
  - 本專案不包含任何由 **poncle** 擁有之版權遊戲檔案、二進位組件、遊戲素材或逆向工程原始產出，亦與 poncle 無任何官方附屬或背書關係。
- **授權條款**：
  - 本專案程式碼依據 [MIT License](LICENSE) 授權條款開放。

---

## 🤝 參與貢獻與 AI 協作

本專案採用結構化 AI 協作與工程交接規範：
- 規格定義請參閱 [DESIGN.md](DESIGN.md)。
- 跨代理協作規則請參閱 [AGENTS.md](AGENTS.md)。
- 即時狀態與工程驗證紀錄請參閱 [AI_HANDOFF.md](AI_HANDOFF.md)。

歡迎提交 Issue 與 Pull Request！
