# Vampire Survivors 修改器 — 完整設計規格書

> 原則：**完全不修改遊戲本體** —— 不改動、不新增遊戲安裝資料夾內的任何檔案。
> 本文件為交付給實作 AI 的規格書。實作語言：**C#**。
> 文中「已確認」= 已在本機遊戲檔案 / 存檔中實際驗證過的事實（2026-07-22）。

---

## 1. 環境事實（已確認）

| 項目 | 值 |
|---|---|
| 遊戲版本 | Unity **IL2CPP**（x64） |
| 遊戲主程式 | `C:\Program Files (x86)\Steam\steamapps\common\Vampire Survivors\VampireSurvivors.exe`（行程名 `VampireSurvivors`） |
| 程式碼本體 | 同資料夾 `GameAssembly.dll`（174,786,048 bytes） |
| IL2CPP metadata | `VampireSurvivors_Data\il2cpp_data\Metadata\global-metadata.dat`（49,067,656 bytes，**未加密**，可直接餵給 Il2CppDumper） |
| 存檔（實際生效） | `%SteamInstall%\userdata\<SteamUserId>\1794680\remote\SaveData`（實際帳號目錄已在本機驗證，repo 不記錄個人 ID） |
| 存檔格式 | **未加密純 JSON**，含 `checksum` 欄位（64 字元小寫 hex，SHA-256） |
| 反作弊 | 無。metadata 中甚至存在 `Debug_ToggleInvulnerability`（官方除錯無敵開關） |
| Steam AppID | 1794680 |

---

## 2. 軌道 A — 存檔修改器（C# WPF）

### 2.1 目標
「全部能改的都改」：提供 (a) 分類過的常用功能頁籤，(b) 通用 JSON 樹狀編輯器兜底，保證任何欄位都可改。

### 2.2 存檔欄位總表（已確認，自實際存檔枚舉）

**資源類**
- `Coins` (double)、`LifetimeCoins`、`TotalCoins` — 金幣（目前值 181,888,500,000，可知遊戲接受極大值）
- `Seals` (int)、`AdventureStars` (decimal)

**解鎖類（皆為字串 ID 陣列）**
- `BoughtCharacters`[28]、`UnlockedCharacters`[36]（ID 例：`ANTONIO`, `IMELDA`, `PASQUALINA`）
- `UnlockedWeapons`[167]、`CollectedWeapons`[99]（ID 例：`WHIP`, `VAMPIRICA`, `MAGIC_MISSILE`, `KNIFE`, `AXE`）
- `BoughtPowerups`[87]、`UnlockedPowerUpRanks`[12]、`CollectedItems`[43]
- `UnlockedStages`[24]、`UnlockedHypers`[18]、`UnlockedArcanas`[27]
- `Achievements`[153]（ID 例：`Survive1Minute`, `Defeat5000Enemies`）、`Secrets`[34]
- `UnlockedSkins` / `UnlockedSkinsV2` / `BoughtSkins`[50]、`OpenedCoffins`[8]
- 完整 ID 清單來源：Il2CppDumper 產出的 `dump.cs` 中的 enum（`CharacterType`、`WeaponType`、`StageType`、`AchievementType` 等），實作時從 dump 抽出做成資料表

**蛋（EggData，object）**
- 每角色一個 dict，鍵為屬性名：`magnet, armor, maxHp, amount, moveSpeed, curse, revivals, luck, duration, rerolls, regen, speed, banish, area, cooldown, skips, power, ...` + `total`（蛋總數）
- 修改屬性值時應同步維護 `total` 的一致性

**統計 / 進度**
- `KillCount`、`PickupCount`、`DestroyedCount`、`StageCompletionLog`、`CharacterStageData`、`CharacterEnemiesKilled`、`CharacterSurvivedMinutes`（皆 object）
- `LifetimeSurvived`、`LifetimeHeal`、`CompletedHurries`、`HighestFever`、`LongestFever` 等

**旗標**
- `CheatCodeUsed` (bool) — 遊戲自己的作弊標記，UI 上應顯示並允許使用者重設
- `HasKilledTheFinalBoss`、`Didit`、各種 `HasSeen*` / `HasUsed*`
- **`AlwaysQuickTreasureAnim` (bool)** — 內建「快速寶箱動畫」設定（見 §3.5）
- `SequentialChestMode` (bool)

**設定類**（音量、語言、顯示…約 40 個欄位）— 由通用 JSON 編輯器覆蓋即可，不必做專屬 UI。

### 2.3 checksum 演算法（實作時必須先驗證）

- 存檔中 `checksum` = 64 字元小寫 hex（SHA-256）。
- metadata 中已確認存在：`GenerateChecksum`、`UpdateChecksum`、`ChecksumIsValid`、`DataChecksumError`、`NoChecksum`、`ReadOnlyWithChecksum`，以及一個 regex 樣式字串 `"checksum":"[a-z0-9]*"` — 強烈暗示演算法為：**以 regex 把 JSON 中 checksum 的值清空（或整段替換）後，對剩餘字串取 SHA-256**。
- 實作步驟：
  1. 在 `dump.cs` 搜 `GenerateChecksum` 找到所屬類別，確認雜湊輸入（是否有 salt、清空方式是值清空還是欄位移除、編碼）
  2. 驗證法：讀現有存檔 → 依演算法重算 → 應等於檔內 checksum
  3. 兜底：實測遊戲對錯誤 checksum 的行為（可能僅記 `DataChecksumError` 不拒讀）；但正式版一律寫入正確 checksum

### 2.4 安全機制（必做）

1. 寫入前自動備份：`backups\SaveData_yyyyMMdd_HHmmss.json`，保留全部歷史
2. 偵測 `VampireSurvivors` 行程，執行中**拒絕寫入**（遊戲退出時會覆蓋存檔）
3. Steam Cloud：修改流程 =「Steam 開著、遊戲關閉」時改 → 下次啟動遊戲 Steam 以本地較新檔上傳。UI 中放操作說明。`%APPDATA%\Vampire_Survivors\saves\` 只有 `steam_autocloud.vdf`，實際存檔以 Steam userdata 為準（已確認）
4. 寫回時保持 JSON 欄位順序與格式盡量接近原始（降低差異面），編碼 UTF-8 無 BOM

### 2.5 UI 結構建議

- 頁籤：資源 / 角色與武器解鎖 / 成就與秘密 / 蛋編輯器 / 旗標 / 進階（JSON 樹狀編輯器）
- 「一鍵全解鎖」與「一鍵最大化」按鈕須有二次確認
- 底部常駐：目前存檔路徑、遊戲執行狀態燈、最近備份時間

---

## 3. 軌道 B — 記憶體修改器（C# 外部 trainer）

### 3.1 總體架構

- 純外部行程：`OpenProcess` / `ReadProcessMemory` / `WriteProcessMemory`（P/Invoke），**不注入 DLL、不碰遊戲檔案**
- 需要改程式碼行為的功能（§3.4）採**記憶體內 code patch**（`VirtualProtectEx` + `WriteProcessMemory` 寫程式碼頁）— 仍然零檔案接觸，遊戲重啟即還原
- 所有偏移放外部 `offsets.json`，鍵入遊戲版本（以 `GameAssembly.dll` 的檔案雜湊或版本號辨識），遊戲更新只需重新 dump 更新此檔
- 背景執行緒以固定頻率（約 100ms）維持鎖定值（enforce loop）；每個功能獨立開關 + 全域熱鍵

### 3.2 前置分析流程（實作 AI 的第一步）

1. 用 **Il2CppDumper** 處理 `GameAssembly.dll` + `global-metadata.dat` → 產出 `dump.cs`（類別與欄位偏移）、`script.json`（RVA）
2. 外部指標鏈解析模式（IL2CPP 標準做法）：
   `GameAssembly.dll基址 + 類別TypeInfo的RVA` → 讀出 klass 指標 → `klass + static_fields偏移` → 靜態欄位區 → 依 dump.cs 欄位偏移逐層走到目標實例欄位
3. 錨點類別（**以下名稱皆已在本機 metadata 中確認存在**）：
   - `VampireSurvivors.Framework` 的 `GameManager`（全域入口，找其靜態 Instance）
   - `VampireSurvivors.Objects.PlayerOptions`、`VampireSurvivors.Objects.PlayerModifierStats`
   - `TreasureFactory`、`TreasureReelUI`、`TreasurePrizeTypePair`、`OpenTreasurePage`
4. 開發期以 Cheat Engine 驗證指標鏈穩定性後，才固化進 `offsets.json`

### 3.3 功能一：無敵

- 已確認欄位：角色類上有 `_isInvul`、`_isInvulnerable`、`_permanentInvulnerability`、`_shieldInvulTime`、`InvulTimeBonus`；另有官方 `Debug_ToggleInvulnerability`
- **首選：`_permanentInvulnerability` 設 true**（語意即永久無敵），enforce loop 維持
- 指標鏈：GameManager.Instance → 玩家 CharacterController →該欄位
- 備援：找 `Debug_ToggleInvulnerability` 對應的旗標欄位直接寫

### 3.4 功能二：傷害倍率

- 目標：`PlayerModifierStats` 的 `power` 欄位（EggData 亦用同名鍵，屬性體系一致；metadata 已確認 `_power` 存在）
- 注意：實作時先在 dump.cs 確認欄位型別 — 若是包裝型別（如 EggFloat/EggDouble）需多走一層指標取內部值
- UI：倍率滑桿（x1–x1000），寫入 = 原始值 × 倍率；須快取原始值以便還原與重算
- 備援方案：若 stats 會被遊戲每幀重算覆蓋，改為 code patch 傷害計算函式（dump.cs 搜 `TakeDamage` / `GetDamaged`，已確認存在）

### 3.5 功能三：遊戲速率倍率

- 目標：Unity `Time.timeScale`（位於 `UnityPlayer.dll` 的 TimeManager 結構，非 GameAssembly）
- 做法：對 `UnityPlayer.dll` 做 AOB（signature）掃描定位 TimeManager，直接寫 float timeScale；簽名放 `offsets.json`
- 注意：遊戲暫停/恢復會寫 0/1 覆蓋 → enforce loop 需「偵測非 0 時才覆寫為倍率值」，避免破壞暫停
- 備援：Cheat Engine 風格 speedhack（hook 計時 API）需注入，違反原則，不採用

### 3.6 功能四：寶箱最高獎勵

- 已確認錨點：`TreasureFactory`、`_treasurePrizeTypes`、`TreasurePrizeTypePair`、log 字串 `"Treasure PrizeCount = "` 與 `" PrizeType = "`
- 做法（優先序）：
  1. dump.cs 搜 `TreasureFactory` 內決定獎勵數量的方法（會引用 luck 與機率常數），對「獎勵數 roll」做**記憶體 code patch** 強制取最大值（5 件 + 金幣上限）
  2. 替代：若寶箱實例上存在 prize count 欄位，polling 在開箱動畫前改寫該欄位
- 開發驗證：遊戲 log（`%LOCALAPPDATA%Low\poncle\Vampire Survivors\Player.log`）會印 `Treasure PrizeCount = ...`，可直接確認 patch 是否生效

### 3.7 功能五：加速寶箱開啟動畫

- **免記憶體修改即可達成（已確認）**：存檔欄位 `AlwaysQuickTreasureAnim`（目前為 false）即內建快速開箱；metadata 另有 `QuickTreasureTime`、`CanPlayQuickTreasureAnim`、`DisplayQuickTreasureChestAnimation`
- 實作：
  1. 軌道 A 直接提供「永久快速開箱」開關（改存檔欄位）
  2. 軌道 B 亦可於執行期寫 `PlayerOptions` 的對應欄位（`AlwaysQuickTreasureAnim` backing field，metadata 已確認 `<AlwaysQuickTreasureAnim>k__BackingField`）達到免重開即生效
  3. 若要「更快」：`QuickTreasureTime` 屬性值再往下調（記憶體寫入）

### 3.8 追加功能清單（第二波，錨點皆已在 metadata 確認）

`PlayerModifierStats` 對每項屬性都有欄位 + `get_`/`set_` 對，以下全部可用與「傷害倍率」相同的指標鏈直接改值：

| 功能 | 錨點欄位 | 備註 |
|---|---|---|
| 經驗倍率 | `_growth` | 升級速度 |
| 金幣倍率 | `_greed` | 局內金幣獲取 |
| 運氣 | `_luck` | 影響寶箱獎勵數、掉落率（與 §3.6 互補） |
| 冷卻縮減 | `_cooldown`、`_cooldownBonus` | 設極大縮減 ≈ 無冷卻 |
| 移動速度 | `_moveSpeed`、`_moveSpeedPerc` | |
| 攻擊範圍 | `area` | |
| 投射物數量 | `amount` | +N 發 |
| 持續時間 | `duration` | |
| 彈速 | `speed` | |
| 護甲 / 回血 | `armor`、`regen` | |
| 磁吸範圍 | `magnet` | 拉大 ≈ 全圖吸取 |
| 復活次數 | `revivals`（UI：`_revivalsLeftText`） | |
| Reroll / Skip / Banish 次數 | `rerolls`、`skips`、`banish` | |
| 詛咒（敵人密度） | `_curse`、`_curseBonus` | 刷怪練度用，可雙向調 |
| 魅惑 / 拔牙 | `charm`、`defang` | |

進階功能（需遠端呼叫遊戲函式或 code patch，列為選做）：

- **全屏清怪**：`KillAll` 方法（`KillAllControllers`）
- **直接給武器**：`TryGiveWeaponToPlayer`
- **直接加金幣 / 經驗**：`AddCoins`、`AddCoinsFlat`、`AddCoinsNoRun`、`AddXp`
- 呼叫方式：`CreateRemoteThread` 指向 il2cpp 函式（RVA 來自 script.json）。注意 IL2CPP 執行緒需 attach，實作複雜度高且穩定性差，僅在欄位寫入無法達成時才採用；**不使用 DLL 注入**

**內建作弊碼系統（零修改路線）**：metadata 確認存在 `_gameplayCheatCodeManager`、`_gameplayCheats`、`_menuCheats`、`_CheatsPanel`、`_currentEnteredCheat` —— 即遊戲官方的「咒語（spells）」輸入系統，可解鎖角色/關卡/道具。修改器 UI 可附一頁官方咒語清單（自 dump.cs 的 cheat 資料表抽出），這是完全不需要任何修改的合法解鎖途徑，優先推薦給使用者。

**線上模式防護（必做）**：metadata 存在 `_batchedOnlineLevelUpSkips`、`_OnlineCheatsPanel` 等字串，遊戲具備線上連線功能。Trainer 必須偵測線上會話（dump.cs 找 online session 狀態旗標）並在線上模式**自動停用所有記憶體功能**，README 與 UI 皆須聲明「僅限單機 / 本地遊玩使用」。

### 3.9 P/Invoke 清單

`OpenProcess`, `ReadProcessMemory`, `WriteProcessMemory`, `VirtualProtectEx`, `EnumProcessModulesEx`/`Module32First`（取 `GameAssembly.dll`、`UnityPlayer.dll` 基址）, `RegisterHotKey`

---

## 4. 專案結構建議（單一方案，兩軌共用）

```
VSModifier.sln
├─ README.md                // 功能說明、使用方式、免責聲明（僅限單機使用）
├─ LICENSE                  // 建議 MIT
├─ .gitignore
├─ VSModifier.Core          // 存檔 JSON 模型、checksum、備份、行程偵測
├─ VSModifier.Memory        // P/Invoke、模組基址、指標鏈、AOB 掃描、code patch、enforce loop
├─ VSModifier.App           // WPF GUI（存檔頁籤 + Trainer 頁籤 + 熱鍵設定）
└─ data/
   ├─ offsets.json          // 版本化偏移表（軌道 B）
   └─ ids/*.json            // 角色/武器/成就 ID 表（自 dump.cs 抽出）
```

### 4.1 GitHub 上架規範（本專案將公開）

**絕對不可進入 repo 的內容（.gitignore + 開發紀律雙重把關）：**

| 內容 | 原因 |
|---|---|
| `GameAssembly.dll`、`global-metadata.dat`、任何遊戲安裝檔 | poncle 的版權財產，公開散布構成侵權 |
| Il2CppDumper 完整產出（`dump.cs`、`il2cpp.h`、`script.json`） | 含遊戲完整程式結構，屬衍生內容；只允許把**自行整理的偏移數字**放進 `offsets.json` |
| 個人存檔 `SaveData` 及 `backups/` | 內含 Steam 帳號 ID 等個資 |
| 遊戲素材（圖片、音效、資料表原文） | 版權內容；ID 字串清單（如 `WHIP`、`ANTONIO`）屬事實性資料，可保留 |
| 絕對路徑中的使用者名稱 | 程式一律用 `%APPDATA%`、Steam 註冊表自動偵測，不得硬編碼本機路徑 |

**.gitignore 至少包含：** `bin/`, `obj/`, `*.user`, `dump/`, `backups/`, `SaveData*`, `*.dll`（data 目錄下白名單例外處理）

**README 必要章節：** 功能總覽（兩軌）/ 安裝與使用 / 「完全不修改遊戲檔案」原理說明 / 偏移表更新流程（遊戲改版時使用者自跑 Il2CppDumper 的教學）/ 免責聲明（僅限單機與本地遊玩、成就與 `CheatCodeUsed` 風險自負、與 poncle 無關）

**發佈策略：** Releases 附編譯好的 zip；`offsets.json` 隨版本 tag 更新，讓使用者在遊戲改版後不必等重新編譯

## 5. 實作順序

1. 軌道 A：存檔讀寫 + checksum 驗證（§2.3 是第一個必須攻克的點）→ 備份機制 → 分類 UI → 通用 JSON 編輯器
2. 跑 Il2CppDumper，抽 ID 表（回饋軌道 A）與偏移（軌道 B 用）
3. 軌道 B 依序：無敵（§3.3，純欄位寫入，最簡單）→ 快速開箱（§3.7）→ 傷害倍率（§3.4）→ 遊戲速率（§3.5）→ 寶箱最高獎勵（§3.6，唯一需要 code patch，最後做）
4. 第二波：§3.8 屬性表全開（與傷害倍率共用同一套指標鏈基礎設施，邊際成本低）→ 線上模式防護 → 進階遠端呼叫功能（選做）
5. GitHub 上架：依 §4.1 檢查表過濾內容後開源

## 6. 風險與注意事項

- 遊戲更新 → GameAssembly.dll 改變 → 偏移全部失效：`offsets.json` 以版本雜湊鍵入，啟動時比對，不符則提示重新 dump
- 成就會照常解鎖；`CheatCodeUsed` 旗標可能被遊戲設起（UI 提供檢視/重設）
- code patch 寫入程式碼頁前必須 `VirtualProtectEx`；patch 僅存在於記憶體，重啟遊戲自動還原
- 所有寫入操作皆須可單獨開/關並可還原原始值
