using System.IO;
using UnityEngine;
// You must add a reference to Newtonsoft.Json.dll in your project
using Newtonsoft.Json; 

namespace MapHack
{
    public class ConfigData
    {
        public float IconScale { get; set; } = 1.75f;
        public bool RingEnabled { get; set; } = true;
        public string MinHeightColor { get; set; } = "Blue";
        public string MaxHeightColor { get; set; } = "Red";
        public float RingActivationHeight { get; set; } = 0.5f;
    }
    
    public static class ModConfig
    {
        // Define the base directory for all our mod's config files
        private static readonly string ModBaseDirectory = Path.Combine(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..")), // Gets the game's root folder
            "config",
            "MapHack" // Our dedicated subfolder
        );
        
        // Define the full path to our JSON file
        private static readonly string configPath = Path.Combine(ModBaseDirectory, "MapHack.json");
        
        // This property holds the actual settings data
        public static ConfigData Config { get; private set; }

        public static void LoadConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    Config = JsonConvert.DeserializeObject<ConfigData>(json);
                    Plugin.Log($"Configuration loaded successfully from: {configPath}");
                }
                else
                {
                    Config = new ConfigData();
                    Plugin.Log("No config file found. Creating a default one.");
                    SaveConfig();
                }
            }
            catch (System.Exception e)
            {
                Plugin.LogError($"Error loading config, creating a default. Error: {e.Message}");
                Config = new ConfigData();
                SaveConfig();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                // This ensures the "config/MapHack" directory is created if it doesn't exist.
                Directory.CreateDirectory(ModBaseDirectory);

                string json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(configPath, json);
            }
            catch (System.Exception e)
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