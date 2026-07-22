# Reverse-engineering notes

本文件只記錄自行整理的偏移與驗證狀態，不包含或重製 `dump.cs`、`script.json`、`il2cpp.h` 或遊戲檔案。

## 目前 Steam 版（2026-07-22 擷取）

- `GameAssembly.dll` SHA-256：`f43017baa184cc6a5d6f6cc41d5bce28eaba5e164083dfa2ecb136fbbdb00dab`
- `UnityPlayer.dll` SHA-256：`4abd2ee6d6ca6176b3122a22cc0264ea0e3c1674bd2969621fe72decbf7b5134`
- `global-metadata.dat` SHA-256：`ad18e1db49fded4c18d6bf7c1a5e607ff80712c743770c833dcbd540bb939498`
- IL2CPP metadata version：31
- Profile 狀態：`verified: false`，只完成靜態推導，尚未實機讀寫驗證。
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
