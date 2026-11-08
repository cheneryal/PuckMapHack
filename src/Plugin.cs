using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapHack
{
    public class Plugin : IPuckPlugin
    {
        public static string MOD_NAME = "MapHack";
        public static string MOD_VERSION = "3.0.0";
        public static string MOD_GUID = "com.example.maphack";

        private static readonly Harmony harmony = new Harmony(MOD_GUID);

        public bool OnEnable()
        {
            Log("Enabling...");
            try
            {
                ModConfig.LoadConfig();

                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                {
                    Log("Applying Harmony patches...");
                    harmony.PatchAll();
                    MapHackUI.Initialize();
                    Log("Patches & UI initialized successfully.");
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
                MapHackUI.Shutdown();
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