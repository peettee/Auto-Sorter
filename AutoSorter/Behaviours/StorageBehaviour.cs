using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;
using HarmonyLib;
using pp.RaftMods.AutoSorter.Protocol;
using Steamworks;
using UnityEngine;

namespace pp.RaftMods.AutoSorter
{
    [DisallowMultipleComponent] //disallow to really make sure we never get into the situation of components being added twice on mod reload.
    public class CStorageBehaviour : MonoBehaviour_ID_Network
    {
        private const int CREATIVE_INFINITE_COUNT = 2147483647;
        public const float DOWNGRADE_TIME_SECONDS = 3f;

        public bool HasInventorySpaceLeft => mi_inventory?.allSlots.Any(_o => _o.active && !_o.locked && !_o.StackIsFull()) ?? false;
        public bool AutoMode { get; private set; } = true;
        public System.DateTime? LastCheck { get; private set; }

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
        /// Initializes the lantern switch behaviour providing a mod handle and a scene light wrapper.
        /// </summary>
        /// <param name="_mod">A handle to the mod object initializing this switch.</param>
        /// <param name="_storage">Contains references to the block and other components.</param>
        public void Load(CAutoSorter _mod, CSceneStorage _storage, CUISorterConfigDialog _configDialog)
        {
            mi_mod                  = _mod;
            mi_sceneStorage         = _storage;
            mi_configWindow         = _configDialog;
            mi_inventory            = mi_sceneStorage.StorageComponent.GetInventoryReference();
            mi_network              = ComponentManager<Raft_Network>.Value;
            mi_localPlayer          = mi_network.GetLocalPlayer();
            //use our block objects index so we receive RPC calls
            //need to use an existing blockindex as clients/host need to be aware of it
            ObjectIndex             = mi_sceneStorage.StorageComponent.ObjectIndex;
            CAutoSorter.Get.RegisterNetworkBehaviour(this);

            if (!Raft_Network.IsHost)
            {
                CAutoSorter.Get.SendTo(new CDTO(EStorageRequestType.REQUEST_STATE, ObjectIndex), mi_network.HostID);
            }
            else if (mi_mod.SavedStorageData.ContainsKey(SaveAndLoad.CurrentGameFileName))
            {
                var data = mi_mod.SavedStorageData[SaveAndLoad.CurrentGameFileName].FirstOrDefault(_o => _o.ObjectID == ObjectIndex);
                if (data != null)
                {
                    mi_sceneStorage.Data = data;
                    UpdateStorageMaterials();
                }
            }

            mi_loaded = true;
        }

        public IEnumerator CheckItems()
        {
            if (mi_sceneStorage.IsUpgraded && HasInventorySpaceLeft)
            {
                LastCheck = System.DateTime.UtcNow;

                Inventory targetInventory;
                int targetItemCount;
                List<int> alreadyChecked;
                foreach (var storage in mi_mod.SceneStorages)
                {
                    if (storage.IsUpgraded && (!CAutoSorter.Config.TransferFromAutosorters || storage == mi_sceneStorage)) continue;

                    alreadyChecked = new List<int>();
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

                            targetInventory.RemoveItem(slot.itemInstance.UniqueName, targetItemCount);
                            CUtil.StackedAddInventory(mi_inventory, slot.itemInstance.UniqueName, targetItemCount);
                            yield return new WaitForEndOfFrame();
                        }
                        continue;
                    }
                     
                    foreach (Slot slot in targetInventory.allSlots)
                    {
                        if (!slot.active ||
                            !slot.HasValidItemInstance() ||
                            slot.locked) continue;

                        if (alreadyChecked.Contains(slot.itemInstance.UniqueIndex) ||
                            !mi_sceneStorage.Data.Filters.ContainsKey(slot.itemInstance.UniqueIndex)) continue;

                        alreadyChecked.Add(slot.itemInstance.UniqueIndex);

                        var itemIdx = mi_sceneStorage.Data.Filters[slot.itemInstance.UniqueIndex];
                        targetItemCount = targetInventory.GetItemCount(itemIdx.UniqueName);

                        if (!itemIdx.NoAmountControl)
                        {
                            targetItemCount = Mathf.Min(Mathf.Max(itemIdx.MaxAmount - mi_inventory.GetItemCount(itemIdx.UniqueName), 0), targetItemCount);
                        }

                        if (targetItemCount <= 0 || targetItemCount == CREATIVE_INFINITE_COUNT) continue;

                        if (!HasInventorySpaceLeft) break;

                        targetInventory.RemoveItem(itemIdx.UniqueName, targetItemCount);
                        CUtil.StackedAddInventory(mi_inventory, itemIdx.UniqueName, targetItemCount);

                        yield return new WaitForEndOfFrame();
                    }
                }
            }
        }

        public void OnNetworkMessageReceived(CDTO _msg, CSteamID _remoteID)
        {
            if (!mi_loaded)
            {
                CUtil.LogD("Received network message but storage is not fully loaded. Dropping message...");
                return;
            }

            switch (_msg.Type) //we use the usesleft value as our command type carrier
            {
                case EStorageRequestType.REQUEST_STATE:  //a client block requested this blocks state, send it back
                    if (Raft_Network.IsHost)
                    {
                        if (!mi_sceneStorage.IsUpgraded) return;

                        CAutoSorter.Get.SendTo(new CDTO(EStorageRequestType.RESPOND_STATE, ObjectIndex) { Info = mi_sceneStorage.Data }, _remoteID);
                    }
                    break;
                case EStorageRequestType.RESPOND_STATE:
                    if (_msg.Info == null)
                    {
                        CUtil.LogW("Invalid storage info received. Update the AutoSorter mod.");
                        return;
                    }
                    mi_sceneStorage.Data = _msg.Info;
                    UpdateStorageMaterials();
                    return;
                case EStorageRequestType.UPGRADE:
                    mi_sceneStorage.Data = _msg.Upgrade ? new CStorageData(_msg.ObjectIndex) : null;
                    UpdateStorageMaterials();
                    return;
            }
        }

        public bool Upgrade()
        {
            if (CAutoSorter.Config.UpgradeCosts.Any(_o => mi_localPlayer.Inventory.GetItemCount(ItemManager.GetItemByName(_o.Name)) < _o.Amount))
            {
                var notif = ComponentManager<HNotify>.Value.AddNotification(
                    HNotify.NotificationType.normal, 
                    "You dont have enough resources to upgrade!", 
                    5);
                notif.Show();
                return false;
            }

            foreach (var cost in CAutoSorter.Config.UpgradeCosts)
            {
                mi_localPlayer.Inventory.RemoveItem(cost.Name, cost.Amount);
            }

            mi_sceneStorage.Data = new CStorageData(ObjectIndex);
            UpdateStorageMaterials();
            SendUpgradeState(true);
            var soundRef = Traverse.Create(mi_sceneStorage.StorageComponent).Field("eventRef_open").GetValue<string>();
            if (!string.IsNullOrEmpty(soundRef))
            {
                RuntimeManager.PlayOneShot(soundRef, transform.position);
            }
            return true;
        }

        public void Downgrade()
        {
            int toAdd;
            foreach (var cost in CAutoSorter.Config.UpgradeCosts)
            {
                toAdd = (int)(cost.Amount * CAutoSorter.Config.ReturnItemsOnDowngradeMultiplier);
                if (toAdd > 0)
                {
                    CUtil.StackedAddInventory(mi_localPlayer.Inventory, cost.Name, toAdd);
                }
            }
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
        
        private void UpdateStorageMaterials()
        {
            var renderer = mi_sceneStorage.StorageComponent.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var rend in renderer)
            {
                var materials = rend.materials;
                foreach (var mat in materials)
                {
                    if (mat.HasProperty("_Diffuse"))
                    {
                        if (mi_sceneStorage.IsUpgraded && CAutoSorter.Config.ChangeStorageColorOnUpgrade)
                        {
                            if (mi_customTexture == null) //pretty heavy operation to make chests red that use the Vegetation shader. There is no way (afaik) to change the main color.
                            {
                                mi_originalTexture = mat.GetTexture("_Diffuse") as Texture2D;
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
                    else if(mat.HasProperty("_MainTex")) //probably std mat
                    {
                        mat.color = mi_sceneStorage.IsUpgraded && CAutoSorter.Config.ChangeStorageColorOnUpgrade ? Color.red : Color.white;
                    }
                }
                rend.materials = materials;
            }
        }

        private void SendUpgradeState(bool _isUpgraded)
        {
            CAutoSorter.Get.Broadcast(new CDTO(EStorageRequestType.UPGRADE, ObjectIndex) { Upgrade = _isUpgraded }); ;
        }

        //private void OnGUI()
        //{
        //    if (!mi_isHovered || !CAutoSorter.Config.Debug || CanvasHelper.ActiveMenu != MenuType.None) return;

        //    GUILayout.BeginHorizontal(GUILayout.Width(Screen.width));
        //    GUILayout.FlexibleSpace();
        //    GUILayout.BeginVertical(GUILayout.Height(Screen.height));
        //    GUILayout.FlexibleSpace();
        //    GUILayout.Box(mi_dialogContent);
        //    GUILayout.Space(5f);
        //    GUILayout.EndHorizontal();
        //}
    }
}
