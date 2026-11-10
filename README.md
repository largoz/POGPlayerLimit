# POG Player Limit

Sets how many players a lobby you host in **Pit of Goblin** takes, you included: anywhere from
1 to 64. The game's own limit is 6.

Requires [MelonLoader](https://thunderstore.io/c/pit-of-goblin/p/LavaGang/MelonLoader/) and
[POG Config](https://thunderstore.io/c/pit-of-goblin/p/Largo/POG_Config/), which shows the slider:
**MODS → POG Player Limit → Max players (you included)**.

## Good to know

- **Only the host needs this mod.** The game checks the limit on the host's side only; players
  who join you play the unmodded game.
- **Change it before you host.** A lobby keeps the size it was created with.
- **64 is the most Epic Online Services allows** for one lobby, and the game runs its online
  lobbies (public, private and the lobby browser) through it. The slider's maximum is read from
  the game's own copy of the EOS SDK.
- The game is balanced for up to 6 players. Some things scale with the player count; how the
  game plays with many more players has not been tested.

## How it works

The game keeps its limit in one field, `NetworkHandler.m_maxClientCount`, and uses it twice:

1. When it starts, it creates its online service with that number; the service gives it to Epic
   Online Services as the lobby size whenever you host.
2. When a player joins, the host lets them in while the players already connected (the host
   counts) plus the new one are no more than that number.

The mod sets the field and the online service's copy of it when the game starts and whenever
the slider changes.

## Building

```sh
dotnet build POGPlayerLimit.csproj -c Release -p:GameDir="<folder holding MelonLoader/ and Mods/>"
```

`GameDir` defaults to the Gale profile on the author's machine. It needs the game's generated
interop assemblies (`MelonLoader/Il2CppAssemblies`) and `Mods/POGConfig.dll`.

## Testing

`test/run_probe.sh <out dir>` runs the game once with a test driver (`test/PlayerLimitProbe`) that
moves the slider the way a player does, hosts a private lobby, reads the lobby size back from
Epic Online Services, and puts the setting back to its default. It needs the game installed
through Steam and nobody playing at the time.
