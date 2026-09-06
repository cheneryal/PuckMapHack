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

        [HarmonyPrefix]
        public static bool Prefix(UIChat __instance)
        {
            try
            {
                // Grab the private text field using Harmony reflection
                var textField = Traverse.Create(__instance).Field("textField").GetValue<UnityEngine.UIElements.TextField>();
                if (textField == null) return true;

                string message = textField.value;

                if (string.IsNullOrEmpty(message)) return true;

                if (message.StartsWith("/"))
                {
                    // split string into command and args
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
                                ShowHelp(__instance);
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
                        }
                    }
                    catch (Exception e)
                    {
                        Plugin.LogError($"Error processing command '{command}': {e}");
                        SendResponse(__instance, "An internal error occurred while processing your command.");
                        commandIntercepted = true; 
                    }

                    // If we processed a command, close the chat UI and prevent the game from sending it to the server
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
            string helpMessage = "<b>MapHack Commands:</b>\n" +
                                 "* <b>/scale [value]</b> - Sets the minimap icon scale (e.g., /scale 1.75).\n" +
                                 "* <b>/ring [on|off]</b> - Toggles the airborne indicator ring.\n" +
                                 "* <b>/mincolor [color]</b> - Sets the ring color at minimum height.\n" +
                                 "* <b>/maxcolor [color]</b> - Sets the ring color at maximum height.\n" +
                                 "* <b>/ringheight [value]</b> - Sets height needed to show the ring (e.g., /ringheight 0.5).\n" +
                                 $"<color=grey>Available colors: {string.Join(", ", ValidColors)}</color>";
            SendResponse(chat, helpMessage);
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
            if (args.Length == 0)
            {
                SendResponse(chat, "Usage: /ring [on|off]");
                return;
            }

            string state = args[0].ToLower();
            if (state == "on")
            {
                ModConfig.Config.RingEnabled = true;
                ModConfig.SaveConfig();
                SendResponse(chat, "Airborne ring has been enabled.");
            }
            else if (state == "off")
            {
                ModConfig.Config.RingEnabled = false;
                ModConfig.SaveConfig();
                SendResponse(chat, "Airborne ring has been disabled.");
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
                // Build the new ChatMessage struct
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