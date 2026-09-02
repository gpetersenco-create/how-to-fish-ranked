# How to Fish: game internals reference (for the 1v1 mod)

Derived from ILSpy decompilation of `Assembly-CSharp.dll` (game build of 2026-09-01).
Game: Unity 6000.4.4f1, Mono, URP, FishNet 4 (FishyUnityTransport + FishySteamworks
via Multipass), Heathen Steamworks, TextMeshPro, LeanTween. No Addressables use in code.
Game dir: `C:\Program Files (x86)\Steam\steamapps\common\How to Fish\How to Fish\`
Managed: `How to Fish_Data\Managed\` (Assembly-CSharp.dll, FishNet.Runtime.dll, ...).
Re-decompile with: `ilspycmd -p -o <outdir> -r "<Managed>" "<Managed>/Assembly-CSharp.dll"`.

## Decompiled RPC naming (FishNet weaver)
For `[ServerRpc]/[ObserversRpc]/[TargetRpc] void Foo(args)` with hash N:
- `Foo(args)` public stub, calls `RpcWriter___Foo___N`
- `RpcWriter___Foo___N` serializes + sends (patch to veto on sender)
- `RpcLogic___Foo___N(P_0, P_1, ...)` the real body on the receiver (patch to change behavior)
- `RpcReader___Foo___N(PooledReader, Channel[, NetworkConnection])`
Call RPCs through the public stub.

## Players
- `Player : NetworkBehaviour`. `Player.LocalPlayer` static. `player.Owner` (NetworkConnection),
  `player.OwnerId`, `player.SteamID` (SyncVar<ulong>), `player.SteamName`.
- `PlayerManager` (static MonoBehaviour): `Players`, `AlivePlayers`, `OtherPlayers` lists,
  `event Action OnPlayerAmountChange`, `OnPlayerDied(Player)`, `OnPlayerResurrected(Player)`,
  `GetPlayerFromBodyPart(Transform)`, `InGodMode`.
- `Client.Clients` : `Dictionary<NetworkConnection, Client>`; `Client.LocalClient`.
- `Player.BlockInputs` (getter, Player.cs:180) checked by all input handlers.
- Sub-components: `player.Vitals` (PlayerVitals), `.Dying` (PlayerDying), `.Holding` (PlayerHolding),
  `.Inventory` (PlayerInventory), `.Movement`, `.Other` (OtherPlayer), `.Transform`, `.CamObject`, `.Rigidbody`.

## Damage
- Client detects hit, then `PlayerVitals.LocalHit(point, dir, Player playerWhoHit, int damage, bool rangedHit, Vector3 force, bool fromNpc=false)` (PlayerVitals.cs:333)
  - `damage *= _playerDamageMultiplier` (0.25f) unless fromNpc
  - if `!ServerSettings.UseFriendlyFire && !fromNpc`: cosmetic only, no RPC
  - else `Server.Instance.HitPlayer(_player, damage, force, point, 2, playerWhoHit)`
- Server: `Server.RpcLogic___HitPlayer___2449261505(Player P_0, int P_1, Vector3 force, Vector3 pos, byte type, Player P_5)`
  gate: `(!P_5 || ServerSettings.UseFriendlyFire || P_0 == P_5)` then `P_0.Vitals.TakeDamage(P_1, pos, force, ignoreInvuln: P_5 != null)`
- Hunger: `PlayerVitals.TickUpdate()` (server, on TimeManager.OnTick) calls `LowerFullnessTick()` (drain) and `DamageFromFullness()`
  (5 hp per 150 ticks at 0 fullness). Health regen tick exists (needs fullness >= 80% and no recent damage).
- `PlayerVitals.TakeDamage(int amount, Vector3 pos, Vector3 force, bool ignoreInvulnerability)` (server only; no-ops if
  `PlayerManager.InGodMode`, `EndGameEffects.IsShowingEndGame`, AFK, or within 0.25 s invuln). Writes `_syncedHealth` (SyncVar<int>, max 100).
  At 0: `_player.Dying.ServerDie(force)`.
- `ServerSettings.Instance` (NetworkBehaviour): `_useFriendlyFire` SyncVar<bool> default TRUE, `UseFriendlyFire` static getter,
  `ToggleFriendlyFire(bool)` server-only, `_useOneShot`/`OneShotEnabled`/`ToggleOneShot()`, `DamageMultiplier` static (difficulty).
- Explosions: `ExplosionManager` calls `Server.Instance.HitPlayer` directly (skips LocalHit).
- Hit sources: ProjectileManager.cs:431, Weapon.cs:340 (point blank), Melee.cs:382, PlayerPunching.cs:352.

## Death / respawn
- `PlayerDying.ServerDie(Vector3 force)` (server): instantiates `GameInfo.DeadPlayerPrefab` ragdoll (`DeadPlayer : Item`),
  network-spawns it, `_deadPlayer = ...`, drops the held item (`SetSyncedHolder(null)`).
- Clients: `PlayerVitals.OnHealthChange(prev,next,asServer)`: health 0 leads to `Dying.LocalDie()` (owner) / `DeathEffects()` (others),
  which set `IsDead = true`, `PlayerUI.ToggleDeathUI`, player GameObject SetActive(false), `PlayerManager.OnPlayerDied`.
  Health > 0 from dead leads to `LocalResurrect()` / `ResurrectEffect(respawned)` where `respawned = (next == 100)`.
- `PlayerDying.ResurrectEffect(bool respawned)` (PlayerDying.cs:194): pos = respawned ? `SpawnManager.PlayerSpawnPos` : last dead pos;
  teleports (`_player.LocalTeleport(pos, rot, instant:true)` for owner, `_player.Other.Teleport` for others), re-enables body.
- `PlayerVitals.ServerResetVitals()` (server): health 100, fullness 100, poison 0. `Heal(int)`, `OnResurrect()` (heal 25 = revive in place).
- `PlayerDying.LocalRespawn()` (give up, hold LMB 1 s) calls `Server.Instance.RespawnPlayer(player,pos,rot)`, whose logic
  `Server.RpcLogic___RespawnPlayer___2210451296` DROPS ALL INVENTORY (`Inventory.ServerDropAll`), `ServerResetVitals`, destroys ragdoll
  (`DeadPlayer.DestroyItem(7)`); if all dead it also moves the boat.
- `PlayerDying.DeadPlayer` property (ragdoll), `PlayerDying.IsDead`.

## Teleport
- `Server.Instance.TeleportPlayer(Player, Vector3 pos, float yaw)` [ServerRpc, RequireOwnership=false] -> `Player.RPCTeleport` (TargetRpc to owner)
  -> `Player.LocalTeleport(pos, rot, instant=false)` -> `PlayerMovement.Teleport`. Owner is position-authoritative
  (`Player.SendPosRot` each tick -> `Server.UpdatePlayerPosRot`). `OtherPlayer.Teleport` is display-only.
- `SpawnManager` (MonoBehaviour in the island scene) publishes statics: `PlayerSpawnPos` (Vector3), `PlayerSpawnRot` (float yaw),
  `BoatSpawnPos`, `BoatSpawnRot`. Public static fields, settable.

## Items / weapons
- `Item : NetworkBehaviour`; `Item.ID` (byte), `Item.name`, `Item.Type` (ItemType {Item, Fish, Weapon}), `Item.Weapon` (Weapon or null),
  `Item.Cost`, `Item.SyncedHolder`, `SetSyncedHolder(Player, bool forced=false)` (refuses dead players unless forced),
  `DestroyItem(byte reason)`, `Drop(...)`, `PutInInventory()`.
- Registry: `GameInfo.IDToItem(byte)`, `GameInfo.GetSpawnable(string nameLowerNoSpaces)`, populated from `Resources.LoadAll<Item>("Items")`.
- Spawn into hands (server), the `Server.RpcLogic___BuyItem___4197152275` pattern:
  `Item it = Instantiate(GameInfo.IDToItem(id), pos, rot); it.SetSyncedHolder(player); InstanceFinder.ServerManager.Spawn(it.gameObject); player.Holding.SetHeldItem(it);`
  or from any client: `Server.Instance.BuyItem(id, player, player.Holding.HeldItem, pos, rot, isFree:true)`.
- `ItemManager.Instance.SpawnNewItem(Item prefab, Vector3, Quaternion)` server world spawn.
- `PlayerInventory.AddItem(byte slot, Item)` (server, item.SyncedHolder must be player), `ServerTryStoreHeldItem(Item)`,
  `ServerDropAll(pos, rot)`, `ApplySlot(int)`. `PlayerHolding.HeldItem`, `SetHeldItem(Item)`.
- `Weapon : Tool : Item`: `Ammo { get; private set; }` (client-local), `Attachments.AmmoPerMag`, `Damage => Attachments.Damage`,
  `Shoot()`, `LocalReload()`, `TryRefillAmmo()`. `WeaponInfo` (ProjectileType byte, ProjectileDamage, ...).
  `Attachments : NetworkBehaviour` SyncVars for sight/barrel/bullet index/extended mag/laser.
- `ServerSettings.OneShotEnabled` makes damage 99999.

## World / islands
- Main scene index 0 persistent. Islands = scenes 1..N (Island1..5, DevIsland) loaded additively by
  `IslandManager` (`LoadIsland(byte)`, `UnloadIslands()` static, `IsLoading`, `OnFirstIslandLoaded` event).
  Island index synced by `OnlineIslandManager._curIsland` SyncVar<byte>; `OnlineIslandManager.CurIsland`,
  `SpawnIsland(byte)` (server), `TpToSpecificIsland(byte)` static, `ToggleTeleportPlayers(bool)`.
- `Island` MonoBehaviour in island scene: statics `IslandSize` (55), `IslandPos`, `CurIsland`; `Client.SendSpawnPlayer` waits for `Island.CurIsland`.
- `IslandSpawner.OnTriggerEnter` = walk-into-trigger island transition (server).
- Water: `WaterManager.WaterHeight`, `GetWaterHeight(Vector3)`, `IsUnderWater(Vector3)`; infinite 3x3 tiles of 200 m re-centered on player.
- Layers: `GameInfo.LevelLayer`, `CanJumpOnLayers` (ground SphereCast in PlayerMovement.cs:505), `ProjectileHitLayer`, `PlayerLayer(s)`, `ItemLayer`, `BoatLayer`. Use `LevelLayer` for arena geometry.
- Boat: `BoatManager.Instance.TryMoveBoat(pos, rot)`, `TrySpawnBoat(prefab,pos,rot)`.

## Session
- Menu is a GameObject in the main scene (`MainMenuManager.ToggleMenu`, `IsInMenu`). Host: `ButtonManager.CreateNewServer()`, then
  Steam: `SteamManager.CreateLobby()` -> `ConnectionManager.CreateOnlineLobby(8)`; local: `ConnectionManager.CreateOfflineLobby()`.
  Singleplayer is still host+client over localhost.
- Join: Steam invite/friend join only (`SteamManager.OnLobbyEntered`, lobby data "version" must equal `Application.version`).
- Max players 8 (SteamManager). `SteamLobbyAuthenticator` checks lobby membership.
- Player spawn: `Client.SendSpawnPlayer` -> `Server.SpawnPlayer` ServerRpc -> `RpcLogic___SpawnPlayer___1871804056` instantiates
  `GameInfo.PlayerPrefab`, `Spawn(go, conn)`; local client snaps to `SpawnManager.PlayerSpawnPos` in `Player.InitializePlayer`.
- `Server.Instance` singleton, `Server.OnServerStarted/OnServerStopped` static events, `Server.Instance.DynamicObjectsHolder`.
- `InstanceFinder.ServerManager / ClientManager / TimeManager` (FishNet) for broadcasts and ticks.

## Systems to suppress (method -> effect)
- `CreatureManager.TickUpdate()` private (fish spawns); `BirdManager.ServerTickUpdate()` / `AddFlyingBird(Bird)`;
  `AlbatrossSpawner.TickUpdate()`; `BossManager.InitializeBossFight(Creature)` static; `NPCManager.AddNpc(NPC)` static;
  `ItemSpawner.Start()`; `IslandSpawner.OnTriggerEnter(Collider)`; `TutorialManager.AddTutorial(Tutorial)` private;
  `AutoSaver.Start()`; `DecorationManager.ToggleLevelDecorations(bool)` static (kill switch).
- Save: `SaveSystem.SaveServer(string name, string json)`, `SaveSystem.SaveLocal(string)`, `SaveSystem.DeleteServer(string)`;
  `SaveManager.SaveServer(bool)`, `SaveLocal()`, `SavePlayer(...)`, `CreateServer(...)` (writes immediately), `LoadWorldItems()`.
  Files: `%USERPROFILE%\AppData\LocalLow\<company>\<product>\Saves\<name>.txt` and `local.txt`.
- `MoneyManager.AddMoney/RemoveMoney/SellItem` (harmless).
- `EndGameManager`, `EndGameEffects.IsShowingEndGame` (blocks TakeDamage if showing).

## UI
- `PlayerUI.CanvasTextPrefab` (TextMeshProUGUI) + `PlayerUI.FXCanvasTrans`: `Instantiate(prefab, parent)` for HUD text (see OnHitUI.InitializeLocal).
- `PlayerUI.ToggleUIDisabled(bool)`, `ToggleDeathUI(bool)`, `AddHitMarker`, `SetLookAtText`, ...
- `ChatManager.ChatMessage(string)` local chat line (rich text ok); `OnlineChatManager.Instance.SendChatMessage(ulong steamId, string)` ObserversRpc from host.
- `DazedCommands.IsServerCommand(string)` handles "/" commands (needs `ClientSettings.CheatsEnabled`); `ClientCommands` handles "!".
- `GameInfo.WorldTextPrefab` -> `WorldText.SetText(string, float size)` world-space billboard text.
- `CanvasManager.ToggleBlackscreen(bool to, bool instant, float duration)`.
- `GameInfo.CurCamera`. Animations use LeanTween.
