using AutoSorter.Manager;
using HarmonyLib;
using HMLLibrary;
using Newtonsoft.Json;
using pp.RaftMods.AutoSorter.Protocol;
using RaftModLoader;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace pp.RaftMods.AutoSorter
{
    /// <summary>
    /// Main mod class handles harmony injections, saving/loading of storage and config data, loading of the mod asset bundle and registering/loading storages into the mods data structure.
    /// Also holds and runs the coroutine used to check auto-sorters and storages for item transfer.
    /// </summary>
    public class CAutoSorter : Mod
    {
        /// <summary>
        /// Explicitly typed mod instance for static access.
        /// </summary>
        private static CAutoSorter Get = null;

        public const string MOD_NAME                                = "AutoSorter";
        private const string MOD_NAMESPACE                          = "pp.RaftMods." + MOD_NAME;

        private const string ASSET_PAYLOAD_PATH                     = "assets/autosorter";

        private const string UI_CANVAS_ROOT                         = "Canvases/_CanvasGame_New";
        private const string UI_CONFIG_DIALOG_PREFAB_PATH           = "Assets/Prefabs/UI/AutoSorter_UIRoot.prefab";
        private const string UI_CONFIG_DIALOG_ITEM_PREFAB_PATH      = "Assets/Prefabs/UI/Windows/Config/Item_Entry.prefab";
        private const string UI_HELP_TEXT_PATH                      = "Assets/Config/help.txt";

        private string ModDataDirectory     => Path.Combine(Application.persistentDataPath, "Mods", MOD_NAME);
        private string ModConfigFilePath    => Path.Combine(ModDataDirectory, "config.json");

        private static CModConfig ExtraSettingsAPI_Settings = new CModConfig();

        /// <summary>
        /// Mod configuration object. Loaded from disk on mod load. 
        /// </summary>
        public static CModConfig Config { get => ExtraSettingsAPI_Settings; private set { ExtraSettingsAPI_Settings = value; } }
        /// <summary>
        /// The help text displayed when first opening the auto-sorter UI or whenever the help button is clicked.
        /// Is loaded from the mods asset bundle.
        /// </summary>
        public static string HelpText               { get; private set; }
        /// <summary>
        /// Dialog reference which can be used to prompt the user before specific actions.
        /// </summary>
        public CUIDialog Dialog                     { get; private set; }
        /// <summary>
        /// Handle to the Raft <see cref="SoundManager"/> to play UI sounds.
        /// </summary>
        public SoundManager Sounds                  { get; private set; }
        
        private Harmony mi_harmony;

        private Coroutine mi_chestRoutineHandle;
        private bool mi_checkChests;
        private double mi_lastCheckDurationSeconds;

        private AssetBundle mi_bundle;

        private GameObject mi_uiRoot;
        private CUISorterConfigDialog mi_configDialog;

        private CStorageManager mi_storageManager;
        private CNetwork mi_network;

        #region ENGINE_CALLBACKS
        /// <summary>
        /// Called when the mod is loaded.
        /// </summary>
        private IEnumerator Start()
        {
            if (Get)
            {
                DestroyImmediate(Get);
                CUtil.LogW("Mod has been loaded twice. Destroying old mod instance.");
            }
            Get = this;

            LoadConfig();

            mi_harmony = new Harmony(MOD_NAMESPACE);
            mi_harmony.PatchAll(Assembly.GetExecutingAssembly());
            if (mi_bundle == null)
            {
                CUtil.LogD("Loading mod bundle @ " + ASSET_PAYLOAD_PATH + "...");
                AssetBundleCreateRequest request = AssetBundle.LoadFromMemoryAsync(GetEmbeddedFileBytes(ASSET_PAYLOAD_PATH));
                yield return request;
                mi_bundle = request.assetBundle;
                if (!mi_bundle)
                {
                    CUtil.LogE("Failed to load embedded bundle. This usually happens if you load two instances of the auto sorter mod.");
                    DestroyImmediate(Get);
                    yield break;
                }
            }

            HelpText = mi_bundle.LoadAsset<TextAsset>(UI_HELP_TEXT_PATH)?.text?.Trim();
            if (string.IsNullOrEmpty(HelpText))
            {
                CUtil.LogW("Help text file could not be read.");
                HelpText = "Failed to load!";
            }

            Sounds = ComponentManager<SoundManager>.Value;
            if (!Sounds)
            {
                CUtil.LogW("Failed to get sound manager on mod load.");
            }

            mi_network = new CNetwork(this);
            mi_storageManager = new CStorageManager(ModDataDirectory, mi_network);
            mi_storageManager.LoadStorageData();

            CUtil.Log($"{MOD_NAME} v. {version} loaded.");
        }

        private void OnDestroy()
        {
            CUtil.LogD($"Destroying {MOD_NAME}...");
            mi_checkChests = false;
            StopAllCoroutines();
            mi_storageManager.Cleanup();

            mi_network.Clear();

            if (mi_harmony != null)
            {
                mi_harmony.UnpatchAll(MOD_NAMESPACE);
            }

            if (mi_bundle)
            {
                mi_bundle.Unload(true);
                mi_bundle = null;
            }

            if (mi_uiRoot)
            {
                GameObject.DestroyImmediate(mi_uiRoot);
            }
            mi_uiRoot = null;

            Get = null;
            CUtil.LogD($"Destroyed {MOD_NAME}!");
        }
        #endregion

        #region MOD_CALLBACKS
        /// <summary>
        /// Called whenever the world is loaded while transitioning from the main menu to the world.
        /// </summary>
        public override void WorldEvent_WorldLoaded()
        {
            base.WorldEvent_WorldLoaded();
            if (mi_uiRoot == null)
            {
                LoadUI();
            }
            StartCoroutine(mi_configDialog.LoadItems());

            if (Raft_Network.IsHost)
            {
                mi_checkChests = true;
                mi_chestRoutineHandle = StartCoroutine(Run());
            }
            CUtil.LogD($"World \"{SaveAndLoad.CurrentGameFileName}\" loaded.");
        }

        /// <summary>
        /// Called whenever the world is unloaded while transitioning from the world to the main menu.
        /// </summary>
        public override void Event_ReturnToMainMenu()
        {
            base.Event_ReturnToMainMenu();

            mi_checkChests = false;
            StopAllCoroutines();

            mi_storageManager.Cleanup();
            mi_network.Clear();
        }

        /// <summary>
        /// Called whenever the user saves the world pressing "Save" in the in-game menu.
        /// </summary>
        public override void WorldEvent_WorldSaved()
        {
            base.WorldEvent_WorldSaved();
            mi_storageManager.SaveStorageData();
        }

        public override bool OnNetworkMessage(object message, Network_UserId from, string modslug)
            => mi_network.OnNetworkMessage(message, from);
        #endregion

        public void SaveConfig()
        {
            try
            {
                if (!Directory.Exists(ModDataDirectory))
                {
                    Directory.CreateDirectory(ModDataDirectory);
                }

                if (Config == null)
                {
                    Config = new CModConfig();
                }

                CUtil.LogD("Save configuration.");
                File.WriteAllText(
                    ModConfigFilePath,
                    JsonConvert.SerializeObject(
                        Config,
                        Formatting.Indented,
                        new JsonSerializerSettings()
                        {
                            DefaultValueHandling = DefaultValueHandling.Include
                        }) ?? throw new System.Exception("Failed to serialize"));
            }
            catch (System.Exception _e)
            {
                CUtil.LogW("Failed to save mod configuration: " + _e.Message);
                CUtil.LogD(_e.StackTrace);
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(ModConfigFilePath))
                {
                    SaveConfig();
                    return;
                }
                CUtil.LogD("Load configuration.");
                Config = JsonConvert.DeserializeObject<CModConfig>(File.ReadAllText(ModConfigFilePath)) ?? throw new System.Exception("De-serialisation failed.");
                if(Config.UpgradeCosts != null)
                {
                    foreach (var cost in Config.UpgradeCosts) cost.Load();
                }
            }
            catch (System.Exception _e)
            {
                CUtil.LogW("Failed to load mod configuration: " + _e.Message + ". Check your configuration file.");
                CUtil.LogD(_e.StackTrace);
            }
        }

        private IEnumerator Run()
        {
            while (mi_checkChests)
            {
                if (!RAPI.IsCurrentSceneGame())
                {
                    mi_checkChests = false;
                    break;
                }

                if (!GameModeValueManager.GetCurrentGameModeValue().playerSpecificVariables.unlimitedResources && 
                    mi_storageManager.HasStorages)
                {
                    System.DateTime now = System.DateTime.UtcNow;

                    yield return mi_storageManager.UpdateStorages();

                    mi_lastCheckDurationSeconds = (System.DateTime.UtcNow - now).TotalSeconds;
                }
                if (mi_lastCheckDurationSeconds < Config.CheckIntervalSeconds)
                {
                    yield return new WaitForSeconds((float)(Config.CheckIntervalSeconds - mi_lastCheckDurationSeconds));
                }

                yield return new WaitForEndOfFrame();
            }
        }
        
        private void LoadUI()
        {
            CUtil.LogD("Loading auto-sorter UI...");
            GameObject rootAsset = mi_bundle.LoadAsset<GameObject>(UI_CONFIG_DIALOG_PREFAB_PATH);
            if (!rootAsset)
            {
                CUtil.LogE("Failed to load UI root asset from bundle.");
                return;
            }
            GameObject itemAsset = mi_bundle.LoadAsset<GameObject>(UI_CONFIG_DIALOG_ITEM_PREFAB_PATH);
            if (!rootAsset)
            {
                CUtil.LogE("Failed to load UI item asset from bundle.");
                return;
            }

            var canvasRoot = GameObject.Find(UI_CANVAS_ROOT);
            if (!canvasRoot)
            {
                CUtil.LogE("Failed to load rafts UI canvases anchor. The mod might be out of date.");
                return;
            }

            mi_uiRoot = GameObject.Instantiate(rootAsset, canvasRoot.transform, false);
            mi_uiRoot.transform.SetAsLastSibling();
            foreach(var t in mi_uiRoot.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = LayerMask.NameToLayer("UI");
            }

            Transform configDialogRoot = mi_uiRoot.transform.Find("ConfigDialog");
            mi_configDialog = configDialogRoot.gameObject.AddComponent<CUISorterConfigDialog>();
            mi_configDialog.Load(this, itemAsset, mi_network);

            Transform dialogRoot = mi_uiRoot.transform.Find("Dialog");
            Dialog = dialogRoot.gameObject.AddComponent<CUIDialog>();
            Dialog.Load(this);
            CUtil.LogD("Auto-sorter UI loaded!");
        }

        private void OnBlockCreated(Storage_Small _storage)
        {
            if (Get == null) return; //mod is being unloaded

            if (_storage == null) return;

            mi_storageManager.RegisterStorage(_storage);
        }

        private IEnumerator WaitAndShowUI(CSceneStorage _storage)
        {
            while (mi_configDialog == null) yield return new WaitForEndOfFrame();
            if (!_storage.StorageComponent.IsOpen) yield break;
            mi_configDialog.Show(_storage);
        }

        #region PATCHES
        [HarmonyPatch(typeof(BlockCreator), "CreateBlock")]
        private class CHarmonyPatch_BlockCreator_CreateBlock
        {
            //Intercept create block method so we can check each created block and register it if it is a storage
            [HarmonyPostfix]
            private static void CreateBlock(BlockCreator __instance, Block __result) => Get.OnBlockCreated(__result as Storage_Small);
        }

        [HarmonyPatch(typeof(RemovePlaceables), "PickupBlock")]
        private class CHarmonyPatch_RemovePlaceables_PickupBlock
        {
            [HarmonyPrefix]
            private static void PickupBlock(RemovePlaceables __instance, Block block)
            {
                if (!(block is Storage_Small)) return;
                var storage = Get.mi_storageManager.GetStorageByIndex(block.ObjectIndex);
                if (storage == null)
                {
                    CUtil.LogW("Failed to find matching storage on storage pickup. This is a bug and should be reported.");
                    return;
                }
                if (!storage.IsUpgraded) return;
                var pickupPlayer = Traverse.Create(__instance).Field("playerNetwork").GetValue<Network_Player>();
                if(pickupPlayer == null || !pickupPlayer.IsLocalPlayer)
                    return;
                CUtil.ReimburseConstructionCosts(pickupPlayer, false);
            }
        }

        [HarmonyPatch(typeof(Storage_Small))]
        private class CHarmonyPatch_Storage_Small
        {
            private static Coroutine mi_routine;

            [HarmonyPostfix][HarmonyPatch("Open")]
            private static void Open(Storage_Small __instance, Network_Player player)
            {
                if (player == null || !player.IsLocalPlayer) return;

                var storage = Get.mi_storageManager.GetStorageByIndex(__instance.ObjectIndex);
                if (storage == null)
                {
                    CUtil.LogW("Failed to find matching storage on storage open. This is a bug and should be reported.");
                    return;
                }

                if (Get.mi_configDialog == null) //If the UI is not fully loaded directly after entering the scene, make sure we wait until its available before showing it to the user.
                {
                    if (mi_routine != null)
                    {
                        Get.StopCoroutine(mi_routine);
                    }
                    mi_routine = Get.StartCoroutine(Get.WaitAndShowUI(storage));
                    return;
                }

                Get.mi_configDialog.Show(storage);
            }

            [HarmonyPostfix][HarmonyPatch("Close")]
            private static void Close(Storage_Small __instance, Network_Player player)
            {
                if (player == null || !player.IsLocalPlayer) return;

                Get.mi_configDialog?.Hide();

                var storage = Get.mi_storageManager.GetStorageByIndex(__instance.ObjectIndex);
                if (storage == null)
                {
                    CUtil.LogW("Failed to find matching storage on storage close. This is a bug and should be reported.");
                    return;
                }

                Get.mi_network.Broadcast(new CDTO(EStorageRequestType.STORAGE_DATA_UPDATE, storage.AutoSorter.ObjectIndex) { Info = storage.Data });
            }
        }

        [HarmonyPatch(typeof(Inventory))]
        private class CHarmonyPatch_Inventory
        {
            [HarmonyPostfix]
            [HarmonyPatch("SetSlotsFromRGD")]
            private static void SetSlotsFromRGD(Inventory __instance, RGD_Slot[] slots)
                => Get.mi_storageManager.SetStorageInventoryDirty(__instance);
        }
        #endregion

        #region COMMANDS
        [ConsoleCommand("asListStorages", "Lists all storages and their status.")]
        public static string ListStorages(string[] _args)
        {
            return $"### Tracked scene storages ({Get.mi_storageManager.SceneStorages?.Count ?? 0}) ###\n" + 
                (
                    Get.mi_storageManager.SceneStorages.Count == 0 ? 
                        "No registered storages in scene." :
                        "- " + string.Join("\n- ",
                            Get.mi_storageManager.SceneStorages.Values.Select(_o => $"\"{_o.StorageComponent.gameObject.name}\" IsOpen: {_o.StorageComponent.IsOpen} Dirty: {_o.IsInventoryDirty} AutoSorter: {_o.IsUpgraded} " +
                                $"{(_o.IsUpgraded ? $" Priority: {_o.Data.Priority} Filters: {_o.Data.Filters.Count}" : "")}" +
                                $"{(_o.AdditionalData != null ? " Ignore: " + _o.AdditionalData.Ignore : "")}")));
        }

        [ConsoleCommand("asTestReduceUses", "Sets the remaining uses of all items in the players inventory to half the maximum uses.")]
        public static string ReduceUses(string[] _args)
        {
            int c = 0;
            var network = ComponentManager<Raft_Network>.Value;
            foreach (var slot in network.GetLocalPlayer().Inventory.allSlots)
            {
                if (slot.HasValidItemInstance())
                {
                    slot.itemInstance.Uses = (int)Mathf.Ceil(slot.itemInstance.BaseItemMaxUses * 0.5f);
                    CUtil.Log("Setting max uses of " + slot.itemInstance.UniqueName + " to " + slot.itemInstance.Uses);
                    c++;
                }
            }
            return "Set item uses on " + c + " items.";
        }

        [ConsoleCommand("asTestMaxUseableAndStackable", "Prints all items that have maxUses > 1 and a stack size > 1.")]
        public static string PrintMaxUseableAndStackable(string[] _args)
        {
            var items = ItemManager.GetAllItems().Where(_o => _o.MaxUses > 1 && _o.settings_Inventory.Stackable);
            return "### Items ###\n" + (items.Any() ? "- " + string.Join("\n- ", items) : "None");
        }
        #endregion
    }
}