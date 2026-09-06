using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapHack
{
    public class Plugin : IPuckPlugin
    {
        public static string MOD_NAME = "MapHack";
        public static string MOD_VERSION = "2.2.0"; // Version updated for new config structure
        public static string MOD_GUID = "com.example.maphack";

        private static readonly Harmony harmony = new Harmony(MOD_GUID);

        public bool OnEnable()
        {
            Log("Enabling...");
            try
            {
                // Load the configuration using the new manager
                ModConfig.LoadConfig();

                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) // Client-side check
                {
                    Log("Applying Harmony patches...");
                    harmony.PatchAll();
                    Log("Patches applied successfully.");
                }
                else
                {
                    Log("Server environment detected, skipping patches.");
                }

                Log("Enabled!");
                return true;
            }
            catch (Exception e)
            {
                LogError($"Failed to enable: {e}");
                return false;
            }
        }

        public bool OnDisable()
        {
            try
            {
                Log("Disabling...");
                harmony.UnpatchSelf();
                Log("Disabled!");
                return true;
            }
            catch (Exception e)
            {
                LogError($"Failed to disable: {e}");
                return false;
            }
        }

        public static void Log(string message) => Debug.Log($"[{MOD_NAME}] {message}");
        public static void LogError(string message) => Debug.LogError($"[{MOD_NAME}] ERROR: {message}");
    }
}