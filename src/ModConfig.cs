using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Newtonsoft.Json;

namespace MapHack
{
    public class ConfigData
    {
        public string ToggleKey { get; set; } = "F7";

        // --- Original Icon & Ring Settings ---
        public float IconScale { get; set; } = 1.75f;
        public bool RingEnabled { get; set; } = true;
        public string MinHeightColor { get; set; } = "Blue";
        public string MaxHeightColor { get; set; } = "Red";
        public float RingActivationHeight { get; set; } = 0.5f;

        // --- Idea 1 & 2: Layout Mode, Ghost Mode, & Proximity Auto-Fade ---
        public string MapMode { get; set; } = "vanilla";
        public float CustomMapScale { get; set; } = 1.0f;
        public float MapOffsetX { get; set; } = 0f;
        public float MapOffsetY { get; set; } = 0f;

        public bool GhostMode { get; set; } = false;
        public float GhostBackgroundAlpha { get; set; } = 0.08f;

        public bool ProximityFadeEnabled { get; set; } = false;
        public float FadeMinDist { get; set; } = 4.0f;
        public float FadeMaxDist { get; set; } = 14.0f;
        public float CloseOpacity { get; set; } = 0.15f;
        public float FarOpacity { get; set; } = 0.85f;

        // --- Idea 3: 3D-to-2D Crosshair Radar Ring ---
        public bool RadarRingEnabled { get; set; } = false;
        public float RadarRadius { get; set; } = 160f;
        public float RadarMaxDistance { get; set; } = 45f;
        public float RadarOpacity { get; set; } = 0.85f;
    }

    public static class ModConfig
    {
        private static readonly string ModBaseDirectory = Path.Combine(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
            "config",
            "MapHack"
        );

        private static readonly string configPath = Path.Combine(ModBaseDirectory, "MapHack.json");

        public static ConfigData Config { get; private set; }

        public static Key ResolvedToggleKey
        {
            get
            {
                if (Config != null && !string.IsNullOrWhiteSpace(Config.ToggleKey) &&
                    Enum.TryParse<Key>(Config.ToggleKey, true, out var k) &&
                    k != Key.None &&
                    Enum.IsDefined(typeof(Key), k))
                {
                    return k;
                }
                return Key.F7;
            }
        }

        public static void LoadConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    Config = JsonConvert.DeserializeObject<ConfigData>(json) ?? new ConfigData();
                    SaveConfig();
                    Plugin.Log($"Configuration loaded successfully from: {configPath}");
                }
                else
                {
                    Config = new ConfigData();
                    Plugin.Log("No config file found. Creating a default one.");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                Plugin.LogError($"Error loading config, creating a default. Error: {e.Message}");
                Config = new ConfigData();
                SaveConfig();
            }
        }

        public static void ResetToDefaults()
        {
            string currentKey = Config?.ToggleKey ?? "F7";
            Config = new ConfigData { ToggleKey = currentKey };
            SaveConfig();
        }

        public static void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(ModBaseDirectory);
                string json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(configPath, json);
            }
            catch (Exception e)
            {
                Plugin.LogError($"Error saving config: {e.Message}");
            }
        }

        private static Color ParseColor(string colorName, Color defaultColor)
        {
            if (string.IsNullOrEmpty(colorName)) return defaultColor;

            switch (colorName.ToLower())
            {
                case "green": return Color.green;
                case "yellow": return Color.yellow;
                case "red": return Color.red;
                case "cyan": return Color.cyan;
                case "blue": return Color.blue;
                case "white": return Color.white;
                default: return defaultColor;
            }
        }

        public static Color GetMinHeightColor()
        {
            return ParseColor(Config?.MinHeightColor, Color.blue);
        }

        public static Color GetMaxHeightColor()
        {
            return ParseColor(Config?.MaxHeightColor, Color.red);
        }
    }
}