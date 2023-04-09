﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AutoSorter.Manager;
using AutoSorter.Wrappers;
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
    public class CStorageBehaviour : MonoBehaviour_ID_Network, ISorterBehaviour
    {
        public INetworkPlayer LocalPlayer => mi_localPlayer;
        public ISceneStorage SceneStorage => mi_sceneStorage;
        public IInventory Inventory => mi_inventory;

        private bool HasInventorySpaceLeft => mi_inventory?.AllSlots.Any(_o => _o.Active && !_o.Locked && !_o.StackIsFull) ?? false;

        private IAutoSorter mi_mod;
        private IStorageManager mi_storageManager;
        private IRaftNetwork mi_network;
        private INetworkPlayer mi_localPlayer;
        private IItemManager mi_itemManager;
        private IASLogger mi_logger;
        private IASNetwork mi_asNetwork;
        private CConfigManager mi_configManager;

        private ISceneStorage mi_sceneStorage;
        private IInventory mi_inventory;

        private bool mi_customTexCreated;
        private bool mi_loaded = false;

        private Texture2D mi_originalTexture;
        private Texture2D mi_customTexture;

        public void LoadDependencies(   IASLogger _logger,
                                        IAutoSorter _mod,
                                        IStorageManager _storageManager,
                                        IItemManager _itemManager,
                                        IRaftNetwork _network,
                                        IASNetwork _asNetwork,
                                        CConfigManager _configManager)
        {
            mi_mod = _mod;
            mi_logger = _logger;
            mi_storageManager = _storageManager;
            mi_itemManager = _itemManager;
            mi_network = _network;
            mi_asNetwork = _asNetwork;
            mi_configManager = _configManager;
        }

        /// <summary>
        /// Initializes the auto-sorter storage behaviour providing a mod, scene storage and config UI handle.
        /// </summary>
        /// <param name="_mod">A handle to the mod object initializing this storage behaviour.</param>
        /// <param name="_storage">Reference to the scene storage which contains this storage behaviour.</param>
        /// <param name="_configDialog">Reference to the auto-sorter UI.</param>
        public void LoadStorage(ISceneStorage _storage)
        {
            mi_sceneStorage         = _storage;
            mi_inventory            = mi_sceneStorage.StorageComponent.GetInventoryReference();
            mi_localPlayer          = mi_network.GetLocalPlayer();
            //use our block objects index so we receive RPC calls
            //need to use an existing block index as clients/host need to be aware of it
            ObjectIndex             = mi_sceneStorage.StorageComponent.ObjectIndex;

            mi_asNetwork.RegisterNetworkBehaviour(this);

            if (!mi_network.IsHost)
            {
                mi_asNetwork.SendTo(new CDTO(EStorageRequestType.REQUEST_STATE, ObjectIndex), mi_network.HostID);
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
                int totalItemsTransfered;
                IInventory targetInventory;
                List<int> toCheck = mi_sceneStorage.Data.AutoMode ? 
                                        mi_inventory.AllSlots
                                            .Where(_o => _o.Active && !_o.IsEmpty && !_o.Locked)
                                            .Select(_o => _o.ItemInstance.UniqueIndex)
                                            .Distinct()
                                            .ToList() : 
                                        mi_sceneStorage.Data.Filters.Select(_o => _o.Key).ToList();

                foreach (var storage in mi_storageManager.SceneStorages.Values)
                {
                    if ((storage.AdditionalData != null && storage.AdditionalData.Ignore) ||
                        storage == mi_sceneStorage ||
                        storage.IsInventoryDirty) continue; //if the inventory has been altered, wait for the next cycle to transfer items from it so there are no issues with priority.

                    totalItemsTransfered = 0;
                    targetInventory = storage.StorageComponent.GetInventoryReference();

                    foreach (ISlot slot in targetInventory.AllSlots.Reverse())
                    {
                        if (!slot.Active ||
                            !slot.HasValidItemInstance ||
                            slot.Locked) continue;

                        if (!toCheck.Contains(slot.ItemInstance.UniqueIndex)) continue;
                        if (targetInventory == null)
                        {
                            mi_logger.LogW("Lost reference to target inventory during item check. Aborting check.");
                            break;
                        }

                        var transferResult = TransferItemsFromInventory(storage, targetInventory, slot, out int itemsTransferred, mi_sceneStorage.Data.AutoMode ? null : mi_sceneStorage.Data.Filters[slot.ItemInstance.UniqueIndex]);
                        totalItemsTransfered += itemsTransferred;
                        if (transferResult == null) break;
                        if (!(bool)transferResult) continue;
                        yield return new WaitForEndOfFrame();
                    }

                    if(totalItemsTransfered > 0)
                    {
                        mi_asNetwork.BroadcastInventoryState(storage.AutoSorter);
                        mi_asNetwork.BroadcastInventoryState(this);
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
                mi_logger.LogD("Received network message but storage is not fully loaded. Dropping message...");
                return;
            }

            switch (_msg.Type)
            {
                case EStorageRequestType.REQUEST_STATE:  //a client block requested this blocks state, send it back
                    if (Raft_Network.IsHost)
                    {
                        if (!mi_sceneStorage.IsUpgraded && mi_sceneStorage.AdditionalData == null) return;

                        mi_asNetwork.SendTo(new CDTO(EStorageRequestType.RESPOND_STATE, ObjectIndex) { Info = mi_sceneStorage.Data, AdditionalInfo = mi_sceneStorage.AdditionalData }, _remoteID);
                    }
                    break;
                case EStorageRequestType.RESPOND_STATE:
                    mi_sceneStorage.Data = _msg.Info;
                    mi_sceneStorage.AdditionalData = _msg.AdditionalInfo;
                    if (mi_sceneStorage.IsUpgraded) //only update the materials if the storage has been upgraded as this message is for initial upgrades on world load only
                    {
                        UpdateStorageMaterials();
                    }
                    return;
                case EStorageRequestType.UPGRADE:
                    mi_sceneStorage.Data = _msg.Upgrade ? new CSorterStorageData(_msg.ObjectIndex) : null;
                    UpdateStorageMaterials();
                    return;
                case EStorageRequestType.STORAGE_DATA_UPDATE:
                    mi_sceneStorage.Data = _msg.Info; //might be null if the storage has not been upgraded yet
                    if(Raft_Network.IsHost && mi_sceneStorage.IsUpgraded)
                    {
                        mi_sceneStorage.Data.SaveName = SaveAndLoad.CurrentGameFileName; //make sure we set the save name again on the hosts side as the clients do not know about the save name
                    }
                    return;
                case EStorageRequestType.STORAGE_IGNORE_UPDATE:
                    mi_sceneStorage.AdditionalData = _msg.AdditionalInfo; //might be null if a storage is un-ignored
                    if (Raft_Network.IsHost && mi_sceneStorage.AdditionalData != null)
                    {
                        mi_sceneStorage.AdditionalData.SaveName = SaveAndLoad.CurrentGameFileName; //make sure we set the save name again on the hosts side as the clients do not know about the save name
                    }
                    break;
                case EStorageRequestType.STORAGE_DIRTY:
                    mi_sceneStorage.IsInventoryDirty = true;
                    mi_logger.LogD($"Inventory for storage \"{mi_sceneStorage.ObjectName}\" was marked as dirty by client.");
                    return;
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
            mi_logger.LogD($"Inventory update received for {_netMessage.storageObjectIndex}");
            mi_inventory.SetSlotsFromRGD(_netMessage.slots.Wrap());
        }

        /// <summary>
        /// Upgrades this storage to an auto-sorter, removes upgrade costs from the players inventory and changes the storage model's material.
        /// </summary>
        /// <returns>True if the upgrade was successful, false if the player does not have enough resources to upgrade.</returns>
        public bool Upgrade()
        {
            if (!GameModeValueManager.GetCurrentGameModeValue().playerSpecificVariables.unlimitedResources &&
                mi_configManager.Config.UpgradeCosts.Any(_o => _o.Item != null && mi_localPlayer.Inventory.GetItemCount(_o.Item) < _o.Amount))
            {
                var notif = ComponentManager<HNotify>.Value.AddNotification(
                    HNotify.NotificationType.normal, 
                    "You don't have enough resources to upgrade!", 
                    5);
                notif.Show();
                return false;
            }

            foreach (var cost in mi_configManager.Config.UpgradeCosts)
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
            mi_mod.ReimburseConstructionCosts(new CItemManagerWrapper(), mi_localPlayer, true);
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

            mi_asNetwork.UnregisterNetworkBehaviour(this);

            if (mi_customTexture)
            {
                Destroy(mi_customTexture);
            }

            mi_sceneStorage.Data = null;
            UpdateStorageMaterials();
            NetworkIDManager.RemoveNetworkID(this);
            mi_storageManager.UnregisterStorage(mi_sceneStorage);
            mi_loaded = false;
        }
        
        private bool? TransferItemsFromInventory(ISceneStorage _targetStorage, IInventory _targetInventory, ISlot _slot, out int _itemsTransfered, CItemFilter _filter = null)
        {
            int targetItemCount = _slot.ItemInstance.Amount;
            _itemsTransfered = 0;

            if (_filter != null && !_filter.NoAmountControl)
            {
                targetItemCount = Mathf.Min(Mathf.Max(_filter.MaxAmount - mi_inventory.GetItemCount(_slot.ItemInstance.UniqueName), 0), targetItemCount);
            }

            if (targetItemCount <= 0) return true;

            if (!HasInventorySpaceLeft) return null;

            if (!CheckIsEligibleToTransfer(_slot.ItemInstance, _targetStorage, out int _allowedTransfer)) return true;
            if (_allowedTransfer > 0) targetItemCount = _allowedTransfer;

            if (!CUtil.HasSpaceLeftForItem(mi_inventory, _slot.ItemInstance.UniqueName)) return true;

            var instance = _slot.ItemInstance.Clone(); 
            if (targetItemCount < instance.Amount) //if not all items are transferred we set the uses to max so we do not lose them. If all items are transferred we keep the usage as specified in the item instance.
            {
                instance.SetUsesToMax();
            }
            targetItemCount = Mathf.Min(targetItemCount, instance.Amount);
            instance.Amount = targetItemCount; //if target item count is larger than the actual amount in this slot, use the amount in slot otherwise the target count.

            mi_inventory.AddItem(instance, false);
            _itemsTransfered = targetItemCount - instance.Amount; //determine how many items have been actually transferred

            mi_logger.LogD($"Trying to add {targetItemCount} ({_itemsTransfered} actual) {_slot.ItemInstance.UniqueName} to {mi_inventory.Name} from {_targetInventory.Name}");

            _slot.ItemInstance.Amount -= _itemsTransfered; //remove the actually transferred items from the source slot
            if (_slot.ItemInstance.Amount <= 0)
            {
                _slot.SetItem(null); //delete source slot item if amount is 0
            }
            else
            {
                _slot.RefreshComponents(); //if items remained after transfer, refresh slot UI
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
                        mat.color = mi_sceneStorage.IsUpgraded && mi_configManager.Config.ChangeStorageColorOnUpgrade ? Color.red : Color.white;
                    }
                    else if (mat.HasProperty("_Diffuse"))
                    {
                        if (mi_sceneStorage.IsUpgraded && mi_configManager.Config.ChangeStorageColorOnUpgrade)
                        {
                            if (mi_customTexture == null) //pretty heavy operation to make chests red that use the Vegetation shader. There is no way (afaik) to change the main color.
                            {
                                mi_originalTexture = mat.GetTexture("_Diffuse") as Texture2D;
                                if(mi_originalTexture == null)
                                {
                                    mi_logger.LogW($"Material had a property _Diffuse, but it was not a texture property on \"{mi_sceneStorage.ObjectName}\". This could be caused by a mod incompatibility. Make sure you report this issue.");
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
            mi_asNetwork.Broadcast(new CDTO(EStorageRequestType.UPGRADE, ObjectIndex) { Upgrade = _isUpgraded }); ;
        }
    
        private bool CheckIsEligibleToTransfer(IItemInstance _item, ISceneStorage _storage, out int _itemRestriction) //make sure if we are transferring between auto-sorters we use priorities to prevent item loops.
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

        public void DestroyImmediate() => DestroyImmediate(this);
    }
}
