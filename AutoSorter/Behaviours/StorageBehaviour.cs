﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;
using HarmonyLib;
using pp.RaftMods.AutoSorter.Protocol;
using Steamworks;
using UnityEngine;

namespace pp.RaftMods.AutoSorter
{
    /// <summary>
    /// Behaviour that is added to each storage in game to take care of the auto-sorter functionality.
    /// Handles upgrades/downgrades and item transfers as well as any other player interaction with the storage.
    /// </summary>
    [DisallowMultipleComponent] //disallow to really make sure we never get into the situation of components being added twice on mod reload.
    public class CStorageBehaviour : MonoBehaviour_ID_Network
    {
        private const int CREATIVE_INFINITE_COUNT = 2147483647;

        public Network_Player LocalPlayer => mi_localPlayer;
        public CSceneStorage SceneStorage => mi_sceneStorage;
        public Inventory Inventory => mi_inventory;

        private bool HasInventorySpaceLeft => mi_inventory?.allSlots.Any(_o => _o.active && !_o.locked && !_o.StackIsFull()) ?? false;

        private CAutoSorter mi_mod;
        private Raft_Network mi_network;
        private Network_Player mi_localPlayer;

        private CSceneStorage mi_sceneStorage;

        private Inventory mi_inventory;

        private bool mi_customTexCreated;
        private bool mi_loaded = false;

        private Texture2D mi_originalTexture;
        private Texture2D mi_customTexture;

        private CUISorterConfigDialog mi_configWindow;

        /// <summary>
        /// Initializes the auto-sorter storage behaviour providing a mod, scene storage and config UI handle.
        /// </summary>
        /// <param name="_mod">A handle to the mod object initializing this storage behaviour.</param>
        /// <param name="_storage">Reference to the scene storage which contains this storage behaviour.</param>
        /// <param name="_configDialog">Reference to the auto-sorter UI.</param>
        public void Load(CAutoSorter _mod, CSceneStorage _storage, CUISorterConfigDialog _configDialog)
        {
            mi_mod                  = _mod;
            mi_sceneStorage         = _storage;
            mi_configWindow         = _configDialog;
            mi_inventory            = mi_sceneStorage.StorageComponent.GetInventoryReference();
            mi_network              = ComponentManager<Raft_Network>.Value;
            mi_localPlayer          = mi_network.GetLocalPlayer();
            //use our block objects index so we receive RPC calls
            //need to use an existing block index as clients/host need to be aware of it
            ObjectIndex             = mi_sceneStorage.StorageComponent.ObjectIndex;
            CAutoSorter.Get.RegisterNetworkBehaviour(this);

            if (!Raft_Network.IsHost)
            {
                CAutoSorter.Get.SendTo(new CDTO(EStorageRequestType.REQUEST_STATE, ObjectIndex), mi_network.HostID);
            }
            else
            {
                if (mi_mod.SavedSorterStorageData.ContainsKey(SaveAndLoad.CurrentGameFileName))
                {
                    var data = mi_mod.SavedSorterStorageData[SaveAndLoad.CurrentGameFileName].FirstOrDefault(_o => _o.ObjectID == ObjectIndex);
                    if (data != null)
                    {
                        mi_sceneStorage.Data = data;
                        UpdateStorageMaterials();
                    }
                }
                if (mi_mod.SavedAdditionaStorageData.ContainsKey(SaveAndLoad.CurrentGameFileName))
                {
                    var additionalData = mi_mod.SavedAdditionaStorageData[SaveAndLoad.CurrentGameFileName].FirstOrDefault(_o => _o.ObjectID == ObjectIndex);
                    if (additionalData != null)
                    {
                        mi_sceneStorage.AdditionalData = additionalData;
                    }
                }
            }
            mi_loaded = true;
        }

        /// <summary>
        /// Main method taking care of transferring items to this auto sorter.
        /// </summary>
        /// <returns>IEnumerator reference to be used in the unity coroutine which runs storage checks.</returns>
        public IEnumerator CheckItems()
        {
            if (mi_sceneStorage.IsUpgraded && HasInventorySpaceLeft)
            {
                int itemsTransfered;
                int actuallyAdded;
                Inventory targetInventory;
                int targetItemCount;
                List<int> alreadyChecked;
                foreach (var storage in mi_mod.SceneStorages)
                {
                    if ((storage.AdditionalData != null && storage.AdditionalData.Ignore) ||
                        storage == mi_sceneStorage ||
                        storage.IsInventoryDirty) continue; //if the inventory has been altered, wait for the next cycle to transfer items from it so there are no issues with priority.

                    itemsTransfered = 0;
                    alreadyChecked  = new List<int>();
                    targetInventory = storage.StorageComponent.GetInventoryReference();

                    if (mi_sceneStorage.Data.AutoMode)
                    {
                        foreach (Slot slot in mi_inventory.allSlots)
                        {
                            if (!slot.active ||
                                !slot.HasValidItemInstance() ||
                                slot.locked) continue;

                            if (alreadyChecked.Contains(slot.itemInstance.UniqueIndex)) continue;

                            alreadyChecked.Add(slot.itemInstance.UniqueIndex);

                            targetItemCount = targetInventory.GetItemCount(slot.itemInstance.UniqueName);
                            if (targetItemCount <= 0 || targetItemCount == CREATIVE_INFINITE_COUNT) continue;

                            if (!HasInventorySpaceLeft) break;

                            if (!CheckIsEligibleToTransfer(slot.itemInstance, storage, out int _allowedTransfer)) continue;
                            if (_allowedTransfer > 0) targetItemCount = _allowedTransfer;

                            actuallyAdded = CUtil.StackedAddInventory(mi_inventory, slot.itemInstance.UniqueName, targetItemCount);
                            CUtil.LogD($"Trying to add {targetItemCount} ({actuallyAdded} actual) {slot.itemInstance.UniqueName} to {mi_inventory.name} from {targetInventory.name}");
                            if(actuallyAdded > 0)
                            {
                                itemsTransfered += actuallyAdded;
                                targetInventory.RemoveItem(slot.itemInstance.UniqueName, actuallyAdded);
                            }
                            yield return new WaitForEndOfFrame();
                        }
                    }
                    else
                    {
                        foreach (Slot slot in targetInventory.allSlots)
                        {
                            if (!slot.active ||
                                !slot.HasValidItemInstance() ||
                                slot.locked) continue;

                            if (alreadyChecked.Contains(slot.itemInstance.UniqueIndex) ||
                                !mi_sceneStorage.Data.Filters.ContainsKey(slot.itemInstance.UniqueIndex)) continue;

                            alreadyChecked.Add(slot.itemInstance.UniqueIndex);

                            var itemFilter = mi_sceneStorage.Data.Filters[slot.itemInstance.UniqueIndex];
                            targetItemCount = targetInventory.GetItemCount(itemFilter.UniqueName);

                            if (!itemFilter.NoAmountControl)
                            {
                                targetItemCount = Mathf.Min(Mathf.Max(itemFilter.MaxAmount - mi_inventory.GetItemCount(itemFilter.UniqueName), 0), targetItemCount);
                            }

                            if (targetItemCount <= 0 || targetItemCount == CREATIVE_INFINITE_COUNT) continue;

                            if (!HasInventorySpaceLeft) break;

                            if (!CheckIsEligibleToTransfer(slot.itemInstance, storage, out int _allowedTransfer)) continue;
                            if (_allowedTransfer > 0) targetItemCount = _allowedTransfer;

                            actuallyAdded = CUtil.StackedAddInventory(mi_inventory, itemFilter.UniqueName, targetItemCount);
                            CUtil.LogD($"Trying to add {targetItemCount} ({actuallyAdded} actual) {slot.itemInstance.UniqueName} to {mi_inventory.name} from {targetInventory.name}");
                            if (actuallyAdded > 0)
                            {
                                itemsTransfered += actuallyAdded;
                                targetInventory.RemoveItem(itemFilter.UniqueName, actuallyAdded);
                            }

                            yield return new WaitForEndOfFrame();
                        }
                    }
                
                    if(itemsTransfered > 0)
                    {
                        CAutoSorter.Get.BroadcastInventoryState(storage.AutoSorter);
                        CAutoSorter.Get.BroadcastInventoryState(this);
                    }
                }
            }
        }

        /// <summary>
        /// Called whenever a mod network message is received in multiplayer and processes the auto-sorters reaction to network input.
        /// </summary>
        /// <param name="_msg">The DTO object sent along the network message.</param>
        /// <param name="_remoteID">The steam ID of the player that sent the network message.</param>
        public void OnNetworkMessageReceived(CDTO _msg, CSteamID _remoteID)
        {
            if (!mi_loaded)
            {
                CUtil.LogD("Received network message but storage is not fully loaded. Dropping message...");
                return;
            }
            Plant p = null;
            Traverse.Create(p).Method("ResetStats").GetValue();

            switch (_msg.Type)
            {
                case EStorageRequestType.REQUEST_STATE:  //a client block requested this blocks state, send it back
                    if (Raft_Network.IsHost)
                    {
                        if (!mi_sceneStorage.IsUpgraded && mi_sceneStorage.AdditionalData == null) return;

                        CAutoSorter.Get.SendTo(new CDTO(EStorageRequestType.RESPOND_STATE, ObjectIndex) { Info = mi_sceneStorage.Data, AdditionalInfo = mi_sceneStorage.AdditionalData }, _remoteID);
                    }
                    break;
                case EStorageRequestType.RESPOND_STATE:
                    mi_sceneStorage.Data = _msg.Info;
                    mi_sceneStorage.AdditionalData = _msg.AdditionalInfo;
                    if (_msg.Info != null)
                    {
                        UpdateStorageMaterials();
                    }
                    return;
                case EStorageRequestType.UPGRADE:
                    mi_sceneStorage.Data = _msg.Upgrade ? new CSorterStorageData(_msg.ObjectIndex) : null;
                    UpdateStorageMaterials();
                    return;
                case EStorageRequestType.STORAGE_DATA_UPDATE:
                    mi_sceneStorage.Data = _msg.Info;
                    return;
                case EStorageRequestType.STORAGE_IGNORE_UPDATE:
                    mi_sceneStorage.AdditionalData = _msg.AdditionalInfo;
                    break;
            }
        }

        /// <summary>
        /// Called whenever the special inventory update message sent is from any of the connected clients.
        /// The message makes sure that storage inventories is kept in sync for all players when auto-sorters transfer items.
        /// Usually this message is sent by Raft whenever a user closes a storage, the mod also sends it whenever the auto-sorter changes inventories and updates this storage's inventory.
        /// </summary>
        /// <param name="_netMessage">The <see cref="Message_Storage_Close"/> message sent by a clients auto-sorter on item transfer.</param>
        public void OnInventoryUpdateReceived(Message_Storage_Close _netMessage)
        {
            CUtil.LogD("Inventory update received for " + _netMessage.storageObjectIndex);
            mi_inventory.SetSlotsFromRGD(_netMessage.slots);
        }

        /// <summary>
        /// Upgrades this storage to an auto-sorter, removes upgrade costs from the players inventory and changes the storage model's material.
        /// </summary>
        /// <returns>True if the upgrade was successful, false if the player does not have enough resources to upgrade.</returns>
        public bool Upgrade()
        {
            if (CAutoSorter.Config.UpgradeCosts.Any(_o => mi_localPlayer.Inventory.GetItemCount(_o.Name) < _o.Amount))
            {
                var notif = ComponentManager<HNotify>.Value.AddNotification(
                    HNotify.NotificationType.normal, 
                    "You don't have enough resources to upgrade!", 
                    5);
                notif.Show();
                return false;
            }

            foreach (var cost in CAutoSorter.Config.UpgradeCosts)
            {
                mi_localPlayer.Inventory.RemoveItem(cost.Name, cost.Amount);
            }

            mi_sceneStorage.Data = new CSorterStorageData(ObjectIndex);
            UpdateStorageMaterials();
            SendUpgradeState(true);
            var soundRef = Traverse.Create(mi_sceneStorage.StorageComponent).Field("eventRef_open").GetValue<string>();
            if (!string.IsNullOrEmpty(soundRef))
            {
                RuntimeManager.PlayOneShot(soundRef, transform.position);
            }
            return true;
        }

        /// <summary>
        /// Downgrades this storage to a regular storage. Changing materials back and giving the user back his invested resources.
        /// </summary>
        public void Downgrade()
        {
            CUtil.ReimburseConstructionCosts(mi_localPlayer, true);
            mi_sceneStorage.Data = null;
            UpdateStorageMaterials();
            SendUpgradeState(false);

            var soundRef = Traverse.Create(mi_sceneStorage.StorageComponent).Field("eventRef_close").GetValue<string>();
            if (!string.IsNullOrEmpty(soundRef))
            {
                RuntimeManager.PlayOneShot(soundRef, transform.position);
            }
        } 

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (CAutoSorter.Get != null)
            {
                CAutoSorter.Get.UnregisterNetworkBehaviour(this);
            }

            if (mi_customTexture)
            {
                Destroy(mi_customTexture);
            }

            mi_sceneStorage.Data = null;
            UpdateStorageMaterials();
            NetworkIDManager.RemoveNetworkID(this);
            mi_mod.UnregisterStorage(mi_sceneStorage);
            mi_loaded = false;
        }
        
        private bool TransferItemsFromInventory(CSceneStorage _targetStorage, ItemInstance _item, out int _itemsTransfered, CItemFilter _filter = null)
        {
            Inventory inventory = _targetStorage.StorageComponent.GetInventoryReference();
            int targetItemCount = inventory.GetItemCount(_item.UniqueName);
            _itemsTransfered = 0;

            if (_filter != null && !_filter.NoAmountControl)
            {
                targetItemCount = Mathf.Min(Mathf.Max(_filter.MaxAmount - mi_inventory.GetItemCount(_item.UniqueName), 0), targetItemCount);
            }

            if (targetItemCount <= 0 || targetItemCount == CREATIVE_INFINITE_COUNT) return true;

            if (!HasInventorySpaceLeft) return false;

            if (!CheckIsEligibleToTransfer(_item, _targetStorage, out int _allowedTransfer)) return true;
            if (_allowedTransfer > 0) targetItemCount = _allowedTransfer;

            _itemsTransfered = CUtil.StackedAddInventory(mi_inventory, _item.UniqueName, targetItemCount);
            CUtil.LogD($"Trying to add {targetItemCount} ({_itemsTransfered} actual) {_item.UniqueName} to {mi_inventory.name} from {inventory.name}");
            if (_itemsTransfered > 0)
            {
                inventory.RemoveItem(_item.UniqueName, _itemsTransfered);
            }

            return true;
        }

        private void UpdateStorageMaterials()
        {
            var renderer = mi_sceneStorage.StorageComponent.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var rend in renderer)
            {
                var materials = rend.materials;
                foreach (var mat in materials)
                {
                    if (!mat) continue;
                    if (mat.HasProperty("_MainTex")) //probably std mat
                    {
                        mat.color = mi_sceneStorage.IsUpgraded && CAutoSorter.Config.ChangeStorageColorOnUpgrade ? Color.red : Color.white;
                    }
                    else if (mat.HasProperty("_Diffuse"))
                    {
                        if (mi_sceneStorage.IsUpgraded && CAutoSorter.Config.ChangeStorageColorOnUpgrade)
                        {
                            if (mi_customTexture == null) //pretty heavy operation to make chests red that use the Vegetation shader. There is no way (afaik) to change the main color.
                            {
                                mi_originalTexture = mat.GetTexture("_Diffuse") as Texture2D;
                                if(mi_originalTexture == null)
                                {
                                    CUtil.LogW("Material had a property _Diffuse, but it was not a texture property on \"" + mi_sceneStorage.StorageComponent.name + "\". This could be caused by a mod incompatibility. Make sure you report this issue.");
                                    continue;
                                }
                                mi_customTexture = CUtil.MakeReadable(mi_originalTexture, TextureFormat.ARGB32, true);
                                mi_customTexture.SetPixels(mi_customTexture.GetPixels().Select(_o => new Color(Mathf.Min(_o.r + 0.35f, 0.9f), _o.g - 0.1f, _o.b - 0.1f, _o.a)).ToArray());
                                mi_customTexture.Apply();
                            }
                            mi_customTexCreated = true;
                            mat.SetTexture("_Diffuse", mi_customTexture);
                        }
                        else if(mi_customTexCreated)
                        {
                            mat.SetTexture("_Diffuse", mi_originalTexture);
                        }
                    }
                }
                rend.materials = materials;
            }
        }



        private void SendUpgradeState(bool _isUpgraded)
        {
            CAutoSorter.Get.Broadcast(new CDTO(EStorageRequestType.UPGRADE, ObjectIndex) { Upgrade = _isUpgraded }); ;
        }
    
        private bool CheckIsEligibleToTransfer(ItemInstance _item, CSceneStorage _storage, out int _itemRestriction) //make sure if we are transferring between auto-sorters we use priorities to prevent item loops.
        {
            _itemRestriction = -1;
            if (!_storage.IsUpgraded) return true;
            if (_storage.Data.AutoMode)
            {
                if (_storage.Data.Priority < mi_sceneStorage.Data.Priority) return true; //if the other sorter is in auto mode we check if it contains the item we want to transfer. if it does and its priority is either higher or the same than ours, dont transfer items.
            }
            else if (!_storage.Data.Filters.ContainsKey(_item.UniqueIndex)) return true;
            else if (_storage.Data.Filters[_item.UniqueIndex].NoAmountControl)
            {
                if (_storage.Data.Priority < mi_sceneStorage.Data.Priority) return true;
            }
            else
            {
                if (_storage.Data.Priority < mi_sceneStorage.Data.Priority) return true;
                _itemRestriction = _storage.AutoSorter.Inventory.GetItemCount(_item.UniqueName) - _storage.Data.Filters[_item.UniqueIndex].MaxAmount;
                if (_itemRestriction > 0) return true;
            }
            return false;
        }
    }
}
