# Height Ruler: how to rebuild it

A 2 m surveyor post for Sizebox, spawned from the Objects list like any other object. It is a
normal `.object` asset, not a plugin feature.

Files in this folder:

| File | What |
|---|---|
| `make_ruler.py` | Blender script that builds the mesh and exports `HeightRuler.fbx` and `height_ruler.blend` |
| `HeightRuler.fbx` | the model, 1 unit = 1 m, Y up, origin at the bottom centre |
| `height_ruler.blend` | the Blender scene, for looking at it |
| `preview.png` | render of the current mesh |

The finished object lives in the game at `Models/Objects/Height Ruler/height ruler.object`
(plus `Height RulerThumb.png`, the catalog icon).

## 1. Regenerate the FBX (Blender 5.2)

```
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --python make_ruler.py
```

Everything is procedural: post height, band size, tick lengths, flag size and the label texts
are constants at the top of the script. The post is red/white 10 cm bands with black ticks every
10 cm (longer at 50 cm, full width at 1 m) and label flags with "0.5 m", "1 m", "1.5 m", "2 m"
as real text geometry on the front and the back. Labels use Arial Bold from `C:\Windows\Fonts`
when present (Blender's built-in font draws "1" like an "I").

## 2. Export the .object (Sizebox SDK, Unity 2017.3.1f1)

Unzip SizeboxSdk.zip (from the official Sizebox SDK download) anywhere. The editor was installed with
`unity install 2017.3.1f1 -m linux` (Linux Build Support is required: the SDK exporter builds
bundles for StandaloneLinuxUniversal, which the Windows game loads fine).

1. Copy the FBX into the project as `Assets\Import\Object\HeightRuler\Height Ruler.fbx`.
   The file name is the name shown in the Objects list, the folder name becomes the bundle name.
2. Run the headless export (one line):

```
"<Unity 2017.3.1f1>\Editor\Unity.exe" -batchmode -quit -projectPath "<path to SizeboxSdk>" -executeMethod Assets.SizeboxSDK.Editor.BatchExport.Run -exportAsset "Assets/Import/Object/HeightRuler/Height Ruler.fbx" -logFile "%TEMP%\sizebox_export.log"
```

   `BatchExport.cs` (a copy is in this folder; put it in the SDK project's `Assets\SizeboxSDK\Editor`) opens the preview scene, selects the FBX and
   calls the SDK's own "Sizebox SDK > Export Model", so the result is the same as using the menu
   in the editor. The first export of a fresh project takes a few minutes (Unity imports the
   whole SDK project once).
3. The bundle lands in `Assets\Export\heightruler (object)\`: `heightruler.object` and
   `Height RulerThumb.png`. Copy both into the game's `Models\Objects\Height Ruler\` folder.
4. Optional check without starting the game (UnityPy):

```
python -I -c "import UnityPy; env = UnityPy.load(r'<path to heightruler.object>'); print([(o.type.name, getattr(o.read(), 'm_Name', '')) for o in env.objects if o.type.name in ('MonoBehaviour', 'Texture2D', 'Material')])"
```

   It must list the `modellist` StringHolder, `Height RulerThumb` and the four materials.

## What the game needs from the bundle

`Models/Objects/*.object` files are plain Unity asset bundles. The game looks for a
`StringHolder` asset named `modellist` with the model names, and per model a `<name>Thumb`
texture (the catalog needs it), the model GameObject `<name>` and optionally a `<name>_shaders`
list. The bundle must be built with a Unity no newer than the game's (2022.3.6f1); 2017.3 is
fine, the shipped unitychan.micro was even built with Unity 5.5.
