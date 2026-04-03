using Newtonsoft.Json;
using pp.RaftMods.AutoSorter;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AutoSorter.Manager
{
    public class CStorageManager
    {
        public bool HasStorages => SceneStorages.Count > 0;

        private string ModDataFilePath => Path.Combine(m_modDataDirectory, "storagedata.json");
        private string ModAdditionalDataFilePath => Path.Combine(m_modDataDirectory, "additional_storagedata.json");

        /// <summary>
        /// Storage data which is loaded and saved to disk to preserve auto-sorter configurations. 
        /// Combines the world name and the storage data for the world.
        /// </summary>
        public Dictionary<string, CSorterStorageData[]> SavedSorterStorageData { get; private set; } = new Dictionary<string, CSorterStorageData[]>();

        /// <summary>
        /// Additional storage data that is loaded and saved to disk to preserve certain settings for all storages (auto-sorter or not).
        /// </summary>
        public Dictionary<string, CGeneralStorageData[]> SavedAdditionaStorageData { get; private set; } = new Dictionary<string, CGeneralStorageData[]>();

        /// <summary>
        /// All currently loaded scene storages. Is loaded on world load.
        /// </summary>
        public Dictionary<uint, CSceneStorage> SceneStorages { get; private set; } = new Dictionary<uint, CSceneStorage>();

        private readonly Queue<CSceneStorage> mi_registerQueuedStorages = new Queue<CSceneStorage>();
        private readonly Queue<CSceneStorage> mi_unregisterQueuedStorages = new Queue<CSceneStorage>();
        private readonly string m_modDataDirectory;
        private readonly CNetwork mi_network;
        private bool mi_deferStorageRegister;

        public CStorageManager(string _modDataDirectory, CNetwork _network)
        {
            m_modDataDirectory = _modDataDirectory;
            mi_network = _network;
        }

        public IEnumerator UpdateStorages()
        {
            mi_deferStorageRegister = true;
            var storages = SceneStorages.Where(_o => _o.Value.IsUpgraded).OrderByDescending(_o => _o.Value.Data.Priority);
            foreach (var storage in storages)
            {
                yield return storage.Value.AutoSorter.CheckItems();
            }
            foreach (var storage in SceneStorages)
            {
                storage.Value.IsInventoryDirty = false;
            }
            mi_deferStorageRegister = false;
            FlushStorageQueues();
        }

        public void LoadStorageData()
        {
            LoadSorterStorageData();
            LoadAdditionalStorageData();
        }

        public void SaveStorageData()
        {
            SaveSorterStorageData();
            SaveAdditionalStorageData();
        }

        public void Cleanup()
        {
            //undo all changes to the scene
            foreach(var storage in SceneStorages.Values)
            {
                Component.DestroyImmediate(storage.AutoSorter);
            }
            SceneStorages.Clear();

            mi_registerQueuedStorages.Clear();
            mi_unregisterQueuedStorages.Clear();
        }

        public void RegisterStorage(Storage_Small _storage)
        {
            if (_storage == null) return;

            if (SceneStorages.ContainsKey(_storage.ObjectIndex))
            {
                CUtil.LogW($"Storage for index {_storage.ObjectIndex} has already been registered.");
                return;
            }

            var sceneStorage = new CSceneStorage();
            sceneStorage.StorageComponent = _storage;
            sceneStorage.AutoSorter = _storage.gameObject.AddComponent<CStorageBehaviour>();
            sceneStorage.StorageComponent.networkedIDBehaviour = sceneStorage.AutoSorter;
            sceneStorage.AutoSorter.Load(this, sceneStorage, mi_network);
            if (mi_deferStorageRegister)
            {
                mi_registerQueuedStorages.Enqueue(sceneStorage);
                CUtil.LogD("Enqueued storage \"" + _storage.gameObject.name + "\" for register as we are currently running checks. Queued storages for register: " + mi_registerQueuedStorages.Count);
                return;
            }
            SceneStorages.Add(_storage.ObjectIndex, sceneStorage);
            CUtil.LogD("Registered storage \"" + _storage.gameObject.name + "\" Total storages: " + SceneStorages.Count);
        }

        public void SetStorageInventoryDirty(Inventory _inventory)
        {
            if (!Raft_Network.IsHost) return;
            if (_inventory is PlayerInventory) return;
            var storageForInventory = SceneStorages.Values.FirstOrDefault(_o => _o.AutoSorter.Inventory == _inventory);
            if (storageForInventory == null) return;
            storageForInventory.IsInventoryDirty = true;
            CUtil.LogD($"Inventory for storage \"{storageForInventory.AutoSorter.name}\"({storageForInventory.ObjectIndex}) is marked as dirty.");
        }

        private void FlushStorageQueues()
        {
            if (mi_registerQueuedStorages.Count > 0)
            {
                CUtil.LogD("Registering " + mi_registerQueuedStorages.Count + " queued storages now.");
                while (mi_registerQueuedStorages.Count > 0)
                {
                    var storage = mi_registerQueuedStorages.Dequeue();
                    SceneStorages.Add(storage.ObjectIndex, storage);
                }
            }
            if (mi_unregisterQueuedStorages.Count > 0)
            {
                CUtil.LogD("Unregistering " + mi_unregisterQueuedStorages.Count + " queued storages now.");
                while (mi_unregisterQueuedStorages.Count > 0)
                {
                    UnregisterStorage(mi_unregisterQueuedStorages.Dequeue());
                }
            }
        }

        public CSceneStorage GetStorageByIndex(uint _objectIndex)
        {
            if (!SceneStorages.ContainsKey(_objectIndex))
            {
                CUtil.LogW($"Tried to get storage for index {_objectIndex} which could not be found.");
                return null;
            }
            return SceneStorages[_objectIndex];
        }

        public void UnregisterStorage(CSceneStorage _storage)
        {
            if (mi_deferStorageRegister)
            {
                mi_unregisterQueuedStorages.Enqueue(_storage);
                CUtil.LogD("Enqueued storage \"" + _storage.StorageComponent.gameObject.name + "\" for unregister as we are currently running checks. Queued storages for unregister: " + mi_unregisterQueuedStorages.Count);
                return;
            }

            SceneStorages.Remove(_storage.ObjectIndex);

            if (SceneStorages != null)
            {
                CUtil.LogD("Unregistered storage \"" + (!_storage.StorageComponent ? "UNKNOWN (storage was destroyed)" : _storage.StorageComponent.gameObject.name) + "\" Total storages: " + (SceneStorages?.Count.ToString() ?? "No storages"));
            }
        }

        private void LoadSorterStorageData()
        {
            try
            {
                if (!File.Exists(ModDataFilePath)) return;

                CSorterStorageData[] data = JsonConvert.DeserializeObject<CSorterStorageData[]>(File.ReadAllText(ModDataFilePath)) ?? throw new Exception("De-serialisation failed.");
                SavedSorterStorageData = data
                    .Where(_o => !string.IsNullOrEmpty(_o.SaveName))
                    .GroupBy(_o => _o.SaveName)
                    .ToDictionary(_o => _o.Key, _o => _o.ToArray());

                foreach (var allStorageData in SavedSorterStorageData)
                {
                    foreach (var storageData in allStorageData.Value)
                    {
                        storageData.OnAfterDeserialize();
                    }
                }
            }
            catch (Exception _e)
            {
                CUtil.LogW("Failed to load saved mod data: " + _e.Message + ". Storage data wont be loaded.");
                CUtil.LogD(_e.StackTrace);
                SavedSorterStorageData = new Dictionary<string, CSorterStorageData[]>();
            }
        }

        private void SaveSorterStorageData()
        {
            try
            {
                if (SceneStorages == null || SceneStorages.Count == 0) return;

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
                    storage.Value.Data?.OnBeforeSerialize();
                }

                SavedSorterStorageData.Add(
                    SaveAndLoad.CurrentGameFileName,
                    SceneStorages
                        .Where(_o => _o.Value.AutoSorter && _o.Value.IsUpgraded)
                        .Select(_o => _o.Value.Data)
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
                CUtil.LogD(_e.StackTrace);
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
                CUtil.LogD(_e.StackTrace);
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
                        .Where(_o => _o.Value.AdditionalData != null)
                        .Select(_o => _o.Value.AdditionalData)
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
                CUtil.LogD(_e.StackTrace);
            }
        }
    }
}
