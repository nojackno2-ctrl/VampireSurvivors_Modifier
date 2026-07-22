# AI Handoff

## 目前目標

依 `DESIGN.md` 完成 C# WPF Vampire Survivors 修改器，包含存檔修改器與外部記憶體 Trainer，且不修改遊戲安裝檔案。

## 接手代理立即執行

1. 完整閱讀根目錄 `AGENTS.md`、本文件與 `DESIGN.md`。
2. 執行 `git status --short`、`git diff`、`git log -8 --oneline`，不得覆蓋其他代理或使用者的未提交修改。
3. 以 `dotnet build VSModifier.sln --configuration Debug` 與 `dotnet run --project VSModifier.Tests` 建立目前基準。
4. 所有遊戲安裝檔只可唯讀；不得直接修改安裝目錄。實際存檔寫入只能由修改器安全流程執行，測試不得寫使用者存檔。
5. 目前唯一需要使用者外部狀態的關鍵工作：進入任一單人關卡後，完成 Trainer 全鏈唯讀與逐項可逆實機驗證。Profile 在此之前必須保持 `verified: false`。

最近重要 Commit：`e8e1deb`（三檔版本指紋）、`78dace3`（授權與發行前驗證）。目前工作樹可能正在進行版本化解鎖表與單屬性金蛋編輯修正，務必以 Git diff 為準。

## 已確認環境

- 工作區起始時只有 `DESIGN.md`，不是 Git 儲存庫。
- .NET SDK 10.0.302、Windows Desktop Runtime 10.0.10 可用。
- 實際 `SaveData`、`VampireSurvivors.exe`、`GameAssembly.dll` 與 `global-metadata.dat` 均存在。
- 工作區內未找到 Il2CppDumper。

## 關鍵證據

### SaveData checksum（2026-07-22，已用實際存檔唯讀驗證）

1. 以 UTF-8 無 BOM 讀取完整 JSON。
2. 將第一個符合 `("checksum"\s*:\s*")[a-f0-9]{64}(")` 的值替換為空字串，保留 `"checksum":""` 欄位。
3. 對替換後的完整 UTF-8 位元組計算 SHA-256，輸出小寫 hex。
4. 計算結果與實際存檔的 64 字元 checksum 完全相等。
5. 移除整個欄位的候選算法不相等，已排除。

## 目前進度

- 已讀取並採用 `DESIGN.md`。
- 已完成 checksum 算法的第一手驗證。
- 已初始化 Git `main` 分支及 `VSModifier.slnx`。
- 已建立 `VSModifier.Core`、`VSModifier.Memory`、`VSModifier.App`、`VSModifier.Tests` 專案。
- 已實作 checksum、JSON 存檔模型、備份與安全寫入、遊戲行程阻擋、Steam 存檔探索及常用編輯操作。
- `dotnet build VSModifier.slnx` 成功；第一次測試執行為 5/5 通過（2026-07-22）。
- 使用者會以 Visual Studio 編譯，並已授權在重要、已驗證的里程碑建立 commit。
- 版本維護範圍以目前 Steam 最新版與之後的新版本為主；因沒有歷史遊戲檔案，不為舊版本建立無法驗證的推測 Profile。遊戲更新時保留多 Profile 架構並新增或更新最新版本資料。
- Trainer 安全底層已完成：外部行程附加、模組定位、型別化讀寫、指標鏈、AOB 掃描、可還原 code patch、100ms 鎖值、倍率鎖值及 offsets profile。
- Trainer 版本辨識已升級為三檔精確指紋：`GameAssembly.dll`、`UnityPlayer.dll`、`global-metadata.dat` 的 SHA-256 必須同時命中同一個 schema 2 Profile。未知、混搭或 metadata 不符時會回報具體原因並拒絕附加；`offsets.json` 可並列多版本 Profile。
- 目前三檔指紋已登錄為 `verified: false`，缺偏移、線上 guard 或尚未完成實機驗證時拒絕附加。
- 最新建置為 0 警告、0 錯誤；完整測試 10/10 通過。live read-only 同時驗證遊戲路徑探索、profile 命中與 fail-closed 狀態。
- Il2CppDumper v6.7.46 已成功產生本機忽略的 dump；最後 `Console.ReadKey` 因重導輸入拋例外，但 `dump.cs`/`script.json`/`il2cpp.h` 均完整存在於 `dump/current`，沒有寫到遊戲資料夾。
- 已新增 `VSModifier.IdExtractor`，產生 11 組版本解鎖陣列。PowerUp 兩欄不覆蓋，因重複 ID 代表等級。
- 靜態候選偏移已寫入 `data/offsets.json` 與 `docs/reverse-engineering.md`，profile 仍為 `verified: false`。
- 主選單兩次只讀診斷均走通 TypeInfo/static_fields，但 `GM.Core` 尚為 null；需進入實際關卡後繼續驗證。

## 已知嘗試

- 第一版 WPF 介面加入後的初次建置失敗：缺少 `System.IO` / `System.Windows.Input` using，並有 `out` 參數 nullable 分析警告。已定位為純編譯問題，正在修正；該版本未執行。
- 修正後 WPF 與全方案建置為 0 警告、0 錯誤，核心測試仍為 5/5 通過。
- 嘗試用 Windows 應用控制做介面視覺 QA，但應用核准逾時；目前只有編譯證據，尚無執行中畫面證據。
- live read-only 驗證通過：程式找到 1 個實際 SaveData、確認 checksum 有效並解析 154 個頂層欄位；測試未寫入存檔，也未輸出個人路徑或內容。
- Trainer 基礎設施初次建置失敗：`LibraryImport` 來源產生器要求啟用 `/unsafe`。決定改用不需 unsafe 的 `DllImport`，並在審查時一併修正附加行程物件可能過早 Dispose 的生命週期問題。
- Trainer 測試首次編譯時，兩個 `SequenceEqual` 預期值被推導成 `int[]` 而非 `byte[]`；已明確指定 byte 陣列。程式庫本身當次為 0 警告、0 錯誤。
- 加入倍率鎖值後首次建置發現布林解碼分支回傳 `bool`，與統一的 `double` 回傳型別不符；已改為明確的 1/0。該次尚未執行 live profile 驗證。
- 官方 Il2CppDumper v6.7.46 net7-win 首次執行失敗，退出碼 `-2147450730`：本機沒有 .NET 7 runtime（只有 8/10），尚未產生 dump。下一次改為僅對該程序啟用 major roll-forward，不安裝舊 runtime。

## 重要限制

- 不得把本機絕對使用者路徑、Steam ID、SaveData 內容或遊戲衍生輸出寫入 repo。
- 所有會改動資料的測試只能使用工作區內產生的測試檔。
- Trainer 已有目前版本的候選 profile，但仍是 `verified: false`；在完成單人關卡唯讀與逐項可逆寫入驗證前不得假稱記憶體功能可用。

## 下一步

1. 在實際單人關卡內重跑唯讀診斷，確認線上 guard 與角色／屬性鏈。
2. 逐項執行可逆短時間寫入驗證，確認線上 guard、還原與實際遊戲效果後才將 profile 設為已驗證。
3. 實際開箱驗證寶箱最高獎勵 patch，並完成其餘 WPF 頁籤的執行中視覺 QA。

## 最新診斷紀錄（2026-07-22）

- 新增 `--inspect-time-scale-read-only` 的第一次編譯失敗：`nint + long` 產生 `long`，無法傳入位址參數；改為明確 `nint` 轉換後，全方案建置為 0 警告、0 錯誤。
- `Time.get_timeScale` 唯讀定位到 `UnityPlayer.dll + 0xB7D40`；AOB＋RIP-relative 解析器與唯一匹配檢查已加入，11/11 測試通過。實際主選單唯讀解析 `gameSpeed = 1`，未寫入遊戲。
- 寶箱最高獎勵已定位成兩段 patch：獎勵等級強制為 3（五件）以及共同金幣結算強制為 1000。新增原子複合 patch，第二段失敗時會還原第一段；全方案 0 警告、12/12 測試通過，且實際程序唯讀比對兩段原始位元組成功。尚未寫入或實際開箱驗證。
- WPF Trainer 頁已完成實際畫面 QA：第一次畫面發現深色背景上的標題與停用控制對比過低，加入全域 TextBlock／CheckBox／TextBox／Button 深色與停用樣式後重新截圖確認可讀、無裁切。
- 全域熱鍵以 `RegisterHotKey` 實作，預設停用。實測註冊成功、`Ctrl+Shift+F1` 能進入事件並在未附加時安全拒絕，解除註冊後同一按鍵不再觸發；`Ctrl+Shift+F12` 保留為緊急中斷並還原。
- 雙模式 JSON 樹狀編輯器第一次建置時，Core 與 WPF 成功，但測試專案缺少 `System.Text.Json` using，導致 `JsonValueKind` 無法解析；補上引用後全方案成功、13/13 測試通過。
- JSON 樹狀／原始雙模式第一次畫面 QA 無裁切，但 TreeView 節點被系統前景色覆蓋成黑色；在資料模板明確指定亮色後重新截圖，根節點與展開的 154 個頂層欄位皆清楚可讀。選取字串節點時，路徑、型別、值與套用按鈕均正確顯示。
- 使用者要求修改器能辨識遊戲版本並對不同版本採用各自修改方法。版本 Profile 已改為 `GameAssembly.dll`／`UnityPlayer.dll`／`global-metadata.dat` 三檔 SHA-256 精確配對；每個 Profile 自帶獨立偏移、AOB、expected bytes 與驗證狀態，不能因其中一檔相同而誤用。
- 三檔版本里程碑 commit `e8e1deb` 完成後重跑 Trainer 唯讀診斷：Profile 精確命中；`gameSpeed=1` 與 `maxTreasure` 兩段原始位元組可讀，其餘 22 項均在指標鏈第 3 層得到 null，與遊戲仍在主選單、`GM.Core` 尚未建立一致。未做任何記憶體寫入；須由使用者手動進入任一單人關卡後再驗證。
- WPF 剩餘頁面已完成實際畫面 QA：資源、解鎖、蛋資料與旗標頁的文字、輸入欄、清單及按鈕均可讀，沒有重疊或裁切；QA 過程沒有按下任何套用或一鍵修改按鈕。至此所有主要頁籤皆有實際畫面證據。
- 已補上設計計畫要求的 MIT `LICENSE`，README 連結授權條款。framework-dependent win-x64 Release publish 已驗證：包含 App／Core／Memory、`data/offsets.json`、`data/ids/unlocks.json`，不含 GameAssembly、metadata、dump、SaveData 等禁止檔；輸出只放在已忽略的 `artifacts/`，Trainer 未完成實機驗證前不發布。
- 完整需求稽核發現解鎖頁列出 16 個陣列，但版本表只有 11 組，且 `UnlockedSkins`／`UnlockedSkinsV2` 實際為 dictionary，原本可能被陣列編輯器破壞。已將兩個 dictionary 從陣列 UI 移除，改由進階 JSON 處理；`offsets.json` 與 `unlocks.json` 以 `profileId` 精確綁定，一鍵解鎖改為原子安全合併，保留既有順序、重複 PowerUp 等級與未收錄 ID。
- 使用者要求每個金蛋屬性都能單獨修改，例如只改攻擊或防禦。金蛋頁改用中文屬性名稱與內部 key 綁定，顯示選定角色／屬性的目前值；已知屬性全部可選，並會從實際 `EggData` 動態加入未來版本新增的數值屬性。套用只改該欄並重算衍生 `total`，其他屬性保持不變，角色 ID 比對不分大小寫。
- 使用者明確要求保留 AI 交接文件，讓其他代理可繼續或修復。`AGENTS.md` 為協作規則，`AI_HANDOFF.md` 為即時狀態；README 已加入接手入口，本文件頂部新增接手命令與實機阻塞點。
- 版本化解鎖與金蛋單屬性里程碑驗證：全方案建置 0 警告／0 錯誤，15/15 測試通過；實際安裝的三檔 Profile 與對應 unlock Profile 同時命中，IdExtractor 已驗證可保留多 Profile 且更新同 ID 不重複。實際 WPF QA 顯示 21 個已知金蛋屬性皆完整可選、中文標籤與目前值區無裁切；解鎖頁 dictionary 說明亦完整。QA 未套用或寫入存檔。
- 線上防護稽核發現原本只在值鎖寫入前檢查，code patch 單獨啟用時缺少持續 guard，背景失敗也只移除單一 lock。已改為附加後永遠啟用 100ms guard；偵測線上或任一背景失敗時 fail-closed 清除全部 locks、還原所有值與 patches、透過 `SafetyStopped` 通知 WPF 自動中斷。複合 patch 還原失敗時保留 enabled 狀態，允許 Dispose 再次嘗試。
- 持續線上 guard 變更已完成建置 0 警告／0 錯誤與 15/15 測試；回歸證明 fail-closed 事件後所有 locks 停止，複合 patch 還原失敗後可第二次重試成功。尚未在單人關卡實測實際 guard 位址，Profile 仍必須保持 `verified: false`。
