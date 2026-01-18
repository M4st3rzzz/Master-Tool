using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace InfiniteHealthToggle
{
    [BepInPlugin("com.master.tools", "Advanced SPT Mod Menu", "1.5.0")]
    public sealed class InfiniteHealthTogglePlugin : BaseUnityPlugin
    {
        internal static InfiniteHealthTogglePlugin Instance;

        // --- General Settings ---
        internal static ConfigEntry<bool> GodModeEnabled;
        internal static ConfigEntry<bool> InfiniteStaminaEnabled;
        internal static ConfigEntry<bool> NoWeightEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleGodModeHotkey;
        internal static ConfigEntry<KeyboardShortcut> ToggleUiHotkey;

        // --- Player ESP Settings ---
        internal static ConfigEntry<bool> EspEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleEspHotkey;
        internal static ConfigEntry<float> EspUpdateInterval;
        internal static ConfigEntry<float> EspMaxDistance;
        internal static ConfigEntry<Color> ColorBear;
        internal static ConfigEntry<Color> ColorUsec;
        internal static ConfigEntry<Color> ColorSavage;

        // --- Item & Container ESP Settings ---
        internal static ConfigEntry<bool> ItemEspEnabled;
        internal static ConfigEntry<bool> ContainerEspEnabled;
        internal static ConfigEntry<string> ItemEspFilter;
        internal static ConfigEntry<float> ItemEspMaxDistance;
        internal static ConfigEntry<float> ItemEspUpdateInterval;
        internal static ConfigEntry<Color> ColorItem;
        internal static ConfigEntry<Color> ColorContainer;

        private Harmony _harmony;

        // --- UI ---
        private bool _showUi;
        private Rect _windowRect = new Rect(25, 25, 500, 650);
        private Vector2 _mainScroll;
        private bool _isResizing;
        private Vector2 _resizeStartMouse;
        private Rect _resizeStartRect;
        private const float ResizeGripSize = 18f;
        private const float MinWindowWidth = 400f;
        private const float MinWindowHeight = 450f;
        private const float DragBarHeight = 22f;
        private const int WindowId = 987654;

        // --- Cache ---
        private static Player _localPlayer;
        private static ActiveHealthController _localActiveHealthController;
        private static Camera _mainCamera;
        private static GameWorld _gameWorld;
        private float _nextLocalRefresh;
        private const float LocalRefreshIntervalSeconds = 4.0f;

        // --- ESP Internals ---
        private float _nextEspUpdate;
        private List<EspTarget> _espTargets = new List<EspTarget>();
        private float _nextItemEspUpdate;
        private List<ItemEspTarget> _itemEspTargets = new List<ItemEspTarget>();
        private GUIStyle _espLabelStyle;
        private bool _espStyleInitialized;

        // --- Optimization Cache ---
        private LootableContainer[] _cachedContainers;
        private float _nextContainerCacheRefresh;
        private const float ContainerCacheInterval = 10.0f; // Refresh container list every 10s

        private void Awake()
        {
            Instance = this;

            // General Binds
            GodModeEnabled = Config.Bind("General", "GodMode", false, "Player takes no damage.");
            InfiniteStaminaEnabled = Config.Bind("General", "Infinite Stamina", false, "Unlimited stamina and breath.");
            NoWeightEnabled = Config.Bind("General", "No Weight", false, "Removes weight penalties.");
            ToggleGodModeHotkey = Config.Bind("Hotkeys", "Toggle GodMode", new KeyboardShortcut(KeyCode.F7), "Hotkey for GodMode.");
            ToggleUiHotkey = Config.Bind("Hotkeys", "Toggle UI", new KeyboardShortcut(KeyCode.Insert), "Hotkey for Menu.");

            // Player ESP Binds
            EspEnabled = Config.Bind("ESP Players", "Enabled", false, "Show players/bots.");
            ToggleEspHotkey = Config.Bind("Hotkeys", "Toggle ESP", new KeyboardShortcut(KeyCode.F8), "Hotkey for ESP.");
            EspUpdateInterval = Config.Bind("ESP Players", "Update Interval", 0.05f, "Update rate for player ESP.");
            EspMaxDistance = Config.Bind("ESP Players", "Max Distance", 400f, "Max distance for players.");
            ColorBear = Config.Bind("ESP Players", "Color BEAR", Color.red, "Color for BEAR faction.");
            ColorUsec = Config.Bind("ESP Players", "Color USEC", Color.blue, "Color for USEC faction.");
            ColorSavage = Config.Bind("ESP Players", "Color Savage", Color.yellow, "Color for Scavs/Bots.");

            // Item & Container ESP Binds
            ItemEspEnabled = Config.Bind("ESP Items", "Enabled", false, "Show loose loot.");
            ContainerEspEnabled = Config.Bind("ESP Containers", "Enabled", false, "Show items inside containers.");
            ItemEspFilter = Config.Bind("ESP Items", "Filter", "", "Filter by name or ID (comma separated).");
            ItemEspMaxDistance = Config.Bind("ESP Items", "Max Distance", 100f, "Max distance for items.");
            ItemEspUpdateInterval = Config.Bind("ESP Items", "Update Interval", 0.5f, "Update rate for item ESP.");
            ColorItem = Config.Bind("ESP Items", "Color", Color.green, "Color for loose items.");
            ColorContainer = Config.Bind("ESP Containers", "Color", new Color(1f, 0.5f, 0f), "Color for container items.");

            _harmony = new Harmony("com.seunome.infinitehealthtoggle");

            // Patches
            TryPatchDamageMethod(typeof(Player), "ApplyDamageInfo", nameof(BlockDamagePrefix_Player));
            TryPatchDamageMethod(typeof(Player), "ApplyDamage", nameof(BlockDamagePrefix_Player));
            TryPatchDamageMethod(typeof(ActiveHealthController), "ApplyDamage", nameof(BlockDamagePrefix_ActiveHealthController));

            Logger.LogInfo("[AdvancedMod] Loaded. INSERT: UI, F7: GodMode, F8: ESP.");
        }

        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
        }

        private void Update()
        {
            if (ToggleUiHotkey.Value.IsDown()) _showUi = !_showUi;
            if (ToggleGodModeHotkey.Value.IsDown()) GodModeEnabled.Value = !GodModeEnabled.Value;
            if (ToggleEspHotkey.Value.IsDown()) EspEnabled.Value = !EspEnabled.Value;

            if (Time.time >= _nextLocalRefresh)
            {
                RefreshLocalReferences();
                _nextLocalRefresh = Time.time + LocalRefreshIntervalSeconds;
            }

            if (_localPlayer != null)
            {
                if (InfiniteStaminaEnabled.Value) HandleInfiniteStamina();
                // NoWeight logic is handled via other means if necessary, but kept for consistency
            }

            // ESP Update Logic
            if (EspEnabled.Value && _localPlayer != null && Time.time >= _nextEspUpdate)
            {
                UpdateEspTargets();
                _nextEspUpdate = Time.time + EspUpdateInterval.Value;
            }

            if ((ItemEspEnabled.Value || ContainerEspEnabled.Value) && _localPlayer != null && Time.time >= _nextItemEspUpdate)
            {
                UpdateItemAndContainerEsp();
                _nextItemEspUpdate = Time.time + ItemEspUpdateInterval.Value;
            }
        }

        private void HandleInfiniteStamina()
        {
            try
            {
                var stamina = _localPlayer.Physical.Stamina;
                var hands = _localPlayer.Physical.HandsStamina;
                var oxygen = _localPlayer.Physical.Oxygen;

                if (stamina != null) stamina.Current = stamina.TotalCapacity;
                if (hands != null) hands.Current = hands.TotalCapacity;
                if (oxygen != null) oxygen.Current = oxygen.TotalCapacity;
            }
            catch { }
        }

        private void UnlockAllDoors()
        {
            if (_gameWorld == null) return;
            try
            {
                var doors = FindObjectsOfType<Door>();
                int count = 0;
                foreach (var door in doors)
                {
                    if (door.DoorState == EDoorState.Locked)
                    {
                        door.DoorState = EDoorState.Shut;
                        count++;
                    }
                }
                Logger.LogInfo($"Unlocked {count} doors.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Unlock Error: {ex.Message}");
            }
        }

        private void OnGUI()
        {
            if (!_espStyleInitialized) InitializeEspStyle();

            if (_localPlayer != null && _mainCamera != null)
            {
                if (EspEnabled.Value) RenderEsp();
                if (ItemEspEnabled.Value || ContainerEspEnabled.Value) RenderItemEsp();
            }

            if (_showUi)
                _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "Advanced SPT Mod Menu");
        }

        private void InitializeEspStyle()
        {
            _espLabelStyle = new GUIStyle(GUI.skin.label);
            _espLabelStyle.fontSize = 12;
            _espLabelStyle.fontStyle = FontStyle.Bold;
            _espLabelStyle.alignment = TextAnchor.MiddleCenter;
            _espStyleInitialized = true;
        }

        private void UpdateEspTargets()
        {
            _espTargets.Clear();
            if (_gameWorld == null || _mainCamera == null) return;

            var players = _gameWorld.RegisteredPlayers;
            if (players == null) return;

            foreach (var player in players)
            {
                if (player == null || player.IsYourPlayer || !player.HealthController.IsAlive) continue;

                Vector3 targetPos = player.Transform.position;
                float dist = Vector3.Distance(_localPlayer.Transform.position, targetPos);
                if (dist > EspMaxDistance.Value) continue;

                Vector3 screenPos = _mainCamera.WorldToScreenPoint(targetPos);
                if (screenPos.z > 0)
                {
                    screenPos.y = Screen.height - screenPos.y;
                    string side = player.Profile?.Side.ToString() ?? "Unknown";
                    string name = player.Profile?.Info?.Nickname ?? "Bot";

                    _espTargets.Add(new EspTarget
                    {
                        ScreenPosition = screenPos,
                        Distance = dist,
                        Nickname = name,
                        Side = side,
                        Color = GetColorForSide(side)
                    });
                }
            }
        }

        private void UpdateItemAndContainerEsp()
        {
            _itemEspTargets.Clear();
            if (_gameWorld == null || _mainCamera == null) return;

            // Parse multiple filters
            string[] filters = ItemEspFilter.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(f => f.Trim().ToLower())
                                .ToArray();

            // 1. Loose Loot
            if (ItemEspEnabled.Value)
            {
                var lootItems = _gameWorld.LootItems;
                if (lootItems != null)
                {
                    for (int i = 0; i < lootItems.Count; i++)
                    {
                        var loot = lootItems.GetByIndex(i);
                        if (loot == null || loot.Item == null) continue;
                        ProcessLoot(loot.transform.position, loot.Item, ColorItem.Value, filters);
                    }
                }
            }

            // 2. Container Loot (Optimized)
            if (ContainerEspEnabled.Value)
            {
                // Refresh container cache periodically
                if (_cachedContainers == null || Time.time >= _nextContainerCacheRefresh)
                {
                    _cachedContainers = FindObjectsOfType<LootableContainer>();
                    _nextContainerCacheRefresh = Time.time + ContainerCacheInterval;
                }

                Vector3 playerPos = _localPlayer.Transform.position;
                float maxDistSq = ItemEspMaxDistance.Value * ItemEspMaxDistance.Value;

                foreach (var container in _cachedContainers)
                {
                    if (container == null) continue;

                    // Fast distance check using squared distance
                    Vector3 containerPos = container.transform.position;
                    float distSq = (containerPos - playerPos).sqrMagnitude;
                    if (distSq > maxDistSq) continue;

                    if (container.ItemOwner == null || container.ItemOwner.RootItem == null) continue;

                    // Check items inside
                    var items = container.ItemOwner.RootItem.GetAllItems();
                    foreach (var item in items)
                    {
                        if (item == container.ItemOwner.RootItem) continue;
                        ProcessLoot(containerPos, item, ColorContainer.Value, filters, true);
                    }
                }
            }
        }

        private void ProcessLoot(Vector3 pos, Item item, Color color, string[] filters, bool isContainer = false)
        {
            float dist = Vector3.Distance(_localPlayer.Transform.position, pos);
            if (dist > ItemEspMaxDistance.Value) return;

            string name = item.ShortName.Localized();
            string id = item.TemplateId;

            // Filter logic: match any of the filters
            bool matches = filters.Length == 0;
            if (!matches)
            {
                foreach (var f in filters)
                {
                    if (name.ToLower().Contains(f) || id.ToLower().Contains(f))
                    {
                        matches = true;
                        break;
                    }
                }
            }

            if (!matches) return;

            Vector3 screenPos = _mainCamera.WorldToScreenPoint(pos);
            if (screenPos.z > 0)
            {
                screenPos.y = Screen.height - screenPos.y;
                _itemEspTargets.Add(new ItemEspTarget
                {
                    ScreenPosition = screenPos,
                    Distance = dist,
                    Name = isContainer ? $"[C] {name}" : name,
                    Color = color
                });
            }
        }

        private Color GetColorForSide(string side)
        {
            if (side.Contains("Bear")) return ColorBear.Value;
            if (side.Contains("Usec")) return ColorUsec.Value;
            return ColorSavage.Value;
        }

        private void RenderEsp()
        {
            foreach (var target in _espTargets)
            {
                string text = $"{target.Nickname}\n[{target.Side}]\n{target.Distance:F1}m";
                DrawTextWithShadow(target.ScreenPosition, text, target.Color);
            }
        }

        private void RenderItemEsp()
        {
            foreach (var target in _itemEspTargets)
            {
                string text = $"{target.Name}\n{target.Distance:F1}m";
                DrawTextWithShadow(target.ScreenPosition, text, target.Color);
            }
        }

        private void DrawTextWithShadow(Vector3 pos, string text, Color color)
        {
            Vector2 size = _espLabelStyle.CalcSize(new GUIContent(text));
            Rect rect = new Rect(pos.x - size.x / 2, pos.y - size.y / 2, size.x, size.y);
            _espLabelStyle.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, _espLabelStyle);
            _espLabelStyle.normal.textColor = color;
            GUI.Label(rect, text, _espLabelStyle);
        }

        private void DrawWindow(int id)
        {
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, DragBarHeight));
            GUILayout.BeginArea(new Rect(10, DragBarHeight + 5, _windowRect.width - 20, _windowRect.height - DragBarHeight - 20));
            _mainScroll = GUILayout.BeginScrollView(_mainScroll);

            // --- General Section ---
            GUILayout.Label("<b>--- GENERAL CHEATS ---</b>");
            GodModeEnabled.Value = GUILayout.Toggle(GodModeEnabled.Value, " GodMode (F7)");
            InfiniteStaminaEnabled.Value = GUILayout.Toggle(InfiniteStaminaEnabled.Value, " Infinite Stamina");
            NoWeightEnabled.Value = GUILayout.Toggle(NoWeightEnabled.Value, " No Weight Penalties");
            if (GUILayout.Button("Unlock All Doors in Raid")) UnlockAllDoors();

            GUILayout.Space(10);
            GUILayout.Label("<b>--- PLAYER ESP ---</b>");
            EspEnabled.Value = GUILayout.Toggle(EspEnabled.Value, " Enable Player ESP (F8)");
            GUILayout.Label($"Max Distance: {EspMaxDistance.Value:F0}m");
            EspMaxDistance.Value = GUILayout.HorizontalSlider(EspMaxDistance.Value, 50f, 1000f);
            GUILayout.Label($"Update Rate (FPS): {1f / EspUpdateInterval.Value:F0}");
            float pFps = GUILayout.HorizontalSlider(1f / EspUpdateInterval.Value, 1f, 60f);
            EspUpdateInterval.Value = 1f / pFps;

            GUILayout.Space(5);
            ColorBear.Value = DrawColorPicker("BEAR", ColorBear.Value);
            ColorUsec.Value = DrawColorPicker("USEC", ColorUsec.Value);
            ColorSavage.Value = DrawColorPicker("Savage", ColorSavage.Value);

            GUILayout.Space(10);
            GUILayout.Label("<b>--- ITEM & CONTAINER ESP ---</b>");
            ItemEspEnabled.Value = GUILayout.Toggle(ItemEspEnabled.Value, " Enable Loose Item ESP");
            ContainerEspEnabled.Value = GUILayout.Toggle(ContainerEspEnabled.Value, " Enable Container Item ESP");
            GUILayout.Label("Filter (Name or ID, comma separated):");
            ItemEspFilter.Value = GUILayout.TextField(ItemEspFilter.Value);
            GUILayout.Label($"Max Distance: {ItemEspMaxDistance.Value:F0}m");
            ItemEspMaxDistance.Value = GUILayout.HorizontalSlider(ItemEspMaxDistance.Value, 5f, 500f);
            GUILayout.Label($"Update Rate (FPS): {1f / ItemEspUpdateInterval.Value:F0}");
            float iFps = GUILayout.HorizontalSlider(1f / ItemEspUpdateInterval.Value, 1f, 60f);
            ItemEspUpdateInterval.Value = 1f / iFps;

            GUILayout.Space(10);
            GUILayout.Label("<b>--- DEBUG INFO ---</b>");
            GUILayout.Label($"GameWorld: {(_gameWorld != null ? "OK" : "NULL")}");
            GUILayout.Label($"LocalPlayer: {(_localPlayer != null ? "OK" : "NULL")}");
            GUILayout.Label($"Containers Cached: {(_cachedContainers != null ? _cachedContainers.Length.ToString() : "0")}");

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Resize logic
            var gripRect = new Rect(_windowRect.width - ResizeGripSize, _windowRect.height - ResizeGripSize, ResizeGripSize, ResizeGripSize);
            GUI.Box(gripRect, "///");
            var e = Event.current;
            if (e.type == EventType.MouseDown && gripRect.Contains(e.mousePosition)) { _isResizing = true; _resizeStartMouse = e.mousePosition; _resizeStartRect = _windowRect; e.Use(); }
            if (_isResizing && e.type == EventType.MouseDrag)
            {
                var delta = e.mousePosition - _resizeStartMouse;
                _windowRect.width = Mathf.Max(MinWindowWidth, _resizeStartRect.width + delta.x);
                _windowRect.height = Mathf.Max(MinWindowHeight, _resizeStartRect.height + delta.y);
                e.Use();
            }
            if (e.type == EventType.MouseUp) _isResizing = false;
        }

        private Color DrawColorPicker(string label, Color color)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            float r = GUILayout.HorizontalSlider(color.r, 0f, 1f, GUILayout.Width(50));
            float g = GUILayout.HorizontalSlider(color.g, 0f, 1f, GUILayout.Width(50));
            float b = GUILayout.HorizontalSlider(color.b, 0f, 1f, GUILayout.Width(50));
            GUILayout.EndHorizontal();
            return new Color(r, g, b, 1f);
        }

        private void RefreshLocalReferences()
        {
            try
            {
                _gameWorld = Singleton<GameWorld>.Instance;
                if (_gameWorld == null) return;
                _localPlayer = _gameWorld.MainPlayer;
                if (_localPlayer == null) return;
                _localActiveHealthController = _localPlayer.ActiveHealthController;
                if (_mainCamera == null) _mainCamera = Camera.main ?? GameObject.Find("FPS Camera")?.GetComponent<Camera>();
            }
            catch { }
        }

        private void TryPatchDamageMethod(Type type, string methodName, string prefixMethodName)
        {
            try
            {
                var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) return;
                var prefix = new HarmonyMethod(typeof(InfiniteHealthTogglePlugin).GetMethod(prefixMethodName, BindingFlags.Static | BindingFlags.NonPublic));
                _harmony.Patch(method, prefix: prefix);
            }
            catch { }
        }

        private static bool BlockDamagePrefix_Player(Player __instance) => !GodModeEnabled.Value || !__instance.IsYourPlayer;
        private static bool BlockDamagePrefix_ActiveHealthController(ActiveHealthController __instance, ref float __result)
        {
            if (GodModeEnabled.Value && _localActiveHealthController != null && ReferenceEquals(__instance, _localActiveHealthController))
            {
                __result = 0f;
                return false;
            }
            return true;
        }

        private class EspTarget
        {
            public Vector2 ScreenPosition;
            public float Distance;
            public string Nickname;
            public string Side;
            public Color Color;
        }

        private class ItemEspTarget
        {
            public Vector2 ScreenPosition;
            public float Distance;
            public string Name;
            public Color Color;
        }
    }
}
