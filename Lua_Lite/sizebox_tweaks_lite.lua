-- Sizebox Tweaks Lite: Lua only, no BepInEx needed.
--
-- Install: copy this file into Sizebox_Data/StreamingAssets/lua/ (next to spawn_gts_key.lua).
-- Do NOT use it together with the SizeboxTweaks BepInEx plugin: both change the same things
-- and the giantess speed would be applied twice.
--
-- Sizebox's Lua API only reaches two of the plugin's fixes:
--   * jump height (the game's jump goes about 1.2 body heights up)
--   * giantess speed (the game slows big giantesses down)
-- The smoother walking, the height fix, Koikatsu maps and the first-person camera need the
-- BepInEx plugins; Lua cannot get at those parts of the game.

-- How high a micro jumps, as a fraction of their own height. 0.3 = about 50 cm for a 1.65 m person.
JUMP_BODY_HEIGHTS = 0.3

-- Giantess animation/walk speed. 1 = game default. This is the same global giantess speed
-- the game's own speed keys change (max 4).
GIANTESS_SPEED = 2.0

-- A micro's take-off speed is jumpPower * scale against gravity 9.8 * scale, and a micro is
-- 2 units tall per unit of scale, so the peak is jumpPower^2 / (4 * 9.8) body heights.
local function jumpPowerFor(bodyHeights)
    return math.sqrt(4 * 9.8 * bodyHeights)
end

local nextCheck = 0
local appliedSpeed = false

local function apply()
    local settings = Game.GetLocalPlayerSettings()
    if settings ~= nil then
        settings.jumpPower = jumpPowerFor(JUMP_BODY_HEIGHTS)
    end
    -- Only once, so the game's own speed keys still work afterwards.
    if not appliedSpeed and gts ~= nil then
        gts.globalSpeed = GIANTESS_SPEED
        appliedSpeed = true
    end
end

function Start()
    apply()
end

function Update()
    -- Re-apply every few seconds: the player settings can be reset when you switch characters.
    if Time.time >= nextCheck then
        nextCheck = Time.time + 3
        apply()
    end
end
