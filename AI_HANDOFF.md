# AI Handoff

## 目前目標

依 `DESIGN.md` 完成 C# WPF Vampire Survivors 修改器，包含存檔修改器與外部記憶體 Trainer，且不修改遊戲安裝檔案。

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
- Trainer 安全底層已完成：外部行程附加、模組定位、型別化讀寫、指標鏈、AOB 掃描、可還原 code patch、100ms 鎖值、倍率鎖值及 offsets profile。
- `GameAssembly.dll` SHA-256 `f43017baa184cc6a5d6f6cc41d5bce28eaba5e164083dfa2ecb136fbbdb00dab` 已登錄為 `verified: false`，缺偏移與線上 guard 時拒絕附加。
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
- Trainer 尚無版本化偏移；在取得並驗證正確偏移前不得假稱記憶體功能可用。

## 下一步

1. 將 Trainer 版本狀態、附加流程與功能控制接入 WPF。
2. 取得 Il2CppDumper 產出，填入線上 guard 與功能偏移並逐項實機驗證。
3. 補做 WPF 執行中視覺 QA。
