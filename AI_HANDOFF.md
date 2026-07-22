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

## 已知嘗試

- 第一版 WPF 介面加入後的初次建置失敗：缺少 `System.IO` / `System.Windows.Input` using，並有 `out` 參數 nullable 分析警告。已定位為純編譯問題，正在修正；該版本未執行。
- 修正後 WPF 與全方案建置為 0 警告、0 錯誤，核心測試仍為 5/5 通過。
- 嘗試用 Windows 應用控制做介面視覺 QA，但應用核准逾時；目前只有編譯證據，尚無執行中畫面證據。
- live read-only 驗證通過：程式找到 1 個實際 SaveData、確認 checksum 有效並解析 154 個頂層欄位；測試未寫入存檔，也未輸出個人路徑或內容。

## 重要限制

- 不得把本機絕對使用者路徑、Steam ID、SaveData 內容或遊戲衍生輸出寫入 repo。
- 所有會改動資料的測試只能使用工作區內產生的測試檔。
- Trainer 尚無版本化偏移；在取得並驗證正確偏移前不得假稱記憶體功能可用。

## 下一步

1. 建立存檔分類操作與 WPF 工作流程。
2. 建立 Trainer 基礎設施，再取得版本化偏移並逐項實機驗證。
