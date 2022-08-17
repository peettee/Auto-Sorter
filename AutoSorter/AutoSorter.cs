using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using pp.RaftMods.AutoSorter.Protocol;
using Steamworks;
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
        public static CAutoSorter Get = null;

        public const string VERSION                                 = "1.1.0";
        public const string MOD_NAME                                = "AutoSorter";
        private const string MOD_NAMESPACE                          = "pp.RaftMods." + MOD_NAME;

        private const string ASSET_PAYLOAD_PATH                     = "assets/autosorter";

        private const string UI_CANVAS_ROOT                         = "Canvases/_CanvasGame_New";
        private const string UI_CONFIG_DIALOG_PREFAB_PATH           = "Assets/Prefabs/UI/AutoSorter_UIRoot.prefab";
        private const string UI_CONFIG_DIALOG_ITEM_PREFAB_PATH      = "Assets/Prefabs/UI/Windows/Config/Item_Entry.prefab";
        private const string UI_HELP_TEXT_PATH                      = "Assets/Config/help.txt";

        /// <summary>
        /// Storage data which is loaded and saved to disk to preserve auto-sorter configurations.
        /// Combines the world name and the storage data for the world.
        /// </summary>
        public Dictionary<string, CSorterStorageData[]> SavedSorterStorageData { get; private set; } = new Dictionary<string, CSorterStorageData[]>();

        /// <summary>
        /// Additional storage data that is loaded and saved to disk to preserve certain settings for all storages (auto-sorter or not).
        /// </summary>
        public Dictionary<string, CGeneralStorageData[]> SavedAdditionaStorageData { get; private set; } = new Dictionary<string, CGeneralStorageData[]>();

        public List<Inventory> DirtyInventories { get; private set; } = new List<Inventory>();

        private string ModDataDirectory     => Path.Combine(Application.persistentDataPath, "Mods", MOD_NAME);
        private string ModConfigFilePath    => Path.Combine(ModDataDirectory, "config.json");
        private string ModDataFilePath      => Path.Combine(ModDataDirectory, "storagedata.json");
        private string ModAdditionalDataFilePath => Path.Combine(ModDataDirectory, "additional_storagedata.json");

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
        /// All currently loaded scene storages. Is loaded on world load.
        /// </summary>
        public List<CSceneStorage> SceneStorages    { get; private set; }
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
        private Raft_Network mi_network;

        private static short mi_modMessagesFloor = short.MaxValue;
        private static short mi_modMessagesCeil = short.MinValue;

        private static Dictionary<uint, CStorageBehaviour> mi_registeredNetworkBehaviours = new Dictionary<uint, CStorageBehaviour>();

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

            foreach (EStorageRequestType m in System.Enum.GetValues(typeof(EStorageRequestType)))
            {
                mi_modMessagesFloor = (short)Mathf.Min(mi_modMessagesFloor, (short)m);
                mi_modMessagesCeil  = (short)Mathf.Max(mi_modMessagesCeil, (short)m);
            }

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

            mi_network = ComponentManager<Raft_Network>.Value;
            if (!mi_network)
            {
                CUtil.LogW("Failed to get network manager on mod load.");
            }

            Sounds = ComponentManager<SoundManager>.Value;
            if (!Sounds)
            {
                CUtil.LogW("Failed to get sound manager on mod load.");
            }

            LoadSorterStorageData();
            LoadAdditionalStorageData();

            CUtil.Log($"{MOD_NAME} v. {VERSION} loaded.");
        }

        private void OnDestroy()
        {
            CUtil.LogD($"Destroying {MOD_NAME}...");
            mi_checkChests = false;
            if (mi_chestRoutineHandle != null)
            {
                StopCoroutine(mi_chestRoutineHandle);
            }

            //undo all changes to the scene
            if (SceneStorages != null)
            {
                for(int i = 0; i < SceneStorages.Count; ++i)
                {
                    if (SceneStorages[i].AutoSorter)
                    {
                        Component.DestroyImmediate(SceneStorages[i].AutoSorter);
                        --i;
                    }
                }
            }

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
            mi_checkChests = true;
            mi_chestRoutineHandle = StartCoroutine(CheckStorages());
        }

        /// <summary>
        /// Called whenever the world is unloaded while transitioning from the world to the main menu.
        /// </summary>
        public override void WorldEvent_WorldUnloaded()
        {
            base.WorldEvent_WorldUnloaded();

            mi_checkChests = false;
            StopAllCoroutines();

            DirtyInventories.Clear();
            if (SceneStorages != null)
            {
                SceneStorages.Clear();
            }
            SceneStorages = null;
        }

        /// <summary>
        /// Called whenever the user saves the world pressing "Save" in the in-game menu.
        /// </summary>
        public override void WorldEvent_WorldSaved()
        {
            base.WorldEvent_WorldSaved();
            SaveSorterStorageData();
            SaveAdditionalStorageData();
        }
        #endregion

        public void UnregisterNetworkBehaviour(CStorageBehaviour _behaviour)
        {
            mi_registeredNetworkBehaviours.Remove(_behaviour.ObjectIndex);
        }

        public void SendTo(CDTO _object, CSteamID _id)
        {
            CUtil.LogD("Sending " + _object.Type + " to " + _id.m_SteamID + ".");
            mi_network.SendP2P(_id, CreateCarrierDTO(_object), EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        }

        public void Broadcast(CDTO _object)
        {
            CUtil.LogD("Broadcasting " + _object.Type + " to others.");
            mi_network.RPC(CreateCarrierDTO(_object), Target.Other, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        }

        public void BroadcastInventoryState(CStorageBehaviour _storageBehaviour)
        {
            CUtil.LogD("Broadcasting storage inventory change to others.");
            mi_network.RPC(new Message_Storage_Close((Messages)EStorageRequestType.STORAGE_INVENTORY_UPDATE, _storageBehaviour.LocalPlayer.StorageManager, _storageBehaviour.SceneStorage.StorageComponent), Target.Other, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        }

        public void RegisterNetworkBehaviour(CStorageBehaviour _behaviour)
        {
            if (mi_registeredNetworkBehaviours.ContainsKey(_behaviour.ObjectIndex))
            {
                CUtil.LogW("Behaviour with ID" + _behaviour.ObjectIndex + " \"" + _behaviour.name + "\" was already registered.");
                return;
            }

            mi_registeredNetworkBehaviours.Add(_behaviour.ObjectIndex, _behaviour);
        }

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
            }
            catch (System.Exception _e)
            {
                CUtil.LogW("Failed to load mod configuration: " + _e.Message + ". Check your configuration file.");
            }
        }

        private void LoadSorterStorageData()
        {
            try
            {
                if (!File.Exists(ModDataFilePath)) return;

                CSorterStorageData[] data = JsonConvert.DeserializeObject<CSorterStorageData[]>(File.ReadAllText(ModDataFilePath)) ?? throw new System.Exception("De-serialisation failed.");
                SavedSorterStorageData = data
                    .GroupBy(_o => _o.SaveName)
                    .Select(_o => new KeyValuePair<string, CSorterStorageData[]>(_o.Key, _o.ToArray()))
                    .ToDictionary(_o => _o.Key, _o => _o.Value);

                foreach (var allStorageData in SavedSorterStorageData)
                {
                    foreach (var storageData in allStorageData.Value)
                    {
                        storageData.OnAfterDeserialize();
                    }
                }
            }
            catch (System.Exception _e)
            {
                CUtil.LogW("Failed to load saved mod data: " + _e.Message + ". Storage data wont be loaded.");
                SavedSorterStorageData = new Dictionary<string, CSorterStorageData[]>();
            }
        }

        private void SaveSorterStorageData()
        {
            try
            { 
                if(SceneStorages == null || SceneStorages.Count == 0) return;

                if (File.Exists(ModDataFilePath))
                {
                    File.Delete(ModDataFilePath);
                }

                if (SavedSorterStorageData.ContainsKey(SaveAndLoad.CurrentGameFileName))
                {
                    SavedSorterStorageData.Remove(SaveAndLoad.CurrentGameFileName);
                }

                foreach (var storage in SceneStorages)
                {
                    storage.Data?.OnBeforeSerialize();
                }

                SavedSorterStorageData.Add(
                    SaveAndLoad.CurrentGameFileName,
                    SceneStorages
                        .Where(_o => _o.AutoSorter && _o.IsUpgraded)
                        .Select(_o => _o.Data)
                        .ToArray());

                File.WriteAllText(
                    ModDataFilePath,
                    JsonConvert.SerializeObject(
                        SavedSorterStorageData.SelectMany(_o => _o.Value).ToArray(),
                        Formatting.None,
                        new JsonSerializerSettings()
                        {
                            DefaultValueHandling = DefaultValueHandling.Ignore
                        }) ?? throw new System.Exception("Failed to serialize"));
            }
            catch (System.Exception _e)
            {
                CUtil.LogW("Failed to save mod data: " + _e.Message + ". Storage data wont be saved.");
            }
        }

        private void LoadAdditionalStorageData()
        {
            try
            {
                if (!File.Exists(ModAdditionalDataFilePath)) return;

                CGeneralStorageData[] data = JsonConvert.DeserializeObject<CGeneralStorageData[]>(File.ReadAllText(ModAdditionalDataFilePath)) ?? throw new System.Exception("De-serialisation failed.");
                SavedAdditionaStorageData = data
                    .GroupBy(_o => _o.SaveName)
                    .Select(_o => new KeyValuePair<string, CGeneralStorageData[]>(_o.Key, _o.ToArray()))
                    .ToDictionary(_o => _o.Key, _o => _o.Value);
            }
            catch (System.Exception _e)
            {
                CUtil.LogW("Failed to load additional saved mod data: " + _e.Message + ". Storage data wont be loaded.");
                SavedAdditionaStorageData = new Dictionary<string, CGeneralStorageData[]>();
            }
        }

        private void SaveAdditionalStorageData()
        {
            try
            {
                if (SceneStorages == null || SceneStorages.Count == 0) return;

                if (File.Exists(ModAdditionalDataFilePath))
                {
                    File.Delete(ModAdditionalDataFilePath);
                }

                if (SavedAdditionaStorageData.ContainsKey(SaveAndLoad.CurrentGameFileName))
                {
                    SavedAdditionaStorageData.Remove(SaveAndLoad.CurrentGameFileName);
                }

                SavedAdditionaStorageData.Add(
                    SaveAndLoad.CurrentGameFileName,
                    SceneStorages
                        .Where(_o => _o.AdditionalData != null)
                        .Select(_o => _o.AdditionalData)
                        .ToArray());

                File.WriteAllText(
                    ModAdditionalDataFilePath,
                    JsonConvert.SerializeObject(
                        SavedAdditionaStorageData.SelectMany(_o => _o.Value).ToArray(),
                        Formatting.None,
                        new JsonSerializerSettings()
                        {
                            DefaultValueHandling = DefaultValueHandling.Ignore
                        }) ?? throw new System.Exception("Failed to serialize"));
            }
            catch (System.Exception _e)
            {
                CUtil.LogW("Failed to save additional mod data: " + _e.Message + ". Storage data wont be saved.");
            }
        }

        private IEnumerator CheckStorages()
        {
            while (mi_checkChests)
            {
                if (!RAPI.IsCurrentSceneGame())
                {
                    mi_checkChests = false;
                    break;
                }

                if (SceneStorages != null)
                {
                    System.DateTime now = System.DateTime.UtcNow;
                    var storages = SceneStorages.Where(_o => _o.IsUpgraded).OrderByDescending(_o => _o.Data.Priority);
                    foreach (var storage in storages)
                    {
                        yield return storage.AutoSorter.CheckItems();
                    }
                    DirtyInventories.Clear();
                    foreach (var storage in SceneStorages) storage.IsInventoryDirty = false;
                    mi_lastCheckDurationSeconds = (System.DateTime.UtcNow - now).TotalSeconds;
                }
                if (mi_lastCheckDurationSeconds < Config.CheckIntervalSeconds)
                {
                    yield return new WaitForSeconds(Config.CheckIntervalSeconds);
                }
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
            mi_configDialog.Load(itemAsset);

            Transform dialogRoot = mi_uiRoot.transform.Find("Dialog");
            Dialog = dialogRoot.gameObject.AddComponent<CUIDialog>();
            CUtil.LogD("Auto-sorter UI loaded!");
        }

        private void RegisterStorage(Storage_Small _storage)
        {
            if (_storage == null) return;

            if (SceneStorages == null)
            {
                SceneStorages = new List<CSceneStorage>();
            }

            if (SceneStorages.Any(_o => _o.StorageComponent == _storage)) return;

            CUtil.LogD("Registering storage " + _storage.gameObject);

            var sceneStorage = new CSceneStorage();
            sceneStorage.StorageComponent   = _storage;
            sceneStorage.AutoSorter = _storage.gameObject.AddComponent<CStorageBehaviour>();
            sceneStorage.StorageComponent.networkedIDBehaviour = sceneStorage.AutoSorter;
            sceneStorage.AutoSorter.Load(this, sceneStorage, mi_configDialog);
            SceneStorages.Add(sceneStorage);
        }

        private void OnBlockCreated(Storage_Small _storage)
        {
            if (Get == null) return; //mod is being unloaded

            if (_storage == null) return;

            Get.RegisterStorage(_storage);
        }
        
        internal void UnregisterStorage(CSceneStorage _storage)
        {
            if (Get == null) return; //mod is being unloaded

            if (SceneStorages?.Contains(_storage) ?? false)
            {
                SceneStorages.Remove(_storage);
            }
        }

        private Message CreateCarrierDTO(CDTO _object)
        {
            if (_object.Info != null)
            {
                _object.Info.OnBeforeSerialize();
            }
            return new Message_InitiateConnection(
                                    (Messages)_object.Type,
                                    0,
                                    JsonConvert.SerializeObject(_object));
        }

        private void SetInventoryDirty(Inventory _inventory)
        {
            if (_inventory is PlayerInventory) return;
            if (DirtyInventories.Contains(_inventory)) return;
            var storageForInventory = SceneStorages?.FirstOrDefault(_o => _o.AutoSorter.Inventory == _inventory);
            if (storageForInventory == null) return;
            DirtyInventories.Add(_inventory);
            storageForInventory.IsInventoryDirty = true;
            CUtil.LogD("Inventory for storage " + storageForInventory.AutoSorter.name + " is marked as dirty.");
        }
        
        #region PATCHES
        [HarmonyPatch(typeof(NetworkUpdateManager), "Deserialize")]
        public class CHarmonyPatch_NetworkUpdateManager_Deserialize
        {
            [HarmonyPrefix]
            private static bool NetworkUpdateManager_Deserialize(Packet_Multiple packet, CSteamID remoteID) 
            {
                List<Message> resultMessages    = packet.messages.ToList();
                List<Message> messages          = packet.messages.ToList();

                foreach (Message package in messages)
                {
                    if (package.t > mi_modMessagesCeil || package.t < mi_modMessagesFloor)
                    {
                        continue; //this is a message type not from this mod, ignore this package.
                    }

                    var inventoryUpdate = package as Message_Storage_Close;
                    var msg = package as Message_InitiateConnection;
                    if (msg == null && inventoryUpdate == null)
                    {
                        CUtil.LogW("Invalid auto-sorter mod message received. Make sure all connected players use the same mod version.");
                        continue;
                    }

                    resultMessages.Remove(package);
                    
                    try
                    {
                        if(inventoryUpdate != null)
                        {
                            if (!mi_registeredNetworkBehaviours.ContainsKey(inventoryUpdate.storageObjectIndex))
                            {
                                CUtil.LogW("No receiver with ID " + inventoryUpdate.storageObjectIndex + " found.");
                                continue;
                            }

                            mi_registeredNetworkBehaviours[inventoryUpdate.storageObjectIndex].OnInventoryUpdateReceived(inventoryUpdate);
                            continue;
                        }

                        CDTO modMessage = JsonConvert.DeserializeObject<CDTO>(msg.password);
                        if (modMessage == null)
                        {
                            CUtil.LogW("Invalid network message received. Update the AutoSorter mod or make sure all connected players use the same version.");
                            continue;
                        }

                        if (!mi_registeredNetworkBehaviours.ContainsKey(modMessage.ObjectIndex))
                        {
                            CUtil.LogW("No receiver with ID " + modMessage.ObjectIndex + " found.");
                            continue;
                        }

                        if(modMessage.Info != null)
                        {
                            modMessage.Info.OnAfterDeserialize();
                        }

                        CUtil.LogD($"Received {modMessage.Type}({package.t}) message from \"{remoteID}\".");
                        mi_registeredNetworkBehaviours[modMessage.ObjectIndex].OnNetworkMessageReceived(modMessage, remoteID);
                    }
                    catch(System.Exception)
                    {
                        CUtil.LogW("Failed to read mod network message. You or one of your fellow players might have to update the mod.");
                    }
                }

                if (resultMessages.Count == 0) return false; //no packages left, nothing todo. Dont even call the vanilla method

                //we remove all custom messages from the provided package and reassign the modified list so it is passed to the vanilla method.
                //this is to make sure we dont lose any vanilla packages
                packet.messages = resultMessages.ToArray();
                return true; //nothing for the mod left to do here, let the vanilla behaviour take over
            }
        }

        [HarmonyPatch(typeof(BlockCreator), "CreateBlock")]
        private class CHarmonyPatch_BlockCreator_CreateBlock
        {
            //Intercept create block method so we can check each created block and register it if it is a storage
            [HarmonyPostfix]
            private static void BlockCreator_CreateBlock(BlockCreator __instance, Block __result) => Get.OnBlockCreated(__result as Storage_Small);
        }

        [HarmonyPatch(typeof(Storage_Small), "Open")]
        private class CHarmonyPatch_Storage_Small_Open
        {
            private static Coroutine mi_routine;

            [HarmonyPostfix]
            private static void Storage_Small_Open(Storage_Small __instance, Network_Player player)
            {
                if (player == null || !player.IsLocalPlayer) return;

                var storage = Get.SceneStorages.FirstOrDefault(_o => _o.StorageComponent.ObjectIndex == __instance.ObjectIndex);
                if (storage == null)
                {
                    CUtil.LogW("Failed to find matching storage on storage open. This is a bug and should be reported.");
                    return;
                }

                if(Get.mi_configDialog == null) //If the UI is not fully loaded directly after entering the scene, make sure we wait until its available before showing it to the user.
                {
                    if(mi_routine != null)
                    {
                        Get.StopCoroutine(mi_routine);
                    }
                    mi_routine = Get.StartCoroutine(WaitAndShowUI(storage));
                    return;
                }

                Get.mi_configDialog.Show(storage);
            }

            private static IEnumerator WaitAndShowUI(CSceneStorage _storage)
            {
                while (Get.mi_configDialog == null) yield return new WaitForEndOfFrame();
                if (!_storage.StorageComponent.IsOpen) yield break;
                Get.mi_configDialog.Show(_storage);
            }
        }

        [HarmonyPatch(typeof(Storage_Small), "Close")]
        private class CHarmonyPatch_Storage_Small_Close
        {
            [HarmonyPostfix]
            private static void Storage_Small_Close(Storage_Small __instance, Network_Player player)
            {
                if (player == null || !player.IsLocalPlayer) return;

                Get.mi_configDialog?.Hide();

                var storage = Get.SceneStorages.FirstOrDefault(_o => _o.StorageComponent.ObjectIndex == __instance.ObjectIndex);
                if (storage == null)
                {
                    CUtil.LogW("Failed to find matching storage on storage close. This is a bug and should be reported.");
                    return;
                }

                Get.Broadcast(new CDTO(EStorageRequestType.STORAGE_DATA_UPDATE, storage.AutoSorter.ObjectIndex) { Info = storage.Data });
            }
        }

        [HarmonyPatch(typeof(RemovePlaceables), "PickupBlock")]
        private class CHarmonyPatch_RemovePlaceables_PickupBlock
        {
            [HarmonyPrefix]
            private static void PickupBlock(RemovePlaceables __instance, Block block)
            {
                if (!(block is Storage_Small)) return;
                var storage = Get.SceneStorages.FirstOrDefault(_o => _o.StorageComponent.ObjectIndex == block.ObjectIndex);
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
        
        [HarmonyPatch(typeof(Inventory), "AddItem", typeof(string), typeof(int))]
        private class CHarmonyPatch_Inventory_AddItem_1
        {
            [HarmonyPrefix]
            private static void AddItem(Inventory __instance, string uniqueItemName, int amount) => Get.SetInventoryDirty(__instance);
        }

        [HarmonyPatch(typeof(Inventory), "AddItem", typeof(string), typeof(Slot), typeof(int))]
        private class CHarmonyPatch_Inventory_AddItem_2
        {
            [HarmonyPrefix]
            private static void AddItem(Inventory __instance, string uniqueItemName, Slot slot, int amount) => Get.SetInventoryDirty(__instance);
        }

        [HarmonyPatch(typeof(Inventory), "AddItem", typeof(ItemInstance), typeof(bool))]
        private class CHarmonyPatch_Inventory_AddItem_3
        {
            [HarmonyPrefix]
            private static void AddItem(Inventory __instance, ItemInstance itemInstance, bool dropIfFull = true) => Get.SetInventoryDirty(__instance);
        }
        
        [HarmonyPatch(typeof(Inventory), "MoveItem")]
        private class CHarmonyPatch_Inventory_MoveItem
        {
            [HarmonyPrefix]
            private static void MoveItem(Inventory __instance, Slot slot, UnityEngine.EventSystems.PointerEventData eventData)
            {
                if (slot == null || slot.IsEmpty || __instance.secondInventory == null) return; //if items are moved within the player inventory, ignore.

                Slot movedToSlot    = Traverse.Create<Inventory>().Field("toSlot").GetValue<Slot>();
                Inventory movedTo   = movedToSlot != null ? Traverse.Create(movedToSlot).Field("inventory").GetValue<Inventory>() : null;
                if (movedTo == null || movedTo == __instance) return; //if items are moved within the same inventory, ignore.
                Get.SetInventoryDirty(movedTo);
            }
        }

        [HarmonyPatch(typeof(Inventory), "SetSlotsFromRGD")]
        private class CHarmonyPatch_Inventory_SetSlotsFromRGD
        {
            [HarmonyPrefix]
            private static void SetSlotsFromRGD(Inventory __instance, RGD_Slot[] slots) => Get.SetInventoryDirty(__instance);
        }
        #endregion
    }
}