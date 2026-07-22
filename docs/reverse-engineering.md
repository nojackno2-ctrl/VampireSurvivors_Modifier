# Reverse-engineering notes

本文件只記錄自行整理的偏移與驗證狀態，不包含或重製 `dump.cs`、`script.json`、`il2cpp.h` 或遊戲檔案。

## 2026-07-19 local build

- `GameAssembly.dll` SHA-256：`f43017baa184cc6a5d6f6cc41d5bce28eaba5e164083dfa2ecb136fbbdb00dab`
- IL2CPP metadata version：31
- Profile 狀態：`verified: false`，只完成靜態推導，尚未實機讀寫驗證。
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

在讀取鏈路、合理值範圍、線上 guard 與實際遊戲效果全部驗證前，不得把此 profile 改為 `verified: true`。
