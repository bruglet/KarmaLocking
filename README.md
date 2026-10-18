# KarmaLocking

![Static Badge](https://img.shields.io/badge/CLAUDE-VIBECODED?style=for-the-badge&logo=claude&label=VIBECODED&labelColor=%23333333&color=%23de7356)

![Icon](https://raw.githubusercontent.com/TheElevatedOne/KarmaLocking/refs/heads/main/icon.png)

Risk of Rain 2 BepInEx plugin: click a selected Karma level a second time to lock Karma at it for the run.
The user-facing description (also the Thunderstore README) is in [README_MOD.md](README_MOD.md).

## Requirements

- .NET SDK (any version that can build `netstandard2.1`)
- Risk of Rain 2 installed through Steam
- An r2modman profile with BepInExPack and HookGenPatcher whose `MMHOOK_RoR2.dll` was regenerated
  for the current game version (launch the game once with that profile after an update).
  The default is the `Hallowed Ready` profile; the `Default` profile's MMHOOK predates the Karma update.
- Python 3 with Pillow, only to regenerate the images (`make icons`)

## Building

```sh
make build                     # bin/Release/KarmaLocking.dll
make install                   # copy into the profile's BepInEx/plugins/ELEVATED-KarmaLocking
make package                   # dist/ELEVATED-KarmaLocking-<version>.zip, ready to upload to Thunderstore
make install PROFILE_DIR="$HOME/.config/r2modmanPlus-local/RiskOfRain2/profiles/Default"
```

`package.sh` validates the icon (256x256 PNG), the version (thunderstore.conf vs `PluginVersion` in the source),
the description length and the dependency strings, then writes `manifest.json` and zips
`manifest.json`, `icon.png`, `README.md` (from README_MOD.md), `CHANGELOG.md` and the DLL.

To release a new version, bump `VERSION` in `thunderstore.conf`, `PluginVersion` in `src/KarmaLockingPlugin.cs`
and `<Version>` in `KarmaLocking.csproj`, add a `CHANGELOG.md` entry, then `make package`.

## Layout

| Path | Purpose |
| --- | --- |
| `src/KarmaLockingPlugin.cs` | Plugin entry point, config and all hooks |
| `src/KarmaLock.cs` | Lock state (host-owned, mirrored on modded clients) |
| `src/LockNetwork.cs` | Host/client handshake and state sync that never reaches vanilla players |
| `src/AssemblyInfo.cs` | `SearchableAttribute.OptIn` so the game finds the console commands and message handler |
| `src/LockBadges.cs` | The lock overlay for `RuleChoiceController` and `KarmaIcon` |
| `src/LockSprite.cs` | Loads the embedded `assets/lock.png` |
| `tools/make_icons.py` | Generates `icon.png` and `assets/lock.png` |
| `tests/TestDriver/` | Test-only plugin that scripts an in-game singleplayer session (never packaged) |

## Server-side design

Modelled on other host-only mods (itsschwer's ServerSider, 6thmoon's DifficultySelection, Wolfo's QoL):

- **No catalog changes.** An earlier draft added a `Karma.Lock` rule; that changes the rule list vanilla clients
  deserialize, so it was dropped. The lock lives in host memory and is only enforced in `KarmaManager.ModifyKarma`.
- **No R2API hard dependency**, so R2API never adds this mod to the network mod list (which would block vanilla players).
- **Client -> host** uses `ExecuteOnServer` console commands (`karmalock_hello`, `karmalock_set`). They ride the vanilla
  "send console command" call; a vanilla host just logs "not a recognized ConCommand".
- **Host -> client** uses a custom UNet message (0x4B4C), sent only to connections that said hello. A vanilla peer that
  receives an unknown message ID drops the *rest of that packet* (`NetworkConnection.HandleReader`), so broadcasting
  it to everyone (as WolfoLibrary does) could lose unrelated messages for vanilla players.
