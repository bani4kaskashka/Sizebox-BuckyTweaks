# Bucky Tweaks: technical notes

A BepInEx 5 plugin for Sizebox 3.02 (Unity 2022.3.6, Mono). It does three things:

1. **Less slidey movement.** The player micro used to drift for about half a second when starting, stopping and turning.
2. **Correct character heights.** A character set to 180 cm now actually stands 180 cm tall, measured to the top of the head.
3. **Koikatsu and Koikatsu Sunshine maps** show up in the stage list and load straight from your KK/KKS install.

## Installed where

`Sizebox 3.0/Sizebox v3.02 - Win64/Sizebox v3.02 - Win64/`

| Path | What |
|---|---|
| `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt`, `BepInEx/core` | BepInEx 5.4.23.5 (mod loader) |
| `BepInEx/plugins/SizeboxTweaks/SizeboxTweaks.dll` | this plugin |
| `BepInEx/plugins/ConfigurationManager/` | in-game settings window (press **F1**) |
| `BepInEx/config/sizebox.tweaks.cfg` | settings (editable in F1 or a text editor) |
| `BepInEx/config/BepInEx.cfg` | `HideManagerGameObject = true` (required: Sizebox destroys the plugin host object otherwise and F1 would not work) |
| `BepInEx/cache/SizeboxTweaks/kkmaps/` | Koikatsu map thumbnails (made the first time you visit a map) and the zipmod map index (safe to delete, rebuilt on next start) |
| `BepInEx/LogOutput.log` | log; height measurements and map loading are written here |

**Uninstall:** delete `winhttp.dll`. That alone disables every mod; delete the `BepInEx` folder too to remove them completely. No game file was modified. The original download is still in `Sizebox v3.02 - Win64.zip`.

## 1. Movement

Sizebox moves the micro with `velocity = Lerp(previous, target, movementSmoothing * dt)` and turns it with `turnSmoothing`. The shipped values are 4 and 3, and the walk/run animation lags behind speed by 0.65 s. That is the ice-skating feel. The plugin feeds the controller its own values, with separate acceleration and deceleration.

These numbers were measured in game by the plugin's self test, possessing Unity-chan with scripted input:

| | Vanilla | Tweaked (defaults) |
|---|---|---|
| Standstill to 90% run speed | 0.56 s | 0.18 s |
| Release keys to 10% speed (slide) | 0.52 s | 0.12 s |
| 90° turn: travel direction catches up | 0.50 s | 0.16 s |
| Top run speed | 5.87 u/s | 5.84 u/s (unchanged) |

Settings (`[Movement]`): `Acceleration` 12, `Deceleration` 16, `TurnSpeed` 20, `AnimationBlendTime` 0.15, plus walk/run/sprint multipliers. These default to the game's own 0.5 / 1 / 1.38, so speed is unchanged. Set `Enabled = false` to get vanilla back.

**Jump height** (`JumpHeight`, default 0.3). Sizebox launches a jump at `jumpHeight x scale`, and the shipped value peaks at 1.16 body heights (1.91 m for a 1.65 m person). The setting is the peak as a fraction of the character's own height. The self test measured 0.28 body heights (0.46 m) with the default.

**Giantess speed** (`[Movement (Giantess)]`). A giantess walks by her animation. On top of that, the game's "Giantess Speed Affected by Scale" option scales her speed by `1 - log10(height)/5`, so a 100 m giantess moves at 60% speed and a 1 km one at 40%.
- `SpeedMultiplier` (default **2**) speeds up both the animation and the walking, for player and AI giantesses alike. The self test read 1.48 instead of 0.74 for a 20 m giantess.
- `SpeedChange` (5 to 8) makes her start and stop a bit quicker.
- To remove the size slowdown entirely, turn off "Giantess Speed Affected by Scale" in the game's settings.

## 2. Heights

**Why it was wrong.** Sizebox rescales every character so its measured height is 2 units (micro) or 2000 units (giantess), and the size you type in is based on that measurement. The measurement is the combined renderer bounding box (`bounds.max.y`). Skinned-mesh bounds are padded, and hair, ribbons, ears, hats and stray props push the box up by a different amount on every model. So two models set to the same size end up different heights, and a 180 cm and a 200 cm character can stand head to head.

**Fix.** When a character spawns, the plugin skins its mesh in the rest pose and measures the real top. The default `Mode = Body` uses the highest vertex weighted to the humanoid skeleton: the head skin and face, plus hair only if the hair is weighted to the head. The size you type is then the character's height without hats, ribbons or hair sticking up. `AllMesh` measures to the top of anything. `Vanilla` turns the fix off.

**Verified** with the two Unity-chan models that ship with the game (a giantess and a micro version of the same character), both set to 10 m:

| | Vanilla | Fixed |
|---|---|---|
| Giantess Unity-chan, head bone height | 8.34 m | 8.58 m |
| Micro Unity-chan, head bone height | 7.76 m (7% shorter) | 8.55 m (same) |

The micro's bounding box was 10% taller than her actual body, and the giantess's 3% taller. See `screenshots/heights_before_after.png`. Every spawn writes a line like this to the log:
`Height [Micro] unitychan: box 1.6687  allMesh 1.5603  body 1.5152 ... -> using body (box is +10.1% vs used)`

Notes:
- Only characters spawned after a change use the new measure.
- Saved scenes store each character's scale, not its height. A character saved before installing the fix keeps that scale, but the model is re-normalised with the new measure. So it reloads slightly bigger (Unity-chan micro: about 10%), and its size label now matches its body.
- Micros play random idle animations, and some slouch, so a micro's live head height can sit a few % under the label.

## 3. Koikatsu maps

All 41 Koikatsu maps and 31 Koikatsu Sunshine maps appear in Stage Select as `KK - <name>` and `KKS - <name>`. Nothing is copied or converted: the plugin loads the map bundle from your game folder.

- Koikatsu is read from `KoikatsuFolder` (`D:/Koikatsu BetterRepack RX17`).
- Sunshine is read from `KoikatsuSunshineFolder` (`E:/Koikatsu Sunshine`). If left empty, both are looked up in the registry.

**Why it works.** Sizebox custom stages are scene asset bundles, and after loading one Sizebox swaps every material onto its own shaders by name. Koikatsu map bundles are Unity 5.6 (KKS: 2019.4) scene bundles that still carry type trees, and the 2022 runtime loads them. Their materials are almost all Unity's Standard shader. Koikatsu uses about 1 unit = 1 m, the same as Sizebox, so characters and maps match in scale.

**What the plugin fixes up on load:**
- **Shared bundles.** It loads the map's shared bundles from KK's `abdata/abdata` manifest first. All KKS maps need this, otherwise they render white.
- **Layers.** It moves everything to Sizebox's `Map` layer so the ground is walkable and the camera collides with it.
- **Lights.** KK lights only lit KK's own layer number, so they are re-masked. Character-only rim lights are turned off.
- **Triggers.** It disables KK gameplay trigger volumes.
- **Missing colliders.** It adds colliders to solid meshes that have none. KKS floors have none, so you would fall through, and with them you can stand on desks and benches.
- **Materials.** It maps KK shaders to Sizebox's: transparent becomes Fade, leaves become Cutout. It hides KK's painted sky domes, minimap meshes and screen effects, and makes metal surfaces non-metallic. KK's reflection cubemap does not survive the version change, so metal would render black.
- **Lighting.** It uses realtime lighting from Sizebox's sun and sky instead of KK's baked lightmaps. KK switches day/evening/night lightmaps with its own scripts, so the baked set loads black or at the wrong time of day. `KeepBakedLighting = true` turns this off.
- **Spawn point.** It adds a spawn point on the main floor near the map's origin.
- **Thumbnail.** It saves a thumbnail the first time you visit a map.

**Tested in game:** KK Park, KK classroom 1-1 and KKS Beach. All three load, render and are walkable, with characters at the right scale. The other maps use the same code path but have not each been checked by eye.

**Giantesses on Koikatsu maps.** A giantess walks by pushing a large physics capsule, and that capsule collides with the "Map" layer. On Sizebox's own stages that layer is only the ground. On a Koikatsu map it holds every tree, hedge, lamp, bench, wall and desk, so the capsule gets wedged and knocked sideways. That is the "pulled to one side and can't move" bug.
- With `GiantessWalksThroughProps` on (the default), giantesses only collide with flat floor colliders and walk through everything else. Micros still collide with everything.
- Measured on KK Park, a 20 m player giantess walking 3 s in each direction:

| Direction | Off | On |
|---|---|---|
| Forward | 23 m | 23 m |
| Back | 32 m | 32 m |
| Right | 8 m: blocked, climbed 6 m up a prop, deflected | 27 m |
| Left | 28 m, deflected | 30 m |

- **Micros pulled to one side (classroom and other indoor maps).** Many KK maps have a solid 1.2 km sky sphere collider around the whole map. Every character spawned inside it, and physics kept shoving them toward its edge: in the classroom the player could only move 0.08 m in any direction, always the same way. These enclosing colliders are now switched off. Thin sea/ground planes are kept. After the fix, the classroom player walks 2-8 m each way, limited only by desks and walls.
- **Spawn point.** It now stands on free floor: if furniture (the teacher's lectern) or a wall is in the way, it walks out to clear floor at the same level. It is placed at eye height (about 1.7 m indoors, lower under low ceilings, 3 m outdoors) because Sizebox's floating camera starts there and sets its speed from its height above the ground. Before, it started on the floor, tiny and crawling. Measured: 1.6 m and 1.6 u/s in the classroom, 3 m and 3 u/s in the park, versus 2 m on Sizebox's own Island.

### Mod maps (sideloader zipmods)

Maps from sideloader zipmods in each game's `mods` folder are listed too, as `KK mod - <name>` and `KKS mod - <name>`. With the BetterRepack mods that is 247 KK and 223 KKS mod maps, minus the two noted below. `IncludeModMaps = false` turns them off.

- **Names** come from the mod's Studio map list (`abdata/studio/info/**/Map_*.csv`). Without one, the name comes from `manifest.xml`, and without that, from the bundle file name.
- **Old versions.** Repacks often keep several versions of a map pack side by side. Like the sideloader, the plugin only lists the newest version of each mod GUID. If two mods ship the same `abdata/map/scene/...` bundle, only the newest one is listed.
- **No unpacking.** Zipmod entries are stored uncompressed, so Unity opens the scene bundle inside the zip at its file offset. Nothing is extracted. The rare deflated entry is unpacked once into `BepInEx/cache/SizeboxTweaks/kkmaps/extracted/`.
- **Dependencies.** A mod that ships its own `abdata/abdata` manifest gets its shared bundles loaded first. They are looked up in the same zipmod, then the game's `abdata`, then the other zipmods.
- **Same fix-ups as vanilla maps** (layers, lights, colliders, materials, spawn point, thumbnail). Most mod maps have few colliders, so `AddMissingColliders` matters most here.
- **One mod map open at a time.** The KK and KKS builds of a map share internal file names, and Unity will not open both, so switching mod maps closes the previous bundle.
- **Start-up cost.** The first start reads every zipmod's file list, about 40 s for 5,500 KK zipmods on a hard disk. After that the result is cached in `kkmaps/zipmod_index_KK.txt` / `_KKS.txt` and the scan takes about 0.2 s. Only new or changed zipmods are read again.
- **Tested in game** with the Heights self test (Unity-chan and a KK student at 1.65 m): KK mod Pool, KK mod `[PDHM] Paris`, KKS mod `Classroom v2` (from a 10-map pack) and KKS mod `[Ruine02] TrailblazerRoom` (the one deflated zipmod). All four load and render, and the characters stand on the floor at true height. Paris shipped with no colliders at all and Classroom v2 with 2; the plugin added 241 and 166. Indoor mod maps can look dim, because only Sizebox's sun and sky light them.
- **Left out:** maps whose scene bundle was repacked by hand (a `CAB-...` file instead of Unity's `BuildPlayer-<scene>.sharedAssets`). Koikatsu's Unity 5.6 opens them but Unity 2022 cannot, and says the scene "has not been added to the build settings". In this install that is `[Among Us] The Skeld` and `[Splatoon 2] Abandoned Station`. The log names them at start-up.

**Known limits:**
- **KK effects** (water distortion, mirrors, particles) are hidden, not converted.
- **Spawn points** sometimes land at the edge of a big outdoor map (KKS Beach spawns on the sand near the rocks). You can walk or fly from there.
- **Map size:** maps use KK's own size. To make a map giant, use Sizebox's normal scaling of characters instead.
- **Bad maps:** if one map misbehaves, list its file name in `SkipMaps` (for example `livestage_s,church_volumelight`).

## Building

```bash
dotnet build -c Release
```

It needs the .NET SDK. References come from the game's `Sizebox_Data/Managed` and `BepInEx/core` (path set in `SizeboxTweaks.csproj`). The build copies the DLL into `BepInEx/plugins/SizeboxTweaks` automatically.

## Developer self test

Set `[Dev] SelfTest` in the config:
- `Heights` loads a stage, spawns models side by side at `TestHeightMeters` and screenshots them (`heights_<mode>_<stage>.png`).
- `Movement` possesses a micro and measures start, stop and turn times.
- `Scene` loads `TestScene` and screenshots it.
- `Drift` loads `TestScene`, possesses a micro, spawns AI micros and a 20 m giantess, then drives the micro and the giantess 4 ways and logs how far each one got.

Results go to `BepInEx/selftest/` and the log. Leave it `Off` for normal play.

## 1.1.0 additions

- **Jump landing.** After a jump the controller ignores the ground for `jumpCooldown` seconds. The game's 1 s matched its 1.2-body-height jump; with a normal jump you landed after 0.7 s and stood "in the air" for the rest (jump animation, air control only). The cooldown now scales with flight time. Measured: grounded again 60 ms after landing.
- **Stairs.** KK pairs visible meshes with invisible `X_d` collision stand-ins, and stairs get a smooth tilted ramp. Visible meshes that have a stand-in no longer get extra colliders (27 on KK 1F). For real stepped stairs (mod maps) the micro now steps up ledges up to `StepHeight` (default 0.15 body heights). Synthetic test, 18 cm steps: 1.04 m of 1.80 m climbed without the helper, all of it with it. A 30 degree ramp works either way.
- **Objects and cars on KK maps** no longer drift; that was the same solid sky sphere. 10 s test: 0 to 0.03 m.
- **Stage select** names are fitted to one line instead of wrapping onto overlapping lines.
- **Main menu** shows "Bucky Tweaks Version: x" in the bottom-left corner.
- **Graphics API.** The build ships Vulkan, DX11 and OpenGL shaders as well as DX12, so `-force-vulkan` works natively (DXVK can't help: it doesn't do DX12). `GraphicsApi` restarts the game once with the flag. The DOORSTOP_* environment variables are cleared for the child process, otherwise BepInEx skips loading in it. KK Park, uncapped: DX12 1067 fps average, Vulkan 793. The driver timeouts during testing all happened on DX12 while other Unity processes were using the GPU.
- **BepInEx.cfg.** The plugin turns `HideManagerGameObject` on by itself; a restart is needed for F1.

## Menus (1.2.0)

Turn it all off with `[UI] ModernMenus = false`; the blur alone with `BlurPauseBackground`.

- **One look.** Every Sizebox settings window is the same `SettingsMenu` prefab: a title, a 500x450 scroll box and 400x27 rows in three fonts (SF Atarian, two OpenSans weights). `SettingsSkin` (postfix on `Pause.SettingsView.Awake`) turns it into a 640x470 dark card: labels on the left, every control lined up on the right, OpenSans only, slim sliders, small value boxes. Rounded sprites are made at runtime, nothing is shipped.
- **Pause menu.** Resume, Save, Settings, Change Stage, Restart, Main Menu, Quit. Video, Lighting, Audio and Controls are tabs of Settings (each tab calls the game's own `PauseView.On*Click`, so the windows themselves are the game's). The list hides while a window is open.
- **Blur.** On pause the menu stays invisible for one frame, the screen is grabbed with `ScreenCapture.CaptureScreenshotIntoRenderTexture` and bounced down 5 halvings and up 2 with bilinear filtering. The texture is released when the menu closes.
- **Change Stage.** Every entry of `SceneLoader.GetSceneList()` (built in, custom, Koikatsu) as a tile with a search box. Yes on the prompt unpauses and calls `SceneLoader.LoadScene`, the same path as Restart.
- **Video > System.** The game's build/system/feature rows become one list. "Running on" reads `SystemInfo.graphicsDeviceType`, so it says what is really in use. The Graphics API picker writes the config and offers to restart now (strips old `-force-*` flags, adds the new one).
- **Prompts** (restart, quit, change stage) get the same card style.
- **Koikatsu thumbnails** are now 512x512, rendered from every game camera in depth order (sky included) with the player layer hidden. Old 16:9 ones are cropped to a square at startup and re-shot on the next visit. The new picture replaces the cached sprite, so the list shows it without a restart.
- **Dropdowns.** The game's dropdown template is only the item toggle in a bare object (no background, no scroll view), so long lists ran over the window. It is rebuilt like Unity's own template: card, viewport with `RectMask2D`, content, item; 230 px tall and scrolling, with a hover row and an accent bar on the current value.
- **Ticks.** The game's checkmark sprite is dark, so tinting it did nothing. Toggles use a generated accent square with a white tick.
- **Path rows** (Content settings): the path text was a separate child sticking out of its box; it now sits inside the field (end of the path shown when it is long), with View/Browse buttons on the right.
- **Cursor.** Sizebox hides and locks the cursor when its open-menu count reaches zero. While the pause menu is open the cursor is kept free, and the first time something hides it a warning is logged (plus a stack trace from `StateManager.Mouse.Remove`).
- **Colours.** Cards use the game's own menu gray (66, 66, 66) with lighter grays for fields and tracks; the blue accent stays.
- **In-game controls.** `EditSkin` (added to `GuiManager.EditMode`) rescans the edit HUD twice a second and styles what it hasn't seen: SF Atarian text becomes OpenSans at about 60% of the point size, sliders/toggles/fields/dropdowns/scrollbars get the menu style, and dark backing panels get the gray. Colours the game changes itself (selected category) are left alone.

## 1.3.0

- **Solid characters.** Walking into another character shoved it away: the player controller sets its velocity directly and a same-size NPC has the same mass. Measured 17.9 m pushed in 3 s. Raising NPC mass would change the game's crush maths, so instead `Physics.ContactModifyEvent` makes the other character immovable only in contacts with the player (`otherInverseMassScale = 0`). Now 0.0 m; you stop against them. `Movement > SolidCharacters`.
- **Stage select gallery.** A small grid button under the "next" arrow opens the Change Stage grid; picking a tile selects that stage (Start still loads). Keys of the stage select are ignored while the grid is open (space in the search box would otherwise start the game).
- **Koikatsu characters** (beta, `[Koikatsu Characters] Enabled`): listed first in its F1 section, turning it on works without a restart (reopen the catalog); dev keys are under Advanced.
- **Movement** a touch smoother: Acceleration 12 to 10, Deceleration 16 to 13, TurnSpeed 20 to 12 (configs on the old defaults are moved over once).

## 1.4.0

- **Spawn size.** Catalog spawns used `InterfaceControl.lastMicroScale`, the size of the last selected micro; after shrinking one with the gun every new spawn was millimetres tall. The getter now returns the map's starting size (`Gameplay > SpawnAtNormalSize`).
- **Koikatsu maps** get a `MapSettings` with `minPlayerSize = 0.1` like Island (without one the game allows 0.001).
- **Restart** on custom and Koikatsu stages: the game reloads by Unity scene name, which doesn't match those entries, and forgets the loaded entry after loading. The last `SceneLoader.LoadScene(SceneInfo)` is remembered and reloaded. Tested on KK Ground: reloads in 1 s (before: nothing).
- **Void rescue.** The last spot the player stood on solid ground for 1 s is kept; falling with nothing at all below for 1.2 s puts them back there. Gives up after 3 rescues in 10 s. Tested: back after 1.5 s.
- **Giantess and bodies.** SolidCharacters only holds living characters at least half your size, and never when you play a giantess.
- **Jump** 0.3 to 0.38 body heights. **Camera** wheel zoom x2.5, Shift + wheel height x1.8 (`Camera` section). Edit mode uses a different camera (`SimCam`, own `_zoomSpeed`), sped up by the same setting.
- **Quality preset** is three buttons that drive the game's own preset dropdown.
- **Quick save / load** (F5 / F9, `Hotkeys`): saves `Quicksave.json` like the Save menu does. Loading in a stage needed one fix: reading a save raises the game's "rebuild the scene" flag at once, so the running stage rebuilt itself just before being unloaded and the new one came up empty. The flag is held until the new stage is loaded. Saves now store the stage list name for custom and Koikatsu stages (the Unity scene name didn't match anything in the list), so those load too.
- **Undo / redo** (Ctrl+Z / Ctrl+Alt+Z, edit mode, 30 steps; every applied step hands back its opposite for the other list, and a new edit clears redo): Delete hides the thing instead of destroying it (destroyed once it drops off the list or the stage changes); moves, rotations and resizes of the selected thing are recorded once they settle with no mouse button held.
- **Favourites and recents.** Right-click a stage tile or a catalog tile to star it; starred stages, then the last 8 played, come first in the stage grid; starred catalog entries come first in their catalog. Kept in `BepInEx/config/BuckyTweaks.qol.json`.
- **Height under the mouse** in edit mode: name and height of the character you point at.
- **Spawn height per stage**: a Height field next to "Play as spawned" in the Micro and Macro catalogs; catalog spawns on that stage get exactly that height (empty = normal).
- **Photo mode** (F2): hides every menu and HUD until pressed again; the screenshot key still works.
