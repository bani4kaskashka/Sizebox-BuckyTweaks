# Sizebox Tweaks Lite (Lua only)

A version for players who don't want BepInEx. It only does what Sizebox's own Lua API can reach:

| Fix | Lua lite | BepInEx plugins |
|---|---|---|
| Normal jump height (0.3 body heights instead of 1.16) | yes | yes |
| Faster giantesses (sets the global giantess speed to 2x) | yes | yes |
| Less slidey walking/turning | no (not exposed to Lua) | yes |
| Correct character heights | no (needs the model mesh at spawn) | yes |
| Koikatsu maps | no (Lua cannot load bundles) | yes |
| PerspectiveX first-person camera | no (Lua has no camera access) | yes |

Install: copy `sizebox_tweaks_lite.lua` into `Sizebox_Data/StreamingAssets/lua/`. Edit `JUMP_BODY_HEIGHTS` and `GIANTESS_SPEED` at the top of the file to taste.

Don't use it together with the SizeboxTweaks plugin: both change the giantess speed, so it would be applied twice.

Tested in game (with the plugin's own movement and speed options off): the jump peaked at 0.28 body heights and the giantess animation speed doubled.
