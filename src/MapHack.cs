using System;
using System.Collections;
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
        private static readonly FieldInfo indicatorMaterialField = AccessTools.Field(typeof(PuckElevationIndicator), "material");
        private static readonly FieldInfo maxDistanceField = AccessTools.Field(typeof(PuckElevationIndicator), "maximumDistance");

        private static readonly Dictionary<Puck, VisualElement> puckRingMap = new Dictionary<Puck, VisualElement>();
        private static readonly Dictionary<Puck, PuckElevationIndicator> puckIndicatorMap = new Dictionary<Puck, PuckElevationIndicator>();

        private static UIMinimap currentInstance;
        private static VisualElement mapContainer;
        public static VisualElement MapContainer => mapContainer;

        private static bool originalStyleCaptured = false;
        private static StyleEnum<Position> origPosition;
        private static StyleLength origLeft, origRight, origTop, origBottom;
        private static StyleLength origMarginLeft, origMarginRight, origMarginTop, origMarginBottom;
        private static StyleTranslate origTranslate;
        private static StyleRotate origRotate;
        private static StyleScale origScale;
        private static StyleFloat origOpacity;

        private class ElementBgState
        {
            public StyleColor BackgroundColor;
            public StyleColor UnityBackgroundImageTintColor;
        }
        private static readonly Dictionary<VisualElement, ElementBgState> originalBgStates = new Dictionary<VisualElement, ElementBgState>();

        private static VisualElement radarRoot;
        private static VisualElement radarGuideCircle;
        private static readonly Dictionary<Puck, VisualElement> radarPuckDots = new Dictionary<Puck, VisualElement>();
        private static readonly Dictionary<object, VisualElement> radarPlayerDots = new Dictionary<object, VisualElement>();

        private static float smoothedMapOpacity = 1.0f;
        private static string lastAppliedMode = null;
        private static bool lastAppliedGhost = false;
        private static float lastAppliedGhostAlpha = -1f;
        private static float lastAppliedMapScale = -1f;
        private static Vector2 lastAppliedOffset = Vector2.zero;

        public static void ForceRefreshLayout()
        {
            lastAppliedMode = null;
            lastAppliedGhost = !ModConfig.Config.GhostMode;
            lastAppliedGhostAlpha = -1f;
            lastAppliedMapScale = -1f;
        }

        public static void DumpMinimapHierarchy()
        {
            if (mapContainer == null)
            {
                Plugin.Log("Minimap container not found yet.");
                return;
            }
            Plugin.Log("=== UIMinimap VisualElement Hierarchy ===");
            DumpElementRecursive(mapContainer, 0);
            Plugin.Log("=========================================");
        }

        private static void DumpElementRecursive(VisualElement el, int depth)
        {
            if (el == null) return;
            string indent = new string(' ', depth * 2);
            string classes = string.Join(".", el.GetClasses());
            Plugin.Log($"{indent}- Name: '{el.name}' | Type: {el.GetType().Name} | Classes: '{classes}' | Size: {el.layout.width}x{el.layout.height} | BgColor: {el.resolvedStyle.backgroundColor}");
            foreach (var child in el.Children())
            {
                DumpElementRecursive(child, depth + 1);
            }
        }

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

                    var ringElement = CreateRingElement();
                    puckVisualElement.Add(ringElement);
                    puckRingMap.Add(puck, ringElement);
                }
                catch (Exception e) { Plugin.LogError($"Failed in AddPuck patch: {e}"); }
            }
        }

        private static VisualElement CreateRingElement()
        {
            var ringElement = new VisualElement { name = "AirborneRing", pickingMode = PickingMode.Ignore };
            ringElement.style.position = Position.Absolute;
            float offset = -(1.5f + 1.5f);
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
            return ringElement;
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

                if (radarPuckDots.TryGetValue(puck, out var dot))
                {
                    dot.RemoveFromHierarchy();
                    radarPuckDots.Remove(puck);
                }
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
                    currentInstance = __instance;
                    var puckMap = (Dictionary<Puck, VisualElement>)puckMapField.GetValue(__instance);
                    var playerMap = (IDictionary)playerMapField.GetValue(__instance);

                    EnsureMapContainerResolved(__instance, puckMap, playerMap);

                    float currentIconScale = ModConfig.Config.IconScale;
                    bool ringEnabled = ModConfig.Config.RingEnabled;
                    Color minHeightColor = ModConfig.GetMinHeightColor();
                    Color maxHeightColor = ModConfig.GetMaxHeightColor();

                    string mode = (ModConfig.Config.MapMode ?? "vanilla").ToLower();
                    bool isHorizontalMode = (mode == "top" || mode == "bottom");

                    // 1. Update Layout Mode & Ghost Mode
                    if (mapContainer != null)
                    {
                        UpdateMapLayoutAndGhost(mode, puckMap, playerMap);
                    }

                    // 2. Find Local Reference Position
                    Vector3? refPos = GetReferencePosition(playerMap, out Vector3 refForward);
                    float closestPuckDist = float.MaxValue;

                    // 3. Process Players on Minimap
                    foreach (DictionaryEntry entry in playerMap)
                    {
                        var value = entry.Value;
                        if (value == null) continue;
                        var valueType = value.GetType();
                        var item1Field = valueType.GetField("Item1");
                        if (item1Field != null)
                        {
                            var playerIcon = (VisualElement)item1Field.GetValue(value);
                            if (playerIcon != null)
                            {
                                playerIcon.pickingMode = PickingMode.Ignore;
                                playerIcon.style.scale = new StyleScale(new Scale(new Vector2(currentIconScale, currentIconScale)));

                                // Align directional indicator & keep jersey numbers upright in Horizontal Mode
                                AdjustPlayerIconRotation(playerIcon, value, valueType, isHorizontalMode);
                            }
                        }
                    }

                    // 4. Process Pucks on Minimap & Calculate Closest Puck Distance
                    foreach (var puckEntry in puckMap)
                    {
                        Puck puck = puckEntry.Key;
                        VisualElement puckVisual = puckEntry.Value;
                        if (puck == null || puckVisual == null) continue;

                        if (refPos.HasValue)
                        {
                            float dist = Vector3.Distance(refPos.Value, puck.transform.position);
                            if (dist < closestPuckDist) closestPuckDist = dist;
                        }

                        puckVisual.pickingMode = PickingMode.Ignore;
                        puckVisual.style.scale = new StyleScale(new Scale(new Vector2(currentIconScale, currentIconScale)));

                        if (!puckRingMap.TryGetValue(puck, out var ringElement))
                        {
                            ringElement = CreateRingElement();
                            puckVisual.Add(ringElement);
                            puckRingMap[puck] = ringElement;
                        }

                        bool showRing = false;
                        Color newRingColor = minHeightColor;

                        if (ringEnabled && TryGetPuckElevationColor(puck, minHeightColor, maxHeightColor, out newRingColor))
                        {
                            showRing = true;
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

                    // 5. Apply Proximity Auto-Fade
                    if (mapContainer != null)
                    {
                        if (ModConfig.Config.ProximityFadeEnabled && refPos.HasValue && closestPuckDist < float.MaxValue)
                        {
                            float t = Mathf.InverseLerp(ModConfig.Config.FadeMinDist, ModConfig.Config.FadeMaxDist, closestPuckDist);
                            float targetOpacity = Mathf.Lerp(ModConfig.Config.CloseOpacity, ModConfig.Config.FarOpacity, t);
                            smoothedMapOpacity = Mathf.Lerp(smoothedMapOpacity, targetOpacity, Time.deltaTime * 10f);
                            mapContainer.style.opacity = smoothedMapOpacity;
                        }
                        else if (!ModConfig.Config.ProximityFadeEnabled)
                        {
                            mapContainer.style.opacity = origOpacity;
                            smoothedMapOpacity = 1.0f;
                        }
                    }

                    // 6. Update Idea 3: 3D-to-2D Crosshair Radar Ring
                    UpdateRadarRing(refPos, refForward, puckMap, playerMap, minHeightColor, maxHeightColor);
                }
                catch (Exception e)
                {
                    Plugin.LogError($"Error in UIMinimap.Update patch: {e}");
                }
            }
        }

        /// <summary>
        /// Corrects the directional arrow and jersey number orientation when the map container is rotated -90 degrees.
        /// UIMinimap.Update runs right before our Postfix and sets the vanilla rotation angle on the icon/children.
        /// </summary>
        private static void AdjustPlayerIconRotation(VisualElement playerIcon, object tupleVal, Type tupleType, bool isHorizontalMode)
        {
            if (!isHorizontalMode)
            {
                // When not rotated, let UIMinimap.Update's own rotations stand untouched
                return;
            }

            // Check if UIMinimap rotates a child element (like the directional cone) or Item2
            var item2Field = tupleType.GetField("Item2");
            var item2El = item2Field?.GetValue(tupleVal) as VisualElement;

            // If there is a text Label (jersey number), keep it upright (+90 deg to cancel container's -90 deg)
            var label = playerIcon.Q<Label>();
            if (label != null)
            {
                label.style.rotate = new StyleRotate(new Rotate(new Angle(90f, AngleUnit.Degree)));
            }

            // If UIMinimap.Update rotated item2El (or a non-Label child) for the directional indicator,
            // it already inherits mapContainer's -90 deg rotation ONLY if its parent is mapContainer.
            // However, if UIMinimap counter-rotates or sets world-to-screen angles assuming a vertical rink,
            // we check if playerIcon itself or item2El holds the heading rotation:
            if (item2El != null && item2El != label)
            {
                // If item2El is the rotation cone and UIMinimap set its angle, let it inherit the parent's -90 deg,
                // OR if it was counter-rotated by the game, offset it by -90f:
                float gameAngle = item2El.style.rotate.value.angle.value;
                // Only adjust if playerIcon itself was counter-rotated
                if (Mathf.Abs(playerIcon.style.rotate.value.angle.value) > 0.01f)
                {
                    item2El.style.rotate = new StyleRotate(new Rotate(new Angle(gameAngle, AngleUnit.Degree)));
                }
            }
        }

        private static bool TryGetPuckElevationColor(Puck puck, Color minHeightColor, Color maxHeightColor, out Color ringColor)
        {
            ringColor = minHeightColor;
            if (!puckIndicatorMap.TryGetValue(puck, out var indicator) || indicator == null)
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
                        float gradientFactor = Mathf.Pow(normalizedHeight, 1.5f);
                        ringColor = Color.Lerp(minHeightColor, maxHeightColor, gradientFactor);
                        return true;
                    }
                }
            }
            return false;
        }

        private static void EnsureMapContainerResolved(UIMinimap instance, Dictionary<Puck, VisualElement> puckMap, IDictionary playerMap)
        {
            if (mapContainer != null && mapContainer.panel != null) return;

            VisualElement sampleIcon = null;
            foreach (var kvp in puckMap)
            {
                if (kvp.Value != null) { sampleIcon = kvp.Value; break; }
            }
            if (sampleIcon == null)
            {
                foreach (DictionaryEntry entry in playerMap)
                {
                    var val = entry.Value;
                    if (val == null) continue;
                    var f = val.GetType().GetField("Item1");
                    if (f != null)
                    {
                        sampleIcon = f.GetValue(val) as VisualElement;
                        if (sampleIcon != null) break;
                    }
                }
            }

            if (sampleIcon != null && sampleIcon.parent != null)
            {
                // Use the direct rink element that holds the icons so rotating it rotates all icons and arrows together!
                mapContainer = sampleIcon.parent;
            }
            else
            {
                foreach (var field in typeof(UIMinimap).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (typeof(VisualElement).IsAssignableFrom(field.FieldType))
                    {
                        var el = field.GetValue(instance) as VisualElement;
                        if (el != null)
                        {
                            mapContainer = el;
                            break;
                        }
                    }
                }
            }

            if (mapContainer != null && !originalStyleCaptured)
            {
                origPosition = mapContainer.style.position;
                origLeft = mapContainer.style.left;
                origRight = mapContainer.style.right;
                origTop = mapContainer.style.top;
                origBottom = mapContainer.style.bottom;
                origMarginLeft = mapContainer.style.marginLeft;
                origMarginRight = mapContainer.style.marginRight;
                origMarginTop = mapContainer.style.marginTop;
                origMarginBottom = mapContainer.style.marginBottom;
                origTranslate = mapContainer.style.translate;
                origRotate = mapContainer.style.rotate;
                origScale = mapContainer.style.scale;
                origOpacity = mapContainer.style.opacity;
                originalStyleCaptured = true;
                ForceRefreshLayout();
            }
        }

        private static void UpdateMapLayoutAndGhost(string mode, Dictionary<Puck, VisualElement> puckMap, IDictionary playerMap)
        {
            Vector2 offset = new Vector2(ModConfig.Config.MapOffsetX, ModConfig.Config.MapOffsetY);
            float customScale = ModConfig.Config.CustomMapScale;
            bool ghost = ModConfig.Config.GhostMode;
            float ghostAlpha = ModConfig.Config.GhostBackgroundAlpha;

            if (mode != lastAppliedMode || customScale != lastAppliedMapScale || offset != lastAppliedOffset)
            {
                SetPickingModeRecursive(mapContainer, PickingMode.Ignore);

                if (mode == "vanilla")
                {
                    mapContainer.style.position = origPosition;
                    mapContainer.style.left = origLeft;
                    mapContainer.style.right = origRight;
                    mapContainer.style.top = origTop;
                    mapContainer.style.bottom = origBottom;
                    mapContainer.style.marginLeft = origMarginLeft;
                    mapContainer.style.marginRight = origMarginRight;
                    mapContainer.style.marginTop = origMarginTop;
                    mapContainer.style.marginBottom = origMarginBottom;
                    mapContainer.style.translate = origTranslate;
                    mapContainer.style.rotate = origRotate;
                    mapContainer.style.scale = origScale;
                }
                else
                {
                    // Ensure parent container doesn't offset us to the right bezel
                    if (mapContainer.parent != null && mapContainer.parent.name != "Root")
                    {
                        mapContainer.parent.pickingMode = PickingMode.Ignore;
                    }

                    mapContainer.style.position = Position.Absolute;
                    mapContainer.style.marginLeft = 0;
                    mapContainer.style.marginRight = 0;
                    mapContainer.style.marginTop = 0;
                    mapContainer.style.marginBottom = 0;
                    mapContainer.style.right = StyleKeyword.Auto;
                    mapContainer.style.bottom = StyleKeyword.Auto;

                    if (mode == "center")
                    {
                        mapContainer.style.left = new StyleLength(new Length(50f, LengthUnit.Percent));
                        mapContainer.style.top = new StyleLength(new Length(50f, LengthUnit.Percent));
                        mapContainer.style.translate = new StyleTranslate(new Translate(
                            new Length(-50f + offset.x, LengthUnit.Percent),
                            new Length(-50f + offset.y, LengthUnit.Percent), 0));
                        mapContainer.style.rotate = new StyleRotate(new Rotate(new Angle(0f, AngleUnit.Degree)));
                        mapContainer.style.scale = new StyleScale(new Scale(new Vector2(customScale, customScale)));
                    }
                    else if (mode == "top")
                    {
                        mapContainer.style.left = new StyleLength(new Length(50f, LengthUnit.Percent));
                        mapContainer.style.top = new StyleLength(new Length(16f, LengthUnit.Percent));
                        mapContainer.style.translate = new StyleTranslate(new Translate(
                            new Length(-50f + offset.x, LengthUnit.Percent),
                            new Length(-50f + offset.y, LengthUnit.Percent), 0));
                        mapContainer.style.rotate = new StyleRotate(new Rotate(new Angle(-90f, AngleUnit.Degree)));
                        mapContainer.style.scale = new StyleScale(new Scale(new Vector2(customScale, customScale)));
                    }
                    else if (mode == "bottom")
                    {
                        mapContainer.style.left = new StyleLength(new Length(50f, LengthUnit.Percent));
                        mapContainer.style.top = new StyleLength(new Length(84f, LengthUnit.Percent));
                        mapContainer.style.translate = new StyleTranslate(new Translate(
                            new Length(-50f + offset.x, LengthUnit.Percent),
                            new Length(-50f + offset.y, LengthUnit.Percent), 0));
                        mapContainer.style.rotate = new StyleRotate(new Rotate(new Angle(-90f, AngleUnit.Degree)));
                        mapContainer.style.scale = new StyleScale(new Scale(new Vector2(customScale, customScale)));
                    }
                }

                lastAppliedMode = mode;
                lastAppliedMapScale = customScale;
                lastAppliedOffset = offset;
            }

            if (ghost != lastAppliedGhost || (ghost && !Mathf.Approximately(ghostAlpha, lastAppliedGhostAlpha)))
            {
                ApplyGhostMode(mapContainer, ghost, ghostAlpha, puckMap, playerMap);
                lastAppliedGhost = ghost;
                lastAppliedGhostAlpha = ghostAlpha;
            }
        }

        private static void SetPickingModeRecursive(VisualElement el, PickingMode mode)
        {
            if (el == null) return;
            el.pickingMode = mode;
            foreach (var child in el.Children())
            {
                SetPickingModeRecursive(child, mode);
            }
        }

        private static void ApplyGhostMode(VisualElement root, bool enable, float targetAlpha, Dictionary<Puck, VisualElement> puckMap, IDictionary playerMap)
        {
            if (root == null) return;

            var iconElements = new HashSet<VisualElement>();
            foreach (var kvp in puckMap)
            {
                if (kvp.Value != null) CollectElementTree(kvp.Value, iconElements);
            }
            foreach (DictionaryEntry entry in playerMap)
            {
                var val = entry.Value;
                if (val == null) continue;
                var f = val.GetType().GetField("Item1");
                if (f != null)
                {
                    var el = f.GetValue(val) as VisualElement;
                    if (el != null) CollectElementTree(el, iconElements);
                }
            }

            ApplyGhostRecursive(root, enable, targetAlpha, iconElements);
            if (root.parent != null && root.parent.name != "Root")
            {
                ApplyGhostRecursive(root.parent, enable, targetAlpha, iconElements);
            }
        }

        private static void CollectElementTree(VisualElement root, HashSet<VisualElement> set)
        {
            if (root == null) return;
            set.Add(root);
            foreach (var c in root.Children())
            {
                CollectElementTree(c, set);
            }
        }

        private static void ApplyGhostRecursive(VisualElement el, bool enable, float targetAlpha, HashSet<VisualElement> excludedIcons)
        {
            if (el == null || excludedIcons.Contains(el)) return;

            if (!originalBgStates.ContainsKey(el))
            {
                originalBgStates[el] = new ElementBgState
                {
                    BackgroundColor = el.style.backgroundColor,
                    UnityBackgroundImageTintColor = el.style.unityBackgroundImageTintColor
                };
            }

            if (enable)
            {
                Color resolvedBg = el.resolvedStyle.backgroundColor;
                if (resolvedBg.a > 0.01f)
                {
                    el.style.backgroundColor = new StyleColor(new Color(resolvedBg.r, resolvedBg.g, resolvedBg.b, targetAlpha));
                }

                if (el.resolvedStyle.backgroundImage.texture != null || el.resolvedStyle.backgroundImage.sprite != null)
                {
                    Color tint = el.resolvedStyle.unityBackgroundImageTintColor;
                    el.style.unityBackgroundImageTintColor = new StyleColor(new Color(tint.r, tint.g, tint.b, Mathf.Max(targetAlpha, 0.25f)));
                }
            }
            else if (originalBgStates.TryGetValue(el, out var orig))
            {
                el.style.backgroundColor = orig.BackgroundColor;
                el.style.unityBackgroundImageTintColor = orig.UnityBackgroundImageTintColor;
            }

            foreach (var child in el.Children())
            {
                if (child != mapContainer)
                    ApplyGhostRecursive(child, enable, targetAlpha, excludedIcons);
            }
        }

        private static Vector3? GetReferencePosition(IDictionary playerMap, out Vector3 forward)
        {
            forward = Vector3.forward;
            if (Camera.main != null)
            {
                Vector3 camFwd = Camera.main.transform.forward;
                camFwd.y = 0f;
                if (camFwd.sqrMagnitude > 0.001f)
                    forward = camFwd.normalized;

                return Camera.main.transform.position;
            }

            foreach (DictionaryEntry entry in playerMap)
            {
                var comp = entry.Key as Component;
                if (comp != null)
                {
                    var netObj = comp.GetComponentInParent<Unity.Netcode.NetworkBehaviour>();
                    if (netObj != null && netObj.IsOwner)
                    {
                        Vector3 fwd = comp.transform.forward;
                        fwd.y = 0f;
                        if (fwd.sqrMagnitude > 0.001f) forward = fwd.normalized;
                        return comp.transform.position;
                    }
                }
            }

            return null;
        }

        private static void UpdateRadarRing(
            Vector3? refPos,
            Vector3 refForward,
            Dictionary<Puck, VisualElement> puckMap,
            IDictionary playerMap,
            Color minHeightColor,
            Color maxHeightColor)
        {
            if (!ModConfig.Config.RadarRingEnabled || !refPos.HasValue || mapContainer?.panel == null)
            {
                if (radarRoot != null) radarRoot.style.display = DisplayStyle.None;
                return;
            }

            EnsureRadarRootCreated(mapContainer.panel.visualTree);
            radarRoot.style.display = DisplayStyle.Flex;
            radarRoot.style.opacity = ModConfig.Config.RadarOpacity;

            float radius = ModConfig.Config.RadarRadius;
            float maxDist = Mathf.Max(5f, ModConfig.Config.RadarMaxDistance);

            radarGuideCircle.style.width = radius * 2f;
            radarGuideCircle.style.height = radius * 2f;
            radarGuideCircle.style.left = -radius;
            radarGuideCircle.style.top = -radius;

            Vector3 origin = refPos.Value;

            var activePlayers = new HashSet<object>();
            foreach (DictionaryEntry entry in playerMap)
            {
                object playerKey = entry.Key;
                var playerComp = playerKey as Component;
                if (playerComp == null) continue;

                Vector3 worldPos = playerComp.transform.position;
                Vector3 flatDelta = worldPos - origin;
                flatDelta.y = 0f;
                float dist = flatDelta.magnitude;

                if (dist < 1.2f) continue;

                activePlayers.Add(playerKey);

                if (!radarPlayerDots.TryGetValue(playerKey, out var dot))
                {
                    dot = CreateRadarPlayerDot();
                    radarRoot.Add(dot);
                    radarPlayerDots[playerKey] = dot;
                }

                Color dotColor = ExtractPlayerIconColor(entry.Value);
                dot.style.backgroundColor = new StyleColor(dotColor);

                PositionElementOnRadar(dot, flatDelta, refForward, dist, maxDist, radius, 12f);
            }

            var playersToRemove = new List<object>();
            foreach (var kvp in radarPlayerDots)
            {
                if (!activePlayers.Contains(kvp.Key))
                {
                    kvp.Value.RemoveFromHierarchy();
                    playersToRemove.Add(kvp.Key);
                }
            }
            foreach (var k in playersToRemove) radarPlayerDots.Remove(k);

            foreach (var puckEntry in puckMap)
            {
                Puck puck = puckEntry.Key;
                if (puck == null) continue;

                if (!radarPuckDots.TryGetValue(puck, out var puckDot))
                {
                    puckDot = CreateRadarPuckDot();
                    radarRoot.Add(puckDot);
                    radarPuckDots[puck] = puckDot;
                }

                Vector3 flatDelta = puck.transform.position - origin;
                flatDelta.y = 0f;
                float dist = flatDelta.magnitude;

                PositionElementOnRadar(puckDot, flatDelta, refForward, dist, maxDist, radius, 14f);

                if (ModConfig.Config.RingEnabled && TryGetPuckElevationColor(puck, minHeightColor, maxHeightColor, out Color ringCol))
                {
                    puckDot.style.borderTopColor = new StyleColor(ringCol);
                    puckDot.style.borderRightColor = new StyleColor(ringCol);
                    puckDot.style.borderBottomColor = new StyleColor(ringCol);
                    puckDot.style.borderLeftColor = new StyleColor(ringCol);
                    puckDot.style.borderTopWidth = 2.5f;
                    puckDot.style.borderRightWidth = 2.5f;
                    puckDot.style.borderBottomWidth = 2.5f;
                    puckDot.style.borderLeftWidth = 2.5f;
                }
                else
                {
                    var whiteBorder = new StyleColor(Color.white);
                    puckDot.style.borderTopColor = whiteBorder;
                    puckDot.style.borderRightColor = whiteBorder;
                    puckDot.style.borderBottomColor = whiteBorder;
                    puckDot.style.borderLeftColor = whiteBorder;
                    puckDot.style.borderTopWidth = 1.5f;
                    puckDot.style.borderRightWidth = 1.5f;
                    puckDot.style.borderBottomWidth = 1.5f;
                    puckDot.style.borderLeftWidth = 1.5f;
                }
            }
        }

        private static void PositionElementOnRadar(
            VisualElement dot,
            Vector3 flatDelta,
            Vector3 refForward,
            float dist,
            float maxDist,
            float radius,
            float baseSize)
        {
            float angleDeg = Vector3.SignedAngle(refForward, flatDelta.normalized, Vector3.up);
            float angleRad = angleDeg * Mathf.Deg2Rad;

            float x = Mathf.Sin(angleRad) * radius;
            float y = -Mathf.Cos(angleRad) * radius;

            float proximityFactor = 1f - Mathf.Clamp01(dist / maxDist);
            float scale = Mathf.Lerp(0.65f, 1.45f, proximityFactor) * (ModConfig.Config.IconScale / 1.75f);

            dot.style.left = x - (baseSize * 0.5f);
            dot.style.top = y - (baseSize * 0.5f);
            dot.style.scale = new StyleScale(new Scale(new Vector2(scale, scale)));
        }

        private static Color ExtractPlayerIconColor(object tupleObj)
        {
            if (tupleObj == null) return Color.cyan;
            var f = tupleObj.GetType().GetField("Item1");
            var icon = f?.GetValue(tupleObj) as VisualElement;
            if (icon != null)
            {
                Color c = FindFirstNonClearColor(icon);
                if (c.a > 0.1f) return c;
            }
            return new Color(0.2f, 0.6f, 1f, 0.9f);
        }

        private static Color FindFirstNonClearColor(VisualElement el)
        {
            if (el == null) return Color.clear;
            Color bg = el.resolvedStyle.backgroundColor;
            if (bg.a > 0.1f && (bg.r < 0.9f || bg.g < 0.9f || bg.b < 0.9f))
                return bg;

            Color tint = el.resolvedStyle.unityBackgroundImageTintColor;
            if (tint.a > 0.1f && (tint.r < 0.9f || tint.g < 0.9f || tint.b < 0.9f))
                return tint;

            foreach (var child in el.Children())
            {
                Color childCol = FindFirstNonClearColor(child);
                if (childCol.a > 0.1f) return childCol;
            }
            return Color.clear;
        }

        private static void EnsureRadarRootCreated(VisualElement rootTree)
        {
            if (radarRoot != null && radarRoot.panel != null) return;

            radarRoot = new VisualElement
            {
                name = "MapHackRadarRoot",
                pickingMode = PickingMode.Ignore
            };
            radarRoot.style.position = Position.Absolute;
            radarRoot.style.left = new StyleLength(new Length(50f, LengthUnit.Percent));
            radarRoot.style.top = new StyleLength(new Length(50f, LengthUnit.Percent));
            radarRoot.style.width = 0;
            radarRoot.style.height = 0;

            radarGuideCircle = new VisualElement
            {
                name = "MapHackRadarGuide",
                pickingMode = PickingMode.Ignore
            };
            radarGuideCircle.style.position = Position.Absolute;
            var halfPercent = new StyleLength(new Length(50f, LengthUnit.Percent));
            radarGuideCircle.style.borderTopLeftRadius = halfPercent;
            radarGuideCircle.style.borderTopRightRadius = halfPercent;
            radarGuideCircle.style.borderBottomLeftRadius = halfPercent;
            radarGuideCircle.style.borderBottomRightRadius = halfPercent;

            var faintRingColor = new StyleColor(new Color(1f, 1f, 1f, 0.12f));
            radarGuideCircle.style.borderTopColor = faintRingColor;
            radarGuideCircle.style.borderRightColor = faintRingColor;
            radarGuideCircle.style.borderBottomColor = faintRingColor;
            radarGuideCircle.style.borderLeftColor = faintRingColor;
            radarGuideCircle.style.borderTopWidth = 1f;
            radarGuideCircle.style.borderRightWidth = 1f;
            radarGuideCircle.style.borderBottomWidth = 1f;
            radarGuideCircle.style.borderLeftWidth = 1f;

            radarRoot.Add(radarGuideCircle);
            rootTree.Add(radarRoot);
        }

        private static VisualElement CreateRadarPlayerDot()
        {
            var dot = new VisualElement { name = "RadarPlayerDot", pickingMode = PickingMode.Ignore };
            dot.style.position = Position.Absolute;
            dot.style.width = 12f;
            dot.style.height = 12f;
            var radius = new StyleLength(new Length(50f, LengthUnit.Percent));
            dot.style.borderTopLeftRadius = radius;
            dot.style.borderTopRightRadius = radius;
            dot.style.borderBottomLeftRadius = radius;
            dot.style.borderBottomRightRadius = radius;

            var whiteBorder = new StyleColor(new Color(1f, 1f, 1f, 0.85f));
            dot.style.borderTopColor = whiteBorder;
            dot.style.borderRightColor = whiteBorder;
            dot.style.borderBottomColor = whiteBorder;
            dot.style.borderLeftColor = whiteBorder;
            dot.style.borderTopWidth = 1.5f;
            dot.style.borderRightWidth = 1.5f;
            dot.style.borderBottomWidth = 1.5f;
            dot.style.borderLeftWidth = 1.5f;
            return dot;
        }

        private static VisualElement CreateRadarPuckDot()
        {
            var dot = new VisualElement { name = "RadarPuckDot", pickingMode = PickingMode.Ignore };
            dot.style.position = Position.Absolute;
            dot.style.width = 14f;
            dot.style.height = 14f;
            dot.style.backgroundColor = new StyleColor(Color.black);
            var radius = new StyleLength(new Length(50f, LengthUnit.Percent));
            dot.style.borderTopLeftRadius = radius;
            dot.style.borderTopRightRadius = radius;
            dot.style.borderBottomLeftRadius = radius;
            dot.style.borderBottomRightRadius = radius;
            return dot;
        }
    }
}