using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapHack
{
    public static class MinimapPatcher
    {
        private static readonly FieldInfo puckMapField = AccessTools.Field(typeof(UIMinimap), "puckVisualElementMap");
        private static readonly FieldInfo playerMapField = AccessTools.Field(typeof(UIMinimap), "playerBodyVisualElementMap");
        private static readonly Dictionary<Puck, VisualElement> puckRingMap = new Dictionary<Puck, VisualElement>();
        private static readonly FieldInfo indicatorMaterialField = AccessTools.Field(typeof(PuckElevationIndicator), "material");
        private static readonly FieldInfo maxDistanceField = AccessTools.Field(typeof(PuckElevationIndicator), "maximumDistance");
        private static readonly Dictionary<Puck, PuckElevationIndicator> puckIndicatorMap = new Dictionary<Puck, PuckElevationIndicator>();

        [HarmonyPatch(typeof(UIMinimap), nameof(UIMinimap.AddPuck))]
        public static class AddPuckPatch
        {
            [HarmonyPostfix]
            public static void Postfix(UIMinimap __instance, Puck puck)
            {
                try
                {
                    if (puck == null || puckRingMap.ContainsKey(puck)) return;
                    
                    var puckVisualElementMap = (Dictionary<Puck, VisualElement>)puckMapField.GetValue(__instance);
                    if (!puckVisualElementMap.TryGetValue(puck, out var puckVisualElement)) return;
                    
                    var ringElement = new VisualElement { name = "AirborneRing", pickingMode = PickingMode.Ignore };
                    ringElement.style.position = Position.Absolute;
                    float offset = -(1.5f + 1.5f); // Padding + Thickness
                    ringElement.style.top = offset;
                    ringElement.style.left = offset;
                    ringElement.style.right = offset;
                    ringElement.style.bottom = offset;
                    ringElement.style.borderTopWidth = 1.5f;
                    ringElement.style.borderRightWidth = 1.5f;
                    ringElement.style.borderBottomWidth = 1.5f;
                    ringElement.style.borderLeftWidth = 1.5f;
                    var radius = new StyleLength(new Length(50, LengthUnit.Percent));
                    ringElement.style.borderTopLeftRadius = radius;
                    ringElement.style.borderTopRightRadius = radius;
                    ringElement.style.borderBottomLeftRadius = radius;
                    ringElement.style.borderBottomRightRadius = radius;
                    
                    puckVisualElement.Add(ringElement);
                    puckRingMap.Add(puck, ringElement);
                }
                catch (Exception e) { Plugin.LogError($"Failed in AddPuck patch: {e}"); }
            }
        }

        [HarmonyPatch(typeof(UIMinimap), nameof(UIMinimap.RemovePuck))]
        public static class RemovePuckPatch
        {
            [HarmonyPrefix]
            public static void Prefix(Puck puck)
            {
                if (puck == null) return;
                puckRingMap.Remove(puck);
                puckIndicatorMap.Remove(puck);
            }
        }

        [HarmonyPatch(typeof(UIMinimap), "Update")]
        public static class UIMinimapUpdatePatch
        {
            [HarmonyPostfix]
             public static void Postfix(UIMinimap __instance)
            {
                try
                {
                    float currentIconScale = ModConfig.Config.IconScale;
                    bool ringEnabled = ModConfig.Config.RingEnabled;
                    Color minHeightColor = ModConfig.GetMinHeightColor();
                    Color maxHeightColor = ModConfig.GetMaxHeightColor();

                    var playerMap = (System.Collections.IDictionary)playerMapField.GetValue(__instance);
                    foreach (var value in playerMap.Values)
                    {
                        var valueType = value.GetType();
                        var item1Field = valueType.GetField("Item1");
                        if (item1Field != null)
                        {
                            var playerIcon = (VisualElement)item1Field.GetValue(value);
                            if (playerIcon != null)
                            {
                                playerIcon.style.scale = new StyleScale(new Scale(new Vector2(currentIconScale, currentIconScale)));
                            }
                        }
                    }

                    var puckMap = (Dictionary<Puck, VisualElement>)puckMapField.GetValue(__instance);
                    foreach (var puckEntry in puckMap)
                    {
                        Puck puck = puckEntry.Key;
                        VisualElement puckVisual = puckEntry.Value;
                        if (puck == null || puckVisual == null) continue;
                        
                        puckVisual.style.scale = new StyleScale(new Scale(new Vector2(currentIconScale, currentIconScale)));

                        if (puckRingMap.TryGetValue(puck, out var ringElement))
                        {
                            bool showRing = false;
                            Color newRingColor = minHeightColor;

                            if (ringEnabled)
                            {
                                if (!puckIndicatorMap.TryGetValue(puck, out var indicator))
                                {
                                    indicator = puck.GetComponentInChildren<PuckElevationIndicator>();
                                    puckIndicatorMap[puck] = indicator;
                                }

                                if (indicator != null)
                                {
                                    var material = (Material)indicatorMaterialField.GetValue(indicator);
                                    var maxDistance = (float)maxDistanceField.GetValue(indicator);
                                    
                                    if (material != null && material.HasProperty("_Size"))
                                    {
                                        float normalizedHeight = material.GetFloat("_Size");
                                        float actualHeight = normalizedHeight * maxDistance;

                                        if (actualHeight > ModConfig.Config.RingActivationHeight)
                                        {
                                            showRing = true;
                                            float gradientFactor = Mathf.Pow(normalizedHeight, 1.5f);
                                            newRingColor = Color.Lerp(minHeightColor, maxHeightColor, gradientFactor);
                                        }
                                    }
                                }
                            }
                            
                            ringElement.style.display = showRing ? DisplayStyle.Flex : DisplayStyle.None;

                            if (showRing)
                            {
                                var newStyleColor = new StyleColor(newRingColor);
                                if (ringElement.style.borderTopColor != newStyleColor)
                                {
                                    ringElement.style.borderTopColor = newStyleColor;
                                    ringElement.style.borderRightColor = newStyleColor;
                                    ringElement.style.borderBottomColor = newStyleColor;
                                    ringElement.style.borderLeftColor = newStyleColor;
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogError($"Error in UIMinimap.Update patch: {e}");
                }
            }
        }
    }
}