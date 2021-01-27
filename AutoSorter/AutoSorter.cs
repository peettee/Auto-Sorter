using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace pp.RaftMods.AutoSorter
{
    /// <summary>
    /// </summary>
    public class CAutoSorter : Mod
    {
        public static CAutoSorter Get = null;

        public const string VERSION     = "1.0.0";
        public const string MOD_NAME    = "AutoSorter";
        private const string MOD_NAMESPACE = "pp.RaftMods." + MOD_NAME;

        private const string ASSET_PAYLOAD_PATH = "assets/autosorter";

        private const string UI_CANVAS_ROOT = "Canvases/_CanvasGame_New";
        private const string UI_CONFIG_DIALOG_PREFAB_PATH = "Assets/Prefabs/UI/AutoSorter_UIRoot.prefab";
        private const string UI_CONFIG_DIALOG_ITEM_PREFAB_PATH = "Assets/Prefabs/UI/Windows/Config/Item_Entry.prefab";

        public Dictionary<string, CStorageData[]> SavedStorageData { get; private set; } = new Dictionary<string, CStorageData[]>();

        private string ModDataDirectory     => Path.Combine(Application.persistentDataPath, "Mods", MOD_NAME);
        private string ModConfigFilePath    => Path.Combine(ModDataDirectory, "config.json");
        private string ModDataFilePath      => Path.Combine(ModDataDirectory, "storagedata.json");

        public static CModConfig Config { get; private set; }
        public List<CSceneStorage> SceneStorages { get; private set; }
        public double LastCheckDurationMillis { get; private set; }
        public CUIDialog Dialog { get; private set; }
        public SoundManager Sounds { get; private set; }
        private Harmony mi_harmony;

        private Coroutine mi_chestRoutineHandle;
        private bool mi_checkChests;

        private AssetBundle mi_bundle;

        private GameObject mi_uiRoot;
        private CUISorterConfigDialog mi_configDialog;
       // private CUISorterInteractWindow mi_interactWindow;

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

            mi_harmony = new Harmony(MOD_NAMESPACE);
            mi_harmony.PatchAll(Assembly.GetExecutingAssembly());
            if (mi_bundle == null)
            {
                AssetBundleCreateRequest request = AssetBundle.LoadFromMemoryAsync(GetEmbeddedFileBytes(ASSET_PAYLOAD_PATH));
                yield return request;
                mi_bundle = request.assetBundle;
                if (!mi_bundle)
                {
                    CUtil.LogE("Failed to load embedded bundle.");
                    yield break;
                }
            }

            Sounds = ComponentManager<SoundManager>.Value;
            if (!Sounds)
            {
                CUtil.LogW("Failed to get sound manager on mod load.");
            }

            LoadConfig();
            LoadStorageData();

            if (RAPI.IsCurrentSceneGame())
            {
                LoadStorages();
                mi_checkChests = true;
                mi_chestRoutineHandle = StartCoroutine(CheckStorages());
            }

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

        private IEnumerator CheckStorages()
        {
            while (mi_checkChests)
            {
                if (!RAPI.IsCurrentSceneGame())
                {
                    mi_checkChests = false;
                    continue;
                }

                if (SceneStorages != null)
                {
                    System.DateTime now = System.DateTime.UtcNow;
                    var storages = SceneStorages.Where(_o => _o.IsUpgraded).OrderByDescending(_o => _o.Data.Priority);
                    foreach (var storage in storages)
                    {
                        yield return storage.AutoSorter.CheckItems();
                    }
                    LastCheckDurationMillis = (System.DateTime.UtcNow - now).TotalMilliseconds;
                }
                if (LastCheckDurationMillis < Config.CheckIntervalSeconds)
                {
                    yield return new WaitForSeconds(Config.CheckIntervalSeconds);
                }
            }
        }

        public override void WorldEvent_WorldLoaded()
        {
            base.WorldEvent_WorldLoaded();
            StartCoroutine(mi_configDialog.LoadItems());
            mi_checkChests = true;
            mi_chestRoutineHandle = StartCoroutine(CheckStorages());
        }

        public override void WorldEvent_WorldSaved()
        {
            base.WorldEvent_WorldSaved();
            SaveStorageData();
        }

        private void LoadUI()
        {
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
            
            //Transform interactRoot = mi_uiRoot.transform.Find("InteractWindow");
            //mi_interactWindow = interactRoot.gameObject.AddComponent<CUISorterInteractWindow>();

            Transform dialogRoot = mi_uiRoot.transform.Find("Dialog");
            Dialog = dialogRoot.gameObject.AddComponent<CUIDialog>();
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

                Config = JsonConvert.DeserializeObject<CModConfig>(File.ReadAllText(ModConfigFilePath)) ?? throw new System.Exception("Deserialisation failed.");
            }
            catch (System.Exception _e)
            {
                CUtil.LogW("Failed to load mod configuration: " + _e.Message + ". Check your configuration file.");
            }
        }

        private void SaveConfig()
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

        private void LoadStorages()
        {
            SceneStorages = new List<CSceneStorage>();

            var raftStorages = GameObject.FindObjectsOfType<Storage_Small>();
            CUtil.LogD("Loading " + raftStorages.Length + " storages into mod control.");
            foreach (var light in raftStorages)
            {
                RegisterStorage(light);
            }
        }

        private void LoadStorageData()
        {
            try
            {
                if (!File.Exists(ModDataFilePath)) return;

                CStorageData[] data = JsonConvert.DeserializeObject<CStorageData[]>(File.ReadAllText(ModDataFilePath)) ?? throw new System.Exception("Deserialisation failed.");
                SavedStorageData = data
                    .GroupBy(_o => _o.SaveName)
                    .Select(_o => new KeyValuePair<string, CStorageData[]>(_o.Key, _o.ToArray()))
                    .ToDictionary(_o => _o.Key, _o => _o.Value);
            }
            catch (System.Exception _e)
            {
                CUtil.LogW("Failed to load saved mod data: " + _e.Message + ". Storage data wont be loaded.");
                SavedStorageData = new Dictionary<string, CStorageData[]>();
            }
        }

        private void SaveStorageData()
        {
            try
            {
                if (File.Exists(ModDataFilePath))
                {
                    File.Delete(ModDataFilePath);
                }

                if (SavedStorageData.ContainsKey(SaveAndLoad.CurrentGameFileName))
                {
                    SavedStorageData.Remove(SaveAndLoad.CurrentGameFileName);
                }

                foreach(var storage in SceneStorages)
                {
                    storage.Data?.OnBeforeSerialize();
                }

                SavedStorageData.Add(
                    SaveAndLoad.CurrentGameFileName,
                    SceneStorages
                        .Where(_o => _o.AutoSorter && _o.IsUpgraded)
                        .Select(_o => _o.Data)
                        .ToArray());

                File.WriteAllText(
                    ModDataFilePath,
                    JsonConvert.SerializeObject(
                        SavedStorageData.SelectMany(_o => _o.Value).ToArray(),
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

        private void RegisterStorage(Storage_Small _storage)
        {
            if (_storage == null) return;

            if (SceneStorages == null)
            {
                SceneStorages = new List<CSceneStorage>();
            }

            if(mi_uiRoot == null)
            {
                LoadUI();
            }

            if (SceneStorages.Any(_o => _o.StorageComponent == _storage)) return;

            CUtil.LogW("Adding storage " + _storage.gameObject);

            var sceneStorage = new CSceneStorage();
            sceneStorage.StorageComponent   = _storage;
            sceneStorage.AutoSorter = _storage.gameObject.AddComponent<CStorageBehaviour>();
            sceneStorage.Raycastable = _storage.GetComponent<RaycastInteractable>();
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

        #region PATCHES
        [HarmonyPatch(typeof(BlockCreator), "CreateBlock")]
        private class CHarmonyPatch_BlockCreator_CreateBlock
        {
            //Intercept create block method so we can check each created block if it is a light
            [HarmonyPostfix]
            private static void BlockCreator_CreateBlock(BlockCreator __instance, Block __result) => Get.OnBlockCreated(__result as Storage_Small);
        }

        [HarmonyPatch(typeof(Storage_Small), "Open")]
        private class CHarmonyPatch_Storage_Small_Open
        {
            [HarmonyPostfix]
            private static void Storage_Small_Open(Storage_Small __instance, Network_Player player)
            {
                if (player == null || !player.IsLocalPlayer) return;

                var storage = Get.SceneStorages.FirstOrDefault(_o => _o.StorageComponent == __instance);
                if (storage == null)
                {
                    CUtil.LogW("Failed to find matching storage on storage open. This is a bug and should be reported.");
                    return;
                }
                Get.mi_configDialog.Show(storage);
            }
        }

        [HarmonyPatch(typeof(Storage_Small), "Close")]
        private class CHarmonyPatch_Storage_Small_Close
        {
            [HarmonyPostfix]
            private static void Storage_Small_Close(Storage_Small __instance, Network_Player player)
            {
                if (player == null || !player.IsLocalPlayer) return;

                Get.mi_configDialog.Hide();
            }
        }
        #endregion
    }
}