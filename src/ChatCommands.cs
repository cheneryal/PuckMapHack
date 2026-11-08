using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MapHack
{
    [HarmonyPatch(typeof(UIChat), "SubmitMessage")]
    public static class ChatCommands
    {
        private static readonly List<string> ValidColors = new List<string>
        {
            "green", "yellow", "red", "cyan", "blue", "white"
        };

        private static readonly List<string> ValidModes = new List<string>
        {
            "vanilla", "center", "top", "bottom"
        };

        [HarmonyPrefix]
        public static bool Prefix(UIChat __instance)
        {
            try
            {
                var textField = Traverse.Create(__instance).Field("textField").GetValue<UnityEngine.UIElements.TextField>();
                if (textField == null) return true;

                string message = textField.value;
                if (string.IsNullOrEmpty(message)) return true;

                if (message.StartsWith("/"))
                {
                    string[] parts = message.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) return true;

                    string command = parts[0].ToLower();
                    string[] args = parts.Skip(1).ToArray();

                    bool commandIntercepted = false;

                    try
                    {
                        switch (command)
                        {
                            case "/maphack":
                                if (args.Length > 0 && args[0].ToLower() == "help")
                                ShowHelp(__instance);
                                else
                                    MapHackUI.Instance?.Toggle();
                                commandIntercepted = true;
                                break;
                            case "/mh":
                                if (args.Length > 0 && args[0].ToLower() == "help")
                                ShowHelp(__instance);
                                else
                                    MapHackUI.Instance?.Toggle();
                                commandIntercepted = true;
                                break;
                            case "/scale":
                                SetIconScale(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/ring":
                                SetRingEnabled(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/mincolor":
                                SetMinHeightColor(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/maxcolor":
                                SetMaxHeightColor(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/ringheight":
                                SetRingActivationHeight(args, __instance);
                                commandIntercepted = true;
                                break;

                            // --- New Commands for Ideas 1, 2, & 3 ---
                            case "/mode":
                                SetMapMode(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/mapscale":
                                SetCustomMapScale(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/mapoffset":
                                SetMapOffset(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/ghost":
                                SetGhostMode(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/ghostalpha":
                                SetGhostAlpha(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/fade":
                                SetProximityFade(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/fadedist":
                                SetFadeDistances(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/fadealpha":
                                SetFadeAlphas(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/radar":
                                SetRadarEnabled(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/radarsize":
                                SetRadarRadius(args, __instance);
                                commandIntercepted = true;
                                break;
                            case "/dumpmap":
                                MinimapPatcher.DumpMinimapHierarchy();
                                SendResponse(__instance, "Minimap hierarchy dumped to Player.log.");
                                commandIntercepted = true;
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Plugin.LogError($"Error processing command '{command}': {e}");
                        SendResponse(__instance, "An internal error occurred while processing your command.");
                        commandIntercepted = true;
                    }

                    if (commandIntercepted)
                    {
                        __instance.StopInput();
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogError($"Chat Error: {e}");
            }

            return true;
        }

        private static void ShowHelp(UIChat chat)
        {
            string helpMessage =
                "<b>MapHack Commands:</b>\n" +
                "* <b>/mode [vanilla|center|top|bottom]</b> - Swaps minimap layout & orientation.\n" +
                "* <b>/mapscale [val]</b> & <b>/mapoffset [x] [y]</b> - Adjusts map size & %-offset.\n" +
                "* <b>/ghost [on|off]</b> & <b>/ghostalpha [0-1]</b> - Strips white ice background.\n" +
                "* <b>/fade [on|off]</b> - Auto-fades map when puck is near your stick.\n" +
                "* <b>/fadedist [min] [max]</b> & <b>/fadealpha [close] [far]</b> - Tunes proximity fade.\n" +
                "* <b>/radar [on|off]</b> & <b>/radarsize [px]</b> - Toggles 360° crosshair radar ring.\n" +
                "* <b>/scale [val]</b> | <b>/ring [on|off]</b> | <b>/mincolor</b> | <b>/maxcolor</b> | <b>/ringheight</b>";
            SendResponse(chat, helpMessage);
        }

        private static void SetMapMode(string[] args, UIChat chat)
        {
            if (args.Length == 0)
            {
                SendResponse(chat, $"Usage: /mode [{string.Join("|", ValidModes)}]");
                return;
            }

            string mode = args[0].ToLower();
            if (ValidModes.Contains(mode))
            {
                ModConfig.Config.MapMode = mode;
                ModConfig.SaveConfig();
                MinimapPatcher.ForceRefreshLayout();
                SendResponse(chat, $"Minimap mode set to '{mode}'.");
            }
            else
            {
                SendResponse(chat, $"Invalid mode. Options: {string.Join(", ", ValidModes)}");
            }
        }

        private static void SetCustomMapScale(string[] args, UIChat chat)
        {
            if (args.Length > 0 && float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float scale) && scale > 0.1f)
            {
                ModConfig.Config.CustomMapScale = scale;
                ModConfig.SaveConfig();
                MinimapPatcher.ForceRefreshLayout();
                SendResponse(chat, $"Custom map scale set to {scale.ToString(CultureInfo.InvariantCulture)}.");
            }
            else
            {
                SendResponse(chat, "Usage: /mapscale [value] (e.g., /mapscale 1.25)");
            }
        }

        private static void SetMapOffset(string[] args, UIChat chat)
        {
            if (args.Length >= 2 &&
                float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(args[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float y))
            {
                ModConfig.Config.MapOffsetX = x;
                ModConfig.Config.MapOffsetY = y;
                ModConfig.SaveConfig();
                MinimapPatcher.ForceRefreshLayout();
                SendResponse(chat, $"Map offset set to X: {x}%, Y: {y}%.");
            }
            else
            {
                SendResponse(chat, "Usage: /mapoffset [x%] [y%] (e.g., /mapoffset 0 10)");
            }
        }

        private static void SetGhostMode(string[] args, UIChat chat)
        {
            if (TryParseToggle(args, ModConfig.Config.GhostMode, out bool enabled))
            {
                ModConfig.Config.GhostMode = enabled;
                ModConfig.SaveConfig();
                MinimapPatcher.ForceRefreshLayout();
                SendResponse(chat, $"Ghost background mode {(enabled ? "enabled" : "disabled")}.");
            }
            else
            {
                SendResponse(chat, "Usage: /ghost [on|off]");
            }
        }

        private static void SetGhostAlpha(string[] args, UIChat chat)
        {
            if (args.Length > 0 && float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float alpha) && alpha >= 0f && alpha <= 1f)
            {
                ModConfig.Config.GhostBackgroundAlpha = alpha;
                ModConfig.SaveConfig();
                MinimapPatcher.ForceRefreshLayout();
                SendResponse(chat, $"Ghost background alpha set to {alpha.ToString(CultureInfo.InvariantCulture)}.");
            }
            else
            {
                SendResponse(chat, "Usage: /ghostalpha [0.0 - 1.0] (e.g., /ghostalpha 0.05)");
            }
        }

        private static void SetProximityFade(string[] args, UIChat chat)
        {
            if (TryParseToggle(args, ModConfig.Config.ProximityFadeEnabled, out bool enabled))
            {
                ModConfig.Config.ProximityFadeEnabled = enabled;
                ModConfig.SaveConfig();
                SendResponse(chat, $"Proximity auto-fade {(enabled ? "enabled" : "disabled")}.");
            }
            else
            {
                SendResponse(chat, "Usage: /fade [on|off]");
            }
        }

        private static void SetFadeDistances(string[] args, UIChat chat)
        {
            if (args.Length >= 2 &&
                float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float min) &&
                float.TryParse(args[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float max) &&
                min >= 0f && max > min)
            {
                ModConfig.Config.FadeMinDist = min;
                ModConfig.Config.FadeMaxDist = max;
                ModConfig.SaveConfig();
                SendResponse(chat, $"Proximity fade distance set to {min}m (close) - {max}m (far).");
            }
            else
            {
                SendResponse(chat, "Usage: /fadedist [minMeters] [maxMeters] (e.g., /fadedist 4 14)");
            }
        }

        private static void SetFadeAlphas(string[] args, UIChat chat)
        {
            if (args.Length >= 2 &&
                float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float closeAlpha) &&
                float.TryParse(args[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float farAlpha) &&
                closeAlpha >= 0f && closeAlpha <= 1f && farAlpha >= 0f && farAlpha <= 1f)
            {
                ModConfig.Config.CloseOpacity = closeAlpha;
                ModConfig.Config.FarOpacity = farAlpha;
                ModConfig.SaveConfig();
                SendResponse(chat, $"Proximity opacity set to {closeAlpha} (close) and {farAlpha} (far).");
            }
            else
            {
                SendResponse(chat, "Usage: /fadealpha [close 0-1] [far 0-1] (e.g., /fadealpha 0.15 0.85)");
            }
        }

        private static void SetRadarEnabled(string[] args, UIChat chat)
        {
            if (TryParseToggle(args, ModConfig.Config.RadarRingEnabled, out bool enabled))
            {
                ModConfig.Config.RadarRingEnabled = enabled;
                ModConfig.SaveConfig();
                SendResponse(chat, $"Crosshair 360° radar ring {(enabled ? "enabled" : "disabled")}.");
            }
            else
            {
                SendResponse(chat, "Usage: /radar [on|off]");
            }
        }

        private static void SetRadarRadius(string[] args, UIChat chat)
        {
            if (args.Length > 0 && float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float radius) && radius >= 30f)
            {
                ModConfig.Config.RadarRadius = radius;
                ModConfig.SaveConfig();
                SendResponse(chat, $"Radar ring radius set to {radius.ToString(CultureInfo.InvariantCulture)}px.");
            }
            else
            {
                SendResponse(chat, "Usage: /radarsize [radiusPx] (e.g., /radarsize 160)");
            }
        }

        private static bool TryParseToggle(string[] args, bool currentVal, out bool result)
        {
            if (args.Length == 0)
            {
                result = !currentVal;
                return true;
            }
            string s = args[0].ToLower();
            if (s == "on" || s == "true" || s == "1") { result = true; return true; }
            if (s == "off" || s == "false" || s == "0") { result = false; return true; }
            result = currentVal;
            return false;
        }

        private static void SetIconScale(string[] args, UIChat chat)
        {
            if (args.Length == 0)
            {
                SendResponse(chat, "Usage: /scale [value]");
                return;
            }

            if (float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float scale) && scale > 0)
            {
                ModConfig.Config.IconScale = scale;
                ModConfig.SaveConfig();
                SendResponse(chat, $"Icon scale set to {scale.ToString(CultureInfo.InvariantCulture)}.");
            }
            else
            {
                SendResponse(chat, "Invalid scale value. Please provide a positive number.");
            }
        }

        private static void SetRingEnabled(string[] args, UIChat chat)
        {
            if (TryParseToggle(args, ModConfig.Config.RingEnabled, out bool enabled))
            {
                ModConfig.Config.RingEnabled = enabled;
                ModConfig.SaveConfig();
                SendResponse(chat, $"Airborne ring has been {(enabled ? "enabled" : "disabled")}.");
            }
            else
            {
                SendResponse(chat, "Invalid argument. Use 'on' or 'off'.");
            }
        }

        private static void SetMinHeightColor(string[] args, UIChat chat)
        {
            if (args.Length == 0)
            {
                SendResponse(chat, $"Usage: /mincolor [color]. Available: {string.Join(", ", ValidColors)}");
                return;
            }

            string color = args[0].ToLower();
            if (ValidColors.Contains(color))
            {
                ModConfig.Config.MinHeightColor = color;
                ModConfig.SaveConfig();
                SendResponse(chat, $"Minimum height color set to {color}.");
            }
            else
            {
                SendResponse(chat, $"Invalid color. Available colors are: {string.Join(", ", ValidColors)}");
            }
        }

        private static void SetMaxHeightColor(string[] args, UIChat chat)
        {
            if (args.Length == 0)
            {
                SendResponse(chat, $"Usage: /maxcolor [color]. Available: {string.Join(", ", ValidColors)}");
                return;
            }

            string color = args[0].ToLower();
            if (ValidColors.Contains(color))
            {
                ModConfig.Config.MaxHeightColor = color;
                ModConfig.SaveConfig();
                SendResponse(chat, $"Maximum height color set to {color}.");
            }
            else
            {
                SendResponse(chat, $"Invalid color. Available colors are: {string.Join(", ", ValidColors)}");
            }
        }

        private static void SetRingActivationHeight(string[] args, UIChat chat)
        {
            if (args.Length == 0)
            {
                SendResponse(chat, "Usage: /ringheight [value]");
                return;
            }

            if (float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float height) && height >= 0)
            {
                ModConfig.Config.RingActivationHeight = height;
                ModConfig.SaveConfig();
                SendResponse(chat, $"Ring activation height set to {height.ToString(CultureInfo.InvariantCulture)}.");
            }
            else
            {
                SendResponse(chat, "Invalid height value. Please provide a non-negative number.");
            }
        }

        private static void SendResponse(UIChat chat, string message)
        {
            if (chat != null)
            {
                ChatMessage chatMsg = new ChatMessage();
                chatMsg.Content = new Unity.Collections.FixedString512Bytes($"[MapHack] {message}");
                chatMsg.IsSystem = true;

                if (Unity.Netcode.NetworkManager.Singleton != null)
                {
                    chatMsg.Timestamp = Unity.Netcode.NetworkManager.Singleton.ServerTime.Time;
                }

                chat.AddChatMessage(chatMsg, (Units)0, false);
            }
        }
    }
}