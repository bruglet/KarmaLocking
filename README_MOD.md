# Karma Locking

![Static Badge](https://img.shields.io/badge/CLAUDE-VIBECODED?style=for-the-badge&logo=claude&label=VIBECODED&labelColor=%23333333&color=%23de7356)

![Icon](https://raw.githubusercontent.com/TheElevatedOne/KarmaLocking/refs/heads/main/icon.png)

A \[ **server-side / host-only** \] mod. Players without it can still join.

With Karma enabled, the level you pick in the lobby is only where the run *starts*. Karma keeps going up as you play,
so almost every run ends on Karma 7 or 8. This mod lets you **lock** Karma at the level you picked.

## How to use it

In the lobby, open the Karma panel (the edit button next to the Karma rule):

1. **Click a level once**: selects it, same as vanilla. Karma starts there and rises normally.
2. **Click the same level again**: locks it. A small lock appears in the bottom right corner of the icon.
   Karma stays at that level for the whole run.
3. **Click it a third time**: unlocks it. The level stays selected and Karma rises normally again.

Picking a different level (or *Karma Disabled*, or the random button) always starts unlocked.
Level 0 can be locked too, which keeps Karma enabled but at 0 for the whole run.

The lock only applies while the lobby's Karma level is the locked one. If another player's vote moves the level
elsewhere, the run starts unlocked.

## Multiplayer

Only the **host** needs the mod.

| Who has the mod | What happens |
| --- | --- |
| Host only | The host can lock. The lock works for everyone; other players just see Karma not rising. |
| Host and some players | Those players can lock/unlock too (configurable) and see the lock badge. |
| A player but not the host | Nothing changes for them: clicks behave as in vanilla, no badge. |

It adds nothing to the game's content or rule list and never sends anything to players who don't have the mod,
so it doesn't affect who can join a lobby.

## Where the lock shows

For the host and players with the mod, the lock badge appears on:

- the buttons in the lobby's Karma panel
- the Karma summary icon in the lobby's rules list
- the Karma icon during the run
- the Karma icon on the end-of-run report

The server browser can't show it: the lock isn't part of the lobby's public rule data, so players who don't have
the mod are unaffected.

## Configuration

`BepInEx/config/com.elevated.KarmaLocking.cfg`

| Setting | Default | Description |
| --- | --- | --- |
| `AllowClientLocking` | `true` | Host only. Let other players with the mod lock and unlock Karma in your lobby. |

Console commands (open the console with `Ctrl + Alt + ~`):

| Command | Description |
| --- | --- |
| `karmalock_set <level>` | In the lobby: lock Karma at a level (0-8), or `-1` to unlock. |
| `karmalock_status` | Print the current lock state. |

## How it works

- While locked, the host ignores every Karma increase. The starting level is applied as normal.
  Karma is synced by the game itself, so everyone sees the unchanged value.
- Players with the mod tell the host through a console command that the vanilla game already forwards to the host.
  The host then sends the lock state only to those players.

## Compatibility

- Requires the version of the game with the Karma system.
- Changes nothing when Karma is disabled or the lock isn't used.
- Prismatic Trials and Simulacrum disable Karma, so there is nothing to lock.
- Eclipse forces its own starting level; that level can still be locked the same way.
- The lock isn't stored in save files: when continuing a saved run (e.g. ProperSave), lock the level in the lobby again before loading.
