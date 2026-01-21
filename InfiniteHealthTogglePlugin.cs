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
    [BepInPlugin("com.master.tools", "Advanced SPT Mod Menu", "1.9.0")]
    public sealed class InfiniteHealthTogglePlugin : BaseUnityPlugin
    {
        internal static InfiniteHealthTogglePlugin Instance;

        // --- General Settings ---
        internal static ConfigEntry<bool> GodModeEnabled;
        internal static ConfigEntry<bool> InfiniteStaminaEnabled;
        internal static ConfigEntry<bool> NoWeightEnabled;
        internal static ConfigEntry<bool> StatusWindowEnabled;

        internal static ConfigEntry<bool> ShowWeaponInfo;
        internal static ConfigEntry<KeyboardShortcut> ToggleWeaponInfoHotkey;
        private static FieldInfo _stackCountField;

        // --- Hotkeys (Customizable) ---
        internal static ConfigEntry<KeyboardShortcut> ToggleUiHotkey;
        internal static ConfigEntry<KeyboardShortcut> ToggleStatusHotkey;
        internal static ConfigEntry<KeyboardShortcut> ToggleGodModeHotkey;
        internal static ConfigEntry<KeyboardShortcut> ToggleStaminaHotkey;
        internal static ConfigEntry<KeyboardShortcut> ToggleWeightHotkey;
        internal static ConfigEntry<KeyboardShortcut> ToggleEspHotkey;
        internal static ConfigEntry<KeyboardShortcut> ToggleItemEspHotkey;
        internal static ConfigEntry<KeyboardShortcut> ToggleContainerEspHotkey;
        internal static ConfigEntry<KeyboardShortcut> ToggleCullingHotkey;
        internal static ConfigEntry<KeyboardShortcut> ToggleUnlockDoorsHotkey;

        // --- Player ESP Settings ---
        internal static ConfigEntry<bool> EspEnabled;
        internal static ConfigEntry<float> EspUpdateInterval;
        internal static ConfigEntry<float> EspMaxDistance;
        internal static ConfigEntry<Color> ColorBear;
        internal static ConfigEntry<Color> ColorUsec;
        internal static ConfigEntry<Color> ColorSavage;
        internal static ConfigEntry<Color> ColorBoss;

        internal static ConfigEntry<bool> ChamsEnabled;
        internal static ConfigEntry<float> ChamsIntensity;
        internal static ConfigEntry<KeyboardShortcut> ToggleChamsHotkey;
        private static Shader _chamsShader;
        private static Dictionary<Renderer, Shader> _originalShaders = new Dictionary<Renderer, Shader>();



        // --- Item & Container ESP Settings ---
        internal static ConfigEntry<bool> ItemEspEnabled;
        internal static ConfigEntry<bool> ContainerEspEnabled;
        internal static ConfigEntry<string> ItemEspFilter;
        internal static ConfigEntry<float> ItemEspMaxDistance;
        internal static ConfigEntry<float> ItemEspUpdateInterval;
        internal static ConfigEntry<Color> ColorItem;
        internal static ConfigEntry<Color> ColorContainer;

        // --- Performance Settings ---
        internal static ConfigEntry<bool> PerformanceMode;
        internal static ConfigEntry<float> BotRenderDistance;

        private Harmony _harmony;

        // --- UI ---
        private bool _showUi;
        private Rect _windowRect = new Rect(25, 25, 500, 750);
        private Vector2 _mainScroll;
        private bool _isResizing;
        private Vector2 _resizeStartMouse;
        private Rect _resizeStartRect;
        private const float ResizeGripSize = 18f;
        private const float MinWindowWidth = 400f;
        private const float MinWindowHeight = 500f;
        private const float DragBarHeight = 22f;
        private const int WindowId = 987654;

        // --- Status Window (Mini Window) ---
        private Rect _statusRect = new Rect(Screen.width - 210, 20, 200, 165);
        private GUIStyle _statusStyle;
        private bool _statusStyleInitialized;

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
        private const float ContainerCacheInterval = 10.0f;

        private void Awake()
        {
            Instance = this;

            string hotkeyDesc = "Hotkey to toggle this feature. Use Unity KeyCode names (e.g., Keypad0, Keypad1, Insert, Home, PageUp).";

            // General Binds
            GodModeEnabled = Config.Bind("General", "GodMode", false, "Player takes no damage.");
            InfiniteStaminaEnabled = Config.Bind("General", "Infinite Stamina", false, "Unlimited stamina and breath.");
            NoWeightEnabled = Config.Bind("General", "No Weight", false, "Removes weight penalties.");
            StatusWindowEnabled = Config.Bind("General", "Status Window", true, "Show the mini status window.");

            ShowWeaponInfo = Config.Bind("General", "Show Weapon Info", true, "Show current weapon and ammo in status window.");
            ToggleWeaponInfoHotkey = Config.Bind("Hotkeys", "12. Toggle Weapon Info", new KeyboardShortcut(KeyCode.L), "Hotkey to toggle weapon info display.");
            _stackCountField = typeof(Item).GetField("StackObjectsCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            // Hotkeys - Configured to Numpad 0-9 by default
            ToggleUiHotkey = Config.Bind("Hotkeys", "01. Toggle UI", new KeyboardShortcut(KeyCode.Insert), hotkeyDesc);
            ToggleStatusHotkey = Config.Bind("Hotkeys", "02. Toggle Status Window", new KeyboardShortcut(KeyCode.Keypad0), hotkeyDesc);
            ToggleGodModeHotkey = Config.Bind("Hotkeys", "03. Toggle GodMode", new KeyboardShortcut(KeyCode.Keypad1), hotkeyDesc);
            ToggleStaminaHotkey = Config.Bind("Hotkeys", "04. Toggle Stamina", new KeyboardShortcut(KeyCode.Keypad2), hotkeyDesc);
            ToggleWeightHotkey = Config.Bind("Hotkeys", "06. Toggle Weight", new KeyboardShortcut(KeyCode.Keypad4), hotkeyDesc);
            ToggleEspHotkey = Config.Bind("Hotkeys", "07. Toggle Player ESP", new KeyboardShortcut(KeyCode.Keypad5), hotkeyDesc);
            ToggleItemEspHotkey = Config.Bind("Hotkeys", "08. Toggle Item ESP", new KeyboardShortcut(KeyCode.Keypad6), hotkeyDesc);
            ToggleContainerEspHotkey = Config.Bind("Hotkeys", "09. Toggle Container ESP", new KeyboardShortcut(KeyCode.Keypad7), hotkeyDesc);
            ToggleCullingHotkey = Config.Bind("Hotkeys", "10. Toggle Culling", new KeyboardShortcut(KeyCode.Keypad8), hotkeyDesc);
            ToggleUnlockDoorsHotkey = Config.Bind("Hotkeys", "11. Unlock All Doors", new KeyboardShortcut(KeyCode.Keypad9), hotkeyDesc);

            // Player ESP Binds
            EspEnabled = Config.Bind("ESP Players", "Enabled", false, "Show players/bots.");
            EspUpdateInterval = Config.Bind("ESP Players", "Update Interval", 0.05f, "Update rate for player ESP.");
            EspMaxDistance = Config.Bind("ESP Players", "Max Distance", 400f, "Max distance for players.");
            ColorBear = Config.Bind("ESP Players", "Color BEAR", Color.red, "Color for BEAR faction.");
            ColorUsec = Config.Bind("ESP Players", "Color USEC", Color.blue, "Color for USEC faction.");
            ColorSavage = Config.Bind("ESP Players", "Color Savage", Color.yellow, "Color for Scavs/Bots.");
            ColorBoss = Config.Bind("ESP Players", "Color Boss", new Color(0.5f, 0f, 0.5f), "Color for Bosses.");

            ChamsEnabled = Config.Bind("ESP Players", "Chams Enabled", false, "Enable colored models.");
            ChamsIntensity = Config.Bind("ESP Players", "Chams Intensity", 0.5f, "Brightness of Chams colors (0.1 to 1.0).");
            ToggleChamsHotkey = Config.Bind("Hotkeys", "13. Toggle Chams", new KeyboardShortcut(KeyCode.K), "Hotkey to toggle Chams.");
            _chamsShader = Shader.Find("Hidden/Internal-Colored");

            // Item & Container ESP Binds
            ItemEspEnabled = Config.Bind("ESP Items", "Enabled", false, "Show loose loot.");
            ContainerEspEnabled = Config.Bind("ESP Containers", "Enabled", false, "Show items inside containers.");
            ItemEspFilter = Config.Bind("ESP Items", "Filter", "", "Filter by name or ID (comma separated).");
            ItemEspMaxDistance = Config.Bind("ESP Items", "Max Distance", 100f, "Max distance for items.");
            ItemEspUpdateInterval = Config.Bind("ESP Items", "Update Interval", 0.5f, "Update rate for item ESP.");
            ColorItem = Config.Bind("ESP Items", "Color", Color.green, "Color for loose items.");
            ColorContainer = Config.Bind("ESP Containers", "Color", new Color(1f, 0.5f, 0f), "Color for container items.");

            // Performance Binds
            PerformanceMode = Config.Bind("Performance", "Enable Distance Culling", true, "Only render bots within distance.");
            BotRenderDistance = Config.Bind("Performance", "Bot Render Distance", 500f, "Distance to stop rendering bots.");

            _harmony = new Harmony("com.master.tools");

            // Patches
            TryPatchDamageMethod(typeof(Player), "ApplyDamageInfo", nameof(BlockDamagePrefix_Player));
            TryPatchDamageMethod(typeof(Player), "ApplyDamage", nameof(BlockDamagePrefix_Player));
            TryPatchDamageMethod(typeof(ActiveHealthController), "ApplyDamage", nameof(BlockDamagePrefix_ActiveHealthController));

            Logger.LogInfo("[AdvancedMod] Loaded. INSERT: UI, Keypad 0-9: Cheats.");
        }

        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
        }

        private void Update()
        {
            // Hotkey Handling
            if (ToggleChamsHotkey.Value.IsDown()) ChamsEnabled.Value = !ChamsEnabled.Value;
            if (ToggleUiHotkey.Value.IsDown()) _showUi = !_showUi;
            if (ToggleStatusHotkey.Value.IsDown()) StatusWindowEnabled.Value = !StatusWindowEnabled.Value;
            if (ToggleWeaponInfoHotkey.Value.IsDown()) ShowWeaponInfo.Value = !ShowWeaponInfo.Value;
            if (ToggleGodModeHotkey.Value.IsDown()) GodModeEnabled.Value = !GodModeEnabled.Value;
            if (ToggleStaminaHotkey.Value.IsDown()) InfiniteStaminaEnabled.Value = !InfiniteStaminaEnabled.Value;
            if (ToggleWeightHotkey.Value.IsDown()) NoWeightEnabled.Value = !NoWeightEnabled.Value;
            if (ToggleEspHotkey.Value.IsDown()) EspEnabled.Value = !EspEnabled.Value;
            if (ToggleItemEspHotkey.Value.IsDown()) ItemEspEnabled.Value = !ItemEspEnabled.Value;
            if (ToggleContainerEspHotkey.Value.IsDown()) ContainerEspEnabled.Value = !ContainerEspEnabled.Value;
            if (ToggleCullingHotkey.Value.IsDown()) PerformanceMode.Value = !PerformanceMode.Value;
            if (ToggleUnlockDoorsHotkey.Value.IsDown()) UnlockAllDoors();

            if (Time.time >= _nextLocalRefresh)
            {
                RefreshLocalReferences();
                _nextLocalRefresh = Time.time + LocalRefreshIntervalSeconds;
            }

            if (_localPlayer != null)
            {
                if (InfiniteStaminaEnabled.Value) HandleInfiniteStamina();
                HandlePerformanceCulling();
            }

            // ESP Update Logic
            if (EspEnabled.Value && _localPlayer != null && Time.time >= _nextEspUpdate)
            {
                UpdateEsp();
                _nextEspUpdate = Time.time + EspUpdateInterval.Value;
            }

            //Chams with 60fps
            UpdateChams();
            
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

        private void HandlePerformanceCulling()
        {
            if (_gameWorld == null || _localPlayer == null) return;
            try
            {
                var players = _gameWorld.RegisteredPlayers;
                foreach (var p in players)
                {
                    if (p == null || p.IsYourPlayer) continue;

                    GameObject botObj = (p as Component)?.gameObject;
                    if (botObj == null) continue;

                    bool shouldRender = true;
                    if (PerformanceMode.Value)
                    {
                        float dist = Vector3.Distance(_localPlayer.Transform.position, p.Transform.position);
                        shouldRender = dist <= BotRenderDistance.Value;
                    }

                    if (botObj.activeSelf != shouldRender)
                        botObj.SetActive(shouldRender);
                }
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
            if (!_statusStyleInitialized) InitializeStatusStyle();

            if (_localPlayer != null && _mainCamera != null)
            {
                if (EspEnabled.Value) RenderEsp();
                if (ItemEspEnabled.Value || ContainerEspEnabled.Value) RenderItemEsp();
                if (StatusWindowEnabled.Value) DrawStatusWindow();
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

        private void InitializeStatusStyle()
        {
            _statusStyle = new GUIStyle(GUI.skin.box);
            _statusStyle.alignment = TextAnchor.UpperLeft;
            _statusStyle.fontSize = 11;
            _statusStyle.normal.textColor = Color.white;
            _statusStyleInitialized = true;
        }

        private void DrawStatusWindow()
        {
            string status = "<b>[ MOD STATUS ]</b>\n";
            status += $"GodMode: <color={(GodModeEnabled.Value ? "green" : "red")}>{(GodModeEnabled.Value ? "ON" : "OFF")}</color>\n";
            status += $"Stamina: <color={(InfiniteStaminaEnabled.Value ? "green" : "red")}>{(InfiniteStaminaEnabled.Value ? "ON" : "OFF")}</color>\n";
            status += $"Weight: <color={(NoWeightEnabled.Value ? "green" : "red")}>{(NoWeightEnabled.Value ? "ON" : "OFF")}</color>\n";
            status += $"ESP: <color={(EspEnabled.Value ? "green" : "red")}>{(EspEnabled.Value ? "ON" : "OFF")}</color>\n";

            if (ShowWeaponInfo.Value && _localPlayer != null)
            {
                try
                {
                    var handsController = _localPlayer.HandsController;
                    if (handsController != null && handsController.Item is Weapon weapon)
                    {
                        string weaponName = weapon.ShortName.Localized();
                        int currentAmmo = weapon.GetCurrentMagazineCount();
                        int maxAmmo = weapon.GetMaxMagazineCount();

                        status += $"\n<b>[ WEAPON ]</b>\n";
                        status += $"Name: <color=yellow>{weaponName}</color>\n";
                        status += $"Ammo: <color=cyan>{currentAmmo}/{maxAmmo}</color>";
                    }
                }
                catch { }
            }

            float height = ShowWeaponInfo.Value ? 200 : 140;
            _statusRect.height = height;
            GUI.Box(_statusRect, status, _statusStyle);
        }


        private void UpdateEsp()
        {
            if (_gameWorld == null || _mainCamera == null || !EspEnabled.Value)
            {
                _espTargets.Clear();
                return;
            }

            var players = _gameWorld.RegisteredPlayers;
            if (players == null) return;

            _espTargets.Clear();

            foreach (var player in players)
            {
                if (player is Player playerClass)
                {
                    if (playerClass.IsYourPlayer || !playerClass.HealthController.IsAlive) continue;

                    float dist = Vector3.Distance(_mainCamera.transform.position, playerClass.Transform.position);
                    if (dist > EspMaxDistance.Value) continue;

                    Color textColor = GetPlayerColor(playerClass);
                    textColor.a = 1.0f; // Garante que o texto seja sempre visível

                    Vector3 screenPos = _mainCamera.WorldToScreenPoint(playerClass.Transform.position + Vector3.up * 1.8f);
                    if (screenPos.z > 0)
                    {
                        _espTargets.Add(new EspTarget
                        {
                            ScreenPosition = new Vector2(screenPos.x, Screen.height - screenPos.y),
                            Distance = dist,
                            Nickname = playerClass.Profile.Nickname,
                            Side = playerClass.Side.ToString(),
                            Color = textColor
                        });
                    }
                }
            }
        }

        private void UpdateChams()
        {
            if (_gameWorld == null || _mainCamera == null) return;

            var players = _gameWorld.RegisteredPlayers;
            if (players == null) return;

            foreach (var player in players)
            {
                if (player is Player playerClass)
                {
                  
                    float dist = Vector3.Distance(_mainCamera.transform.position, playerClass.Transform.position);

                    bool shouldChams = ChamsEnabled.Value &&
                                       !playerClass.IsYourPlayer &&
                                       playerClass.HealthController.IsAlive &&
                                       dist <= EspMaxDistance.Value;

                    if (shouldChams)
                    {
                        Color color = GetPlayerColor(playerClass);
                        ApplyChams(playerClass, color);
                    }
                    else
                    {
                        
                        ResetChams(playerClass);
                    }
                }
            }
        }

        private void UpdateItemAndContainerEsp()
        {
            _itemEspTargets.Clear();
            if (_gameWorld == null || _mainCamera == null) return;
            string[] filters = ItemEspFilter.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim().ToLower()).ToArray();
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
            if (ContainerEspEnabled.Value)
            {
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
                    Vector3 containerPos = container.transform.position;
                    float distSq = (containerPos - playerPos).sqrMagnitude;
                    if (distSq > maxDistSq) continue;
                    if (container.ItemOwner == null || container.ItemOwner.RootItem == null) continue;
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
            GodModeEnabled.Value = GUILayout.Toggle(GodModeEnabled.Value, $" GodMode [{ToggleGodModeHotkey.Value}]");
            InfiniteStaminaEnabled.Value = GUILayout.Toggle(InfiniteStaminaEnabled.Value, $" Infinite Stamina [{ToggleStaminaHotkey.Value}]");
            NoWeightEnabled.Value = GUILayout.Toggle(NoWeightEnabled.Value, $" No Weight Penalties [{ToggleWeightHotkey.Value}]");
            StatusWindowEnabled.Value = GUILayout.Toggle(StatusWindowEnabled.Value, $" Show Status Window [{ToggleStatusHotkey.Value}]");
            ShowWeaponInfo.Value = GUILayout.Toggle(ShowWeaponInfo.Value, $" Show Weapon Info in Status [{ToggleWeaponInfoHotkey.Value}]");
            if (GUILayout.Button($"Unlock All Doors in Raid [{ToggleUnlockDoorsHotkey.Value}]")) UnlockAllDoors();

            GUILayout.Space(10);
            GUILayout.Label("<b>--- PERFORMANCE ---</b>");
            PerformanceMode.Value = GUILayout.Toggle(PerformanceMode.Value, $" Enable Bot Distance Culling [{ToggleCullingHotkey.Value}]");
            GUILayout.Label($"Bot Render Distance: {BotRenderDistance.Value:F0}m");
            BotRenderDistance.Value = GUILayout.HorizontalSlider(BotRenderDistance.Value, 50f, 1000f);

            GUILayout.Space(10);
            GUILayout.Label("<b>--- PLAYER ESP ---</b>");
            EspEnabled.Value = GUILayout.Toggle(EspEnabled.Value, $" Enable Player ESP (Text) [{ToggleEspHotkey.Value}]");
            GUILayout.Label($"Max Distance: {EspMaxDistance.Value:F0}m");
            EspMaxDistance.Value = GUILayout.HorizontalSlider(EspMaxDistance.Value, 50f, 1000f);
            GUILayout.Label($"Update Rate (FPS): {1f / EspUpdateInterval.Value:F0}");
            float pFps = GUILayout.HorizontalSlider(1f / EspUpdateInterval.Value, 1f, 60f);
            EspUpdateInterval.Value = 1f / pFps;

            GUILayout.Label("<b>--- CHAMS SETTINGS ---</b>");
            ChamsEnabled.Value = GUILayout.Toggle(ChamsEnabled.Value, $" Enable Player Chams (Models) [{ToggleChamsHotkey.Value}]");
            GUILayout.BeginHorizontal();
            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.Label("<b>--- COLORS & TRANSPARENCY (RGB) ---</b>");
            ColorBear.Value = DrawColorPicker("BEAR", ColorBear.Value);
            ColorUsec.Value = DrawColorPicker("USEC", ColorUsec.Value);
            ColorSavage.Value = DrawColorPicker("SAVAGE / SCAV", ColorSavage.Value);
            ColorBoss.Value = DrawColorPicker("BOSS", ColorBoss.Value);


            GUILayout.Space(10);
            GUILayout.Label("<b>--- ITEM & CONTAINER ESP ---</b>");
            ItemEspEnabled.Value = GUILayout.Toggle(ItemEspEnabled.Value, $" Enable Loose Item ESP [{ToggleItemEspHotkey.Value}]");
            ContainerEspEnabled.Value = GUILayout.Toggle(ContainerEspEnabled.Value, $" Enable Container Item ESP [{ToggleContainerEspHotkey.Value}]");
            GUILayout.Label("Filter (Name or ID, comma separated):");
            ItemEspFilter.Value = GUILayout.TextField(ItemEspFilter.Value);
            GUILayout.Label($"Max Distance: {ItemEspMaxDistance.Value:F0}m");
            ItemEspMaxDistance.Value = GUILayout.HorizontalSlider(ItemEspMaxDistance.Value, 5f, 500f);
            GUILayout.Label($"Update Rate (FPS): {1f / ItemEspUpdateInterval.Value:F0}");
            float iFps = GUILayout.HorizontalSlider(1f / ItemEspUpdateInterval.Value, 1f, 60f);
            ItemEspUpdateInterval.Value = 1f / iFps;

            GUILayout.Space(10);
            GUILayout.Label("<b>--- HOTKEYS (Customizable in .cfg) ---</b>");
            GUILayout.Label($"Menu: {ToggleUiHotkey.Value}");
            GUILayout.Label($"Status Window: {ToggleStatusHotkey.Value}");
            GUILayout.Label($"GodMode: {ToggleGodModeHotkey.Value}");
            GUILayout.Label($"Stamina: {ToggleStaminaHotkey.Value}");
            GUILayout.Label($"Weight: {ToggleWeightHotkey.Value}");
            GUILayout.Label($"Player ESP: {ToggleEspHotkey.Value}");
            GUILayout.Label($"Item ESP: {ToggleItemEspHotkey.Value}");
            GUILayout.Label($"Container ESP: {ToggleContainerEspHotkey.Value}");
            GUILayout.Label($"Culling: {ToggleCullingHotkey.Value}");
            GUILayout.Label($"Unlock Doors: {ToggleUnlockDoorsHotkey.Value}");

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
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<b>{label}</b>");
            GUILayout.BeginHorizontal();

            GUILayout.Label("R", GUILayout.Width(15));
            float r = GUILayout.HorizontalSlider(color.r, 0f, 1f, GUILayout.Width(60));

            GUILayout.Label("G", GUILayout.Width(15));
            float g = GUILayout.HorizontalSlider(color.g, 0f, 1f, GUILayout.Width(60));

            GUILayout.Label("B", GUILayout.Width(15));
            float b = GUILayout.HorizontalSlider(color.b, 0f, 1f, GUILayout.Width(60));

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

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

        private void ApplyChams(Player player, Color color)
        {
            if (player == null) return;
            foreach (var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (renderer == null || renderer.material == null) continue;

                if (renderer.material.shader != _chamsShader)
                {
                    if (!_originalShaders.ContainsKey(renderer))
                        _originalShaders[renderer] = renderer.material.shader;

                    renderer.material.shader = _chamsShader;
                    renderer.material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    renderer.material.SetInt("_ZWrite", 0);
                }
                
                renderer.material.SetColor("_Color", color);
            }
        }



        private void ResetChams(Player player)
        {
            if (player == null) return;
            foreach (var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (renderer != null && renderer.material != null && _originalShaders.ContainsKey(renderer))
                {
                    renderer.material.shader = _originalShaders[renderer];
                    _originalShaders.Remove(renderer);
                }
            }
        }

        private Color GetPlayerColor(Player player)
        {
            
            if (player.Profile.Info.Settings.IsBoss()) return ColorBoss.Value;

            
            if (player.Side == EPlayerSide.Bear) return ColorBear.Value;
            if (player.Side == EPlayerSide.Usec) return ColorUsec.Value;

            
            return ColorSavage.Value;
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
