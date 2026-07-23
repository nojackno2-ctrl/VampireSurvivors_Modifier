# AI Handoff

## 目前目標

依 `DESIGN.md` 完成 C# WPF Vampire Survivors 修改器，包含存檔修改器與外部記憶體 Trainer，且不修改遊戲安裝檔案。

## 接手代理立即執行

1. 完整閱讀根目錄 `AGENTS.md`、本文件與 `DESIGN.md`。
2. 執行 `git status --short`、`git diff`、`git log -8 --oneline`，不得覆蓋其他代理或使用者的未提交修改。
3. 以 `dotnet build VSModifier.sln --configuration Debug` 與 `dotnet run --project VSModifier.Tests` 建立目前基準。
4. 所有遊戲安裝檔只可唯讀；不得直接修改安裝目錄。實際存檔寫入只能由修改器安全流程執行，測試不得寫使用者存檔。
5. 2026-07-23 新三檔 Profile 已完成重新抽取、靜態更新與熱重新載入支援；接著進入任一單人關卡，完成 Trainer 全鏈唯讀與逐項可逆實機驗證。Profile 在此之前必須保持 `verified: false`。

最近重要 Commit：`e3347dc`（Trainer 版本資料熱重新載入）、`40a9949`（2026-07-23 遊戲版本資料）、`74fbdab`（受控 Trainer 實機驗證流程）。公開 repository 為 `https://github.com/nojackno2-ctrl/VampireSurvivors_Modifier`；首次公開基線已推送 `main`，目前發布檢查分支為 `agent/initial-public-release`，Draft PR 為 `https://github.com/nojackno2-ctrl/VampireSurvivors_Modifier/pull/1`。兩個最新 commit 尚未 push，接手時仍務必以 Git log、status 與 diff 為準。

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

1. 在實際單人關卡內對 `steam-current-2026-07-23` 重跑唯讀診斷，確認 `onlineSession=0` 與全部角色／屬性鏈。
2. 逐項執行可逆短時間寫入驗證，確認線上 guard、還原與實際遊戲效果。
3. 實際開箱並配合 log 驗證寶箱最高獎勵複合 patch。
4. 全部通過後才將新版 Profile 設為 `verified: true`，重跑 WPF 正式附加與 Release／發布稽核。

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
- UI 規格稽核發現底部狀態列未持續顯示目前存檔路徑與遊戲文字狀態，最近備份也只記住本次執行。已補上完整路徑（畫面省略、ToolTip 保留全文）、紅綠燈加可否安全寫入文字，並於啟動時掃描 `%LOCALAPPDATA%\VSModifier\backups` 顯示最新備份時間。
- 新狀態列已完成建置、15/15 測試、live read-only 與實際 WPF 畫面 QA；在預設視窗大小下遊戲狀態、一般訊息、路徑省略與最近備份可同時閱讀，沒有重疊或裁切。QA 未寫入存檔。
- 版本 Profile 安全稽核發現 schema 尚未驗證 AOB／RIP 範圍、patch 等長性或已驗證 Profile 的功能完整度。已新增結構驗證：所有 address、AOB、patch bytes 與 feature kind/value type 都先驗證；`verified: true` 必須具備線上 guard 與 24 個主要／第二波功能，避免未完整或手誤 Profile 被啟用。
- Profile schema 強化已完成建置 0 警告／0 錯誤、15/15 測試與 live read-only；目前 Profile 可通過，測試中的不同長度 patch 會在 catalog 載入時被拒絕。
- 再次重跑主選單 Trainer 唯讀診斷：精確命中目前 Profile，`gameSpeed=1` 與 `maxTreasure` 兩段位元組可讀；`onlineSession` 與其餘 22 項仍在指標鏈第 3 層得到 null，合計 2/24。退出碼 1 代表 guard 尚不可解析、不是建置失敗；沒有進行任何記憶體寫入。
- 已新增僅供開發的受控驗證路徑：未驗證 Profile 仍不能由正式 WPF 附加；開發命令必須精確指定目前 Profile ID、一次只允許單一功能、限制 100–5000ms，並沿用 100ms 線上 guard。數值與 patch 都會在結束時還原並讀回確認；還原第一次失敗時保留追蹤狀態，供 `DisposeAsync` 再試。
- 受控驗證工具離線里程碑：全方案建置 0 警告／0 錯誤，16/16 測試通過。新增測試證明正式附加仍拒絕 `verified: false`，且開發驗證會拒絕錯誤 Profile ID、錯誤功能類型與超過 5 秒的要求。尚未執行任何受控寫入；必須等使用者進入單人關卡，唯讀確認 `onlineSession=0` 後才可逐項執行。
- 發行內容重新稽核時，framework-dependent win-x64 第一份產物確認禁止檔與 PDB 均為 0，但缺少 `README.md` 與 `LICENSE`。修正後以全新輸出目錄重跑：必要檔 8/8、禁止檔 0、PDB 0，Release 建置 0 警告／0 錯誤且 16/16 測試通過。兩份文件與兩份版本 data 已明確設為輸出項目，並新增 Visual Studio `win-x64-folder` 發布 Profile；Profile 仍為 `verified: false`，不得對外宣稱 Trainer 可用。
- 第一次實際改用 `win-x64-folder.pubxml` 發布時，App 本身沒有 PDB，但 Core／Memory 專案參考仍產生 2 個 PDB；原因是 `.pubxml` 的偵錯屬性只屬於 App，未全域傳遞。新增方案層級 `Directory.Build.props` 後，以第三個全新輸出目錄重測成功：必要檔 8/8、禁止檔 0、PDB 0，Release 0 警告／0 錯誤且 16/16 測試通過。再壓成測試 ZIP 後直接檢查 ZIP entries，必要檔、`data` 子目錄與 0 禁止檔／0 PDB 均保持正確；稽核產物只在已忽略的 `artifacts/`，尚未對外發布。

## 新版遊戲事件（2026-07-23）

- 使用者實際啟動修改器後顯示未知組合，畫面中的新指紋與唯讀重算一致。這不是附加 API 無回應，而是版本安全檢查拒絕舊偏移。
- 新 `GameAssembly.dll`：174,786,048 bytes，SHA-256 `248617379e77e795b0b2f12328e2a86968730fa9ede997d62a66db70be158e3a`。
- `UnityPlayer.dll` 未變：33,661,872 bytes，SHA-256 `4abd2ee6d6ca6176b3122a22cc0264ea0e3c1674bd2969621fe72decbf7b5134`。
- 新 `global-metadata.dat`：49,067,568 bytes，SHA-256 `6e82e833185a101ba30f00216fe2bed0beb78aa339fa452f1f5268839ebeb257`。
- 新版遊戲檔時間為 2026-07-23 09:59（Asia/Taipei）；新版 Profile 必須使用重新抽取的根 RVA，禁止把舊版偏移僅換雜湊後沿用。
- Il2CppDumper v6.7.46 已對新版完成 metadata v31 dump，產物隔離在已忽略的 `dump/current-2026-07-23`；舊 `dump/current` 未覆蓋。工具忽略第三個輸出參數並先寫到 exe 目錄，已將五項新產物精確移至新版目錄，後續不要重複這個錯誤用法。
- 新舊 dump 靜態比較：所有關鍵 TypeDef 與欄位 offset 不變，`TreasureFactory.MakePrizes` RVA 不變；`GM_TypeInfo` 唯一明確變動為 `160283072 -> 160283208`。兩段寶箱 expected bytes 經 PE RVA-to-raw 正確映射後都相符；曾有一次把 RVA 當 raw offset 而得到假性不符，已排除，禁止重複該讀法。
- 已新增 `steam-current-2026-07-23` offsets／unlock Profile 並保留 2026-07-22 Profile。新版 unlock 只有 `TP_CHAOS` 從兩個角色陣列移除。新增 shipped-data 回歸，鎖定新版三檔指紋、GM 根 RVA、兩段寶箱 RVA／expected bytes、fail-closed 狀態與 `TP_CHAOS` 新舊差異。Debug／Release 均為 0 警告／0 錯誤、各 16/16 測試及 Release live read-only 全通過；目前遊戲未執行，尚不能做新版程序鏈診斷，`verified` 保持 false。
- 另以 Visual Studio `win-x64-folder` Profile 發布到全新且已忽略的稽核目錄，確認發布包各包含一份 `steam-current-2026-07-23` offsets／unlock Profile、offset Profile 仍為 `verified: false`、禁止檔 0、PDB 0。使用者畫面中的 VSModifier 行程早於這次新版資料建置；必須關閉舊行程並以最新建置重新啟動，否則仍會載入舊 data。
- 版本熱重新載入第一次 Debug 建置失敗：`OffsetCatalogFile.Path` 屬性遮蔽 `System.IO.Path`，使建構式的 `Path.GetFullPath` 產生 CS1061。已改用完整限定名稱；這是編譯期命名問題，不是記憶體附加或版本判斷失敗。
- 第二次 Debug 建置揭露測試誤用了不存在的 `CreateTemporaryDirectory`／`False` 輔助函式，且先前 UI QA 啟動的 VSModifier PID 4308 鎖住 Debug Core／Memory DLL。測試已改用本專案既有的 GUID 暫存目錄與 `True(!condition)` 慣例；PID 4308 路徑已再次確認為本專案 Debug App，後續採正常關閉視窗後重測。
- 已修正舊行程永久沿用啟動時 Profile 的問題：`OffsetCatalogFile` 以 offsets 內容 SHA-256 偵測變更，未附加時由 1 秒狀態計時器自動重讀；Trainer 頁新增「重新偵測版本」，頂端「重新載入」也會重讀 Profile，且每次正式附加前再重算三檔指紋。重讀開始即清除既有 Profile／catalog，錯誤或未驗證版本一律 fail-closed；UI 會顯示 Profile ID，並明確區分未知版本、尚未實機驗證與按鈕故障。
- 熱重新載入里程碑驗證：先前 Debug App PID 4308 已用 `CloseMainWindow` 正常結束，未強制終止；Debug／Release 全方案皆 0 警告／0 錯誤，新增 catalog 內容變更、重讀與無效版本抑制回歸後共 17/17 測試通過。Release live read-only 再次確認 1 個有效 SaveData、154 個欄位、1 個完整安裝、新 offsets／unlock Profile 命中且 `verified: false`，全程無寫入。以 `win-x64-folder` 發布到全新忽略目錄後，新 offsets／unlock Profile 各 1、PDB 0、禁止檔 0。
