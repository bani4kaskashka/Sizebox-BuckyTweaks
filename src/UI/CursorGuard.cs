using System;
using HarmonyLib;
using SizeboxUI;
using UnityEngine;

namespace SizeboxTweaks.UI
{
    // Sizebox shows the cursor by counting open menus (StateManager.Mouse Add/Remove); at zero it
    // hides and locks it. If that count goes wrong while the pause menu is open, the mouse freezes
    // inside the menu. Log who did it, once.
    [HarmonyPatch(typeof(StateManager.Mouse), nameof(StateManager.Mouse.Remove))]
    internal static class CursorGuard
    {
        private static bool _logged;

        private static void Postfix()
        {
            if (_logged || Cursor.visible)
                return;
            PauseView pause = UnityEngine.Object.FindObjectOfType<PauseView>();
            if (pause == null || !pause.isActiveAndEnabled)
                return;
            _logged = true;
            Plugin.Log.LogWarning("Cursor hidden while the pause menu is open:\n" + Environment.StackTrace);
        }
    }
}
