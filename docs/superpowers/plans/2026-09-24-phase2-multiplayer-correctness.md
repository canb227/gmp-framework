# Phase 2 — Multiplayer Correctness Plan

**Spec:** `docs/superpowers/specs/2026-09-24-codebase-cleanup-design.md` (Phase 2 section)
**Branch:** `fix/multiplayer-phase2`, from `refactor/cleanup`. Implemented inline (no subagents).
**Conventions:** camelCase public members, no namespaces, XML doc comments only (no Markdown docs).
**Verify every task:** `dotnet build` (0 errors, ≤4 warnings) · `tests/check_scenes.ps1` · `tests/test_lan_lobby.ps1` · `tests/test_lan_lobby.ps1 -Game`.

## Task 1 — Tests first: extend the in-game smoke test
`--test-game` gains three checks, each printing a `consensus_<name>=<value>` token on `TEST_RESULT`.
`test_lan_lobby.ps1 -Game` fails unless every instance prints the same value for each consensus token.
- **inventory:** every FactoryPlayer (local and remote copies) holds the bootstrap seed item
  (`test_1x1x1cubePLACEABLE` ×1). Local check → `inv_ok=true|false`.
- **claim:** host spawns one `res://game/dev/items/test_1x1x1cube.tscn` after load. At load+3 s every
  peer calls `GameWorld.Claim` on it simultaneously. At evaluation → `consensus_claim=<authority>`
  (must be identical everywhere and name a lobby member).
- **button:** at load+2 s and load+4 s every peer presses the museum `Button` simultaneously. At
  evaluation → `consensus_button=<acceptedPresses>:<spawning>`; expected `2:False`.
Expected before Tasks 2–7: FAIL (inventories not synced, Claim is a no-op, presses are local).

## Task 2 — RPC hardening (`gmp/net/RPCManager.cs`)
- `[RPC]` attribute; `HandleRPC` only invokes methods carrying it (otherwise log + drop). Cache
  `MethodInfo` per (Type, name). Null/unknown method → log, no exception.
- `[RPC(requireAuthority = true)]`: drop unless the sender is the target GMPObject's `authority`.
- `RPCManager.sender` — transport-level sender id, valid only while an RPC handler runs (like
  Godot's `GetRemoteSenderId`). Never trust a peer id passed as an argument.
- `RPCTo(ulong peer, Node | ulong objectID, method, args)` via `Lobby.Send`.
- `RequestFromAuthority(GMPObject target, method, args)` = `RPCTo(target.authority, target.id, …)`;
  the pattern: request → authority validates (first wins) → authority broadcasts `ApplyX` with
  `requireAuthority`.
- Annotate every existing RPC target (`_SpawnScene`, `_DespawnObject`, `_boxInit`, `_SyncInventory`,
  `ChangeColor`).

## Task 3 — Inbound state: merge by object (`GameWorld.StateSync.cs`)
Replace last-tick-per-sender with `pendingStates[objectId] = bytes`, plus `lastStateTick[objectId] =
(sender, tick)`. On receive, per update: drop if same sender and `tick < lastTick`; otherwise store.
A different sender (authority changed) is always accepted. Apply and clear once per physics tick.
Remove `mostRecentUpdates`/`pendingIncomingTicks`.

## Task 4 — Lobby load gate + session reset
- `Lobby.OnDoneLoading`: restore the all-members `doneLoading` gate.
- `GameWorld.ResetSession()` (called from `Lobby.LeaveLobby`): free every spawned root, clear
  registries/pending state, `tickNum = 0`, `started = false`, re-pause the tree. `maxTickSize` is
  computed from a constant (host ×4) instead of being multiplied in place.
- Member `donePreloading/doneLoading` flags reset right after the load gate fires. (Not on START and no
  reset at game start: LOBBY_Control messages from different peers are not ordered relative to START —
  observed a peer's DonePreloading arriving before the host's START.)

## Task 5 — GMPO lifecycle and interpolation
- `GMPObject.Init` calls `ApplyProcessMode()`; `SpawnScene` carries the scene root's exported
  `pauseable` into the init data. Drop `GMPOBox3DCharacter._Ready`.
- One exported `syncLerpRate` (default 10, ≤0 = snap) on the three transform bases;
  `SyncHelpers.LerpTransform` uses the frame-rate-independent weight `1 - exp(-rate·delta)`.
- `GMPObject.OnAuthorityChanged()` hook; `GMPOBox3DBody` remembers its authored body type and
  restores it when it gains authority, switching to Kinematic when it loses it.
- Fix `GMPOBox3DBody.Enable()` (set `enabled = true`).

## Task 6 — Contested actions
- **Claim/Release** (`GameWorld`): `Claim(gmpo)` → request to current authority; authority grants if
  it still is authority and nobody holds it → broadcasts `_ApplyClaim(id, winner)` (sender must be the
  current authority) → every peer sets `authority`, `heldBy[id]`, calls `OnAuthorityChanged`.
  `Release(gmpo)` → holder broadcasts `_ApplyRelease(id)`. Authority stays with the last holder.
- **Grab** becomes async: `StartGrab` requests the claim; the grab begins when `_ApplyClaim` names the
  local peer (if primary is still held).
- **Pickup**: request to the item's authority with the requesting player's object id; authority
  checks the sender controls that player and the item hasn't been taken → broadcasts
  `ApplyPickup(playerId)`; every peer removes the item locally, the winning player's peer adds it.
- **BasicButton**: presses go to the host (buttons are level props, not GMPOs); host accepts one
  press per 0.5 s cooldown → broadcasts `_ApplyPress(presser)`; `ObjectSpawner` spawns only on the host.

## Task 7 — Inventory and held-item sync
- Local player: `inventory.InventoryChanged → SyncInventory()` (subscribed in `AfterInit`).
- Remote copies: `_SyncInventory` (requireAuthority) replaces slots and calls `UpdateEquippedItem`.
- `PlayerSync.equippedSlot` carries `ActiveHotbarSlot`; remote copies apply it (and refresh the held
  item on change). Remove unused `hotbarItems`/`gridItems`.

## Task 8 — Small fixes + docs
- Item → button hover leaves the item name label visible: hide it.
- Level root "not a GMPObject" warning → plain log.
- `UpdatePickTarget` NREs on items with no definition (`FactoryItem.Fetch` → null, e.g. the spawner's
  `test_inert_sphere`): fall back to the item id.
- XML docs on public APIs touched in this phase.
