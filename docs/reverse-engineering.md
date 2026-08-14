# Reverse-engineering notes

本文件只記錄自行整理的偏移與驗證狀態，不包含或重製 `dump.cs`、`script.json`、`il2cpp.h` 或遊戲檔案。

## 目前 Steam 版（2026-07-23 擷取）

- `GameAssembly.dll` SHA-256：`248617379e77e795b0b2f12328e2a86968730fa9ede997d62a66db70be158e3a`
- `UnityPlayer.dll` SHA-256：`4abd2ee6d6ca6176b3122a22cc0264ea0e3c1674bd2969621fe72decbf7b5134`
- `global-metadata.dat` SHA-256：`6e82e833185a101ba30f00216fe2bed0beb78aa339fa452f1f5268839ebeb257`
- Profile ID：`steam-current-2026-07-23`；狀態 `verified: false`。
- 已用 Il2CppDumper v6.7.46 重新產生本機忽略的 metadata v31 dump。`GM`、`GameManager`、`CharacterController`、`PlayerModifierStats`、`PlayerOptionsData` 與 `TreasureFactory.MakePrizes` 的 TypeDef／欄位偏移維持不變。
- `GM_TypeInfo` RVA 從舊版 `160283072` 移到 `160283208`；所有以 `GM.Core` 為根的新版 pointer chain 已使用新 RVA，禁止僅替換雜湊而沿用舊根位址。
- `TreasureFactory.MakePrizes` RVA 仍為 `0x69E5DE0`。依 PE section table 將兩段 runtime RVA 映射回 raw offset 後，新 DLL 的 `0x69E60C8` 仍為 `8B 4B 18`，`0x69E66C0` 仍為 `F3 0F 2C 47 48`；候選 patch 與 expected bytes 因此可保留，但仍須實際開箱驗證。
- 最終開箱驗證使用專用、開發限定命令，不再要求操作者在原本的五秒裸 patch 視窗內碰到寶箱：先在離線單人關卡靠近一個未開寶箱，再執行 `dotnet run --project VSModifier.Tests --configuration Debug -- --verify-treasure-behavior steam-current-2026-07-23 30000`，然後立即開箱。流程只允許 `maxTreasure`、最多 30 秒，持續執行 100ms 線上 guard，只接受 checkpoint 之後新增的 `Player.log` `Treasure PrizeCount = 5` 事件；成功或任何失敗都會還原兩段 patch 並讀回原始位元組。命令成功只能證明本次 log 行為與還原，仍須搭配使用者目視結果及其餘功能證據，才能考慮將 Profile 改為 `verified: true`。
- `UnityPlayer.dll` 未變，`gameSpeed` 唯一 AOB 可沿用；仍須在實際程序重新確認唯一命中與時間倍率效果。
- 重新抽取的解鎖表只有角色 ID `TP_CHAOS` 從 `BoughtCharacters`／`UnlockedCharacters` 移除，其餘 9 組 ID 陣列沒有集合差異。
- Debug／Release 全方案建置 0 警告／0 錯誤的舊基準與後續測試里程碑均記錄於 `AI_HANDOFF.md`；live read-only 已證明新三檔 Profile 與新解鎖 Profile 同時精確命中。修改器會依 `offsets.json` 內容 SHA-256 自動偵測 Profile 更新，支援手動重新偵測，正式附加前也會重算遊戲三檔指紋。記憶體位元組套用／還原已有受控證據，但實際寶箱行為仍待上述專用流程完成。

## 先前 Steam 版（2026-07-22 擷取）

- `GameAssembly.dll` SHA-256：`f43017baa184cc6a5d6f6cc41d5bce28eaba5e164083dfa2ecb136fbbdb00dab`
- `UnityPlayer.dll` SHA-256：`4abd2ee6d6ca6176b3122a22cc0264ea0e3c1674bd2969621fe72decbf7b5134`
- `global-metadata.dat` SHA-256：`ad18e1db49fded4c18d6bf7c1a5e607ff80712c743770c833dcbd540bb939498`
- IL2CPP metadata version：31
- Profile 狀態：`verified: false`，只完成靜態推導，尚未實機讀寫驗證；保留作為已知舊版資料，不再標示為目前版。
- 版本辨識必須同時命中以上三個 SHA-256；任一檔案不同都視為另一個或混合版本，拒絕套用本 Profile。`offsets.json` schema 2 可並列多個三檔版本 Profile。
- Profile ID：`steam-current-2026-07-22`。`data/ids/unlocks.json` 也以此 ID 綁定解鎖表，未知版本不得套用最新版 ID；一鍵操作採保留重複項目的聯集合併。
- `GM_TypeInfo` RVA：`160283072`
- `Il2CppClass.static_fields`：`0xB8`
- `GM.Core`：靜態欄位 `+0x0`，指向 `GameManager`
- 線上防護候選：`GameManager.<StartedAsOnlineMultiplayerRun> +0x389`
- 主角色候選：`GameManager._mainCharacters +0x2D0` → `List._items +0x10` → 第一個陣列元素 `+0x20`
- 無敵候選：`CharacterController._permanentInvulnerability +0x34C`
- 屬性入口：`CharacterController._playerStats +0x218`
- `PlayerModifierStats` 多數欄位是 `EggFloat`/`EggDouble` 物件，實際基礎值在該物件 `+0x10`；Charm (`+0xB4`) 與 Defang (`+0xB8`) 是直接值。
- 即時快速寶箱候選：`GameManager._playerOptions +0x90` → `PlayerOptions._mainGameConfig +0x50` → `PlayerOptionsData.AlwaysQuickTreasureAnim +0x159`。
- 遊戲速率已完成唯讀實機定位：`Time.get_timeScale` 解析到 `UnityPlayer.dll + 0xB7D40`，其指令以 RIP-relative 方式取得 TimeManager 全域指標，`TimeManager + 0x1AC` 為 `timeScale`。
- `gameSpeed` 不使用固定 `UnityPlayer.dll` RVA；profile 以唯一 AOB `48 8B 05 ?? ?? ?? ?? F3 0F 10 80 AC 01 00 00 C3` 找 getter，再解析位移與指標鏈。主選單唯讀值為 `1.0`，尚未做寫入測試。
- `TreasureFactory.MakePrizes` 以 `Treasure.level` 的 1／2／3 分支生成 1／3／5 件獎勵；目前 profile 將 `mov ecx,[rbx+0x18]`（RVA `0x69E60C8`）改為等長的 `push 3; pop rcx`，沿用原生五件獎勵流程。
- 五件分支的金幣公式為 `Round(Random.value * 500 + 500)`；共同結算點 RVA `0x69E66C0` 改為 `mov eax,1000`。兩段都設定精確 `expectedBytes`，以原子複合 patch 套用與逆序還原；目前只完成實際程序唯讀位元組比對，尚未實際開箱驗證。

在讀取鏈路、合理值範圍、線上 guard 與實際遊戲效果全部驗證前，不得把此 profile 改為 `verified: true`。
