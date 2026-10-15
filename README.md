# Bucky Tweaks

A BepInEx plugin for Sizebox 3.02 that fixes stuff that bugged me.

- **Less slidey movement.** Starting, stopping and turning are snappy now, no ice skating.
- **Real heights.** A character set to 180 cm is actually 180 cm tall. The game measured a box around the model, hair and hats included.
- **Normal jump.** About 50 cm instead of jumping over your own head.
- **Stairs.** Walks up steps and stair ramps.
- **Faster giantesses.** 2x by default, because big ones crawl.
- **Koikatsu maps.** Every Koikatsu and Koikatsu Sunshine map shows up in Stage Select, mod maps included. Loaded straight from your KK folder, nothing copied.

Everything can be tweaked in game with F1.

Driver timeouts or crashes? Set `Graphics > GraphicsApi` to `Vulkan` in F1. The game restarts itself once on Vulkan. A bit slower than DX12 on my card, but it didn't crash.

![Heights before and after](screenshots/heights_before_after.png)

## Install

1. Get [BepInEx 5 x64](https://github.com/BepInEx/BepInEx/releases) and unzip it into the Sizebox folder (next to `Sizebox.exe`).
2. Unzip Bucky Tweaks into `BepInEx/plugins`.
3. Optional, for the F1 menu: [ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases) (the BepInEx5 one), same folder.
4. Start the game twice. The first run sets BepInEx up.

Koikatsu maps: if your KK isn't found on its own, set `KoikatsuFolder` / `KoikatsuSunshineFolder` in F1 (or in `BepInEx/config/sizebox.tweaks.cfg`).

To uninstall, delete `winhttp.dll` (turns off every mod) or the `BuckyTweaks` folder.

## Lua only

Don't want BepInEx? [`Lua_Lite/sizebox_tweaks_lite.lua`](Lua_Lite/sizebox_tweaks_lite.lua) does the jump and giantess speed. Lua can't reach the rest. Don't use both.

## Build

```
dotnet build -c Release
```

Needs the .NET SDK and the game installed next to this folder, or pass `-p:GameDir="path to Sizebox"`.

How it all works: [NOTES.md](NOTES.md).

First person camera: [Sizebox-PerspectiveX](https://github.com/bani4kaskashka/Sizebox-PerspectiveX).
