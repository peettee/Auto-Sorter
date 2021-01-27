using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace pp.RaftMods.AutoSorter
{
    [DisallowMultipleComponent] //disallow to really make sure we never get into the situation of components being added twice on mod reload.
    public class CStorageBehaviour : MonoBehaviour_ID_Network/*, IRaycastable*/
    {
        private const int CREATIVE_INFINITE_COUNT = 2147483647;
        public const float DOWNGRADE_TIME_SECONDS = 3f;

        public bool HasInventorySpaceLeft => mi_inventory?.allSlots.Any(_o => _o.active && !_o.locked && !_o.StackIsFull()) ?? false;
        public bool AutoMode { get; private set; } = true;
        public System.DateTime? LastCheck { get; private set; }

        private CAutoSorter mi_mod;
        private CanvasHelper mi_canvas; 
        private Semih_Network mi_network;
        private Network_Player mi_localPlayer;

        private CSceneStorage mi_sceneStorage;

        private Inventory mi_inventory;

        private bool mi_customTexCreated;
        private bool mi_loaded = false;

        private Texture2D mi_originalTexture;
        private Texture2D mi_customTexture;

        //private CUISorterInteractWindow mi_interactWindow;
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
            //mi_interactWindow       = _interactWindow;
            mi_configWindow         = _configDialog;
            mi_inventory            = mi_sceneStorage.StorageComponent.GetInventoryReference();
            mi_network              = ComponentManager<Semih_Network>.Value;
            mi_localPlayer          = mi_network.GetLocalPlayer();
            //use our block objects index so we receive RPC calls
            //need to use an existing blockindex as clients/host need to be aware of it
            ObjectIndex = mi_sceneStorage.StorageComponent.ObjectIndex;
            NetworkIDManager.AddNetworkID(this);

            if (!Semih_Network.IsHost) //request lantern states from host after load
            {
                mi_network.SendP2P(
                    mi_network.HostID,
                    new Message_Battery_OnOff( //just use the battery message as it should never
                        Messages.Battery_OnOff,
                        mi_network.NetworkIDManager,
                        mi_network.LocalSteamID,
                        this.ObjectIndex,
                        (int)EStorageRequestType.REQUEST_STATE, //we use the battery uses int to pass our custom command type 
                        mi_sceneStorage.IsUpgraded), //doesnt matter
                    EP2PSend.k_EP2PSendReliable,
                    NetworkChannel.Channel_Game);
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
            if (!mi_sceneStorage.IsUpgraded || !HasInventorySpaceLeft) yield break;

            LastCheck = System.DateTime.UtcNow;

            Inventory targetInventory;
            int targetItemCount;
            int stackSize;
            List<int> alreadyChecked;
            foreach(var storage in mi_mod.SceneStorages)
            {
                if (storage.IsUpgraded) continue;

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
                }
                else
                {
                    foreach (var itemIdx in mi_sceneStorage.Data.Filters)
                    {
                        if (alreadyChecked.Contains(itemIdx.Key)) continue;

                        alreadyChecked.Add(itemIdx.Key);

                        var item = ItemManager.GetItemByIndex(itemIdx.Key);
                        targetItemCount = targetInventory.GetItemCount(item);

                        if (!itemIdx.Value.NoAmountControl)
                        {
                            targetItemCount = Mathf.Min(Mathf.Max(itemIdx.Value.MaxAmount - mi_inventory.GetItemCount(item), 0), targetItemCount);
                        }

                        if (targetItemCount <= 0 || targetItemCount == CREATIVE_INFINITE_COUNT) continue;

                        if (!HasInventorySpaceLeft) break;

                        targetInventory.RemoveItem(item.UniqueName, targetItemCount);
                        CUtil.StackedAddInventory(mi_inventory, item.UniqueName, targetItemCount);
                        yield return new WaitForEndOfFrame();
                    }
                }
            }
        }

        //public void OnIsRayed()
        //{
        //    //if (!mi_loaded) return;

        //    //if (!mi_canvas)
        //    //{
        //    //    mi_canvas = ComponentManager<CanvasHelper>.Value;
        //    //    return;
        //    //}

        //    //if (CanvasHelper.ActiveMenu == MenuType.None &&
        //    //    !PlayerItemManager.IsBusy &&
        //    //    mi_canvas.CanOpenMenu &&
        //    //    Helper.LocalPlayerIsWithinDistance(transform.position, Player.UseDistance + 0.5f))
        //    //{
        //    //    if (Input.GetKeyDown(KeyCode.F))
        //    //    {
        //    //        mi_configWindow.Show(mi_sceneStorage);
        //    //        return;
        //    //    }
        //    //}
        //}

        //public void OnRayEnter()
        //{
        //    if (mi_interactWindow)
        //    {
        //        mi_interactWindow.ShowStorage(mi_mod, this);
        //    }
        //}

        //public void OnRayExit()
        //{
        //    if (mi_configWindow)
        //    {
        //        mi_configWindow.Hide();
        //    }
        //    if (mi_interactWindow)
        //    {
        //        mi_interactWindow.HideStorage();
        //    }
        //}

        public override bool Deserialize(Message_NetworkBehaviour _msg, CSteamID _remoteID)
        {
            if (!mi_loaded) return base.Deserialize(_msg, _remoteID);

            Messages type = _msg.Type;
            if (_msg.Type != Messages.Battery_OnOff) return base.Deserialize(_msg, _remoteID);

            Message_Battery_OnOff msg = _msg as Message_Battery_OnOff;
            if (msg == null) return base.Deserialize(_msg, _remoteID);

            switch ((EStorageRequestType)msg.batteryUsesLeft) //we use the usesleft value as our command type carrier
            {
                case EStorageRequestType.REQUEST_STATE:  //a client block requested this blocks state, send it back
                    if (Semih_Network.IsHost)
                    {
                        if (!mi_sceneStorage.IsUpgraded) return true;

                        mi_network.SendP2P(
                            _remoteID,
                            new Message_Battery_OnOff(Messages.Battery_OnOff, mi_network.NetworkIDManager, mi_network.LocalSteamID, this.ObjectIndex, (int)EStorageRequestType.TOGGLE, true),
                            EP2PSend.k_EP2PSendReliable,
                            NetworkChannel.Channel_Game);
                    }
                    return true;
                case EStorageRequestType.TOGGLE:
                    mi_sceneStorage.Data = ((_msg as Message_Battery_OnOff)?.on ?? false) ? new CStorageData(_msg.ObjectIndex) : null;
                    UpdateStorageMaterials();
                    return true;
            }
            return true;
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
            RuntimeManager.PlayOneShot(Traverse.Create(mi_sceneStorage.StorageComponent).Field("eventRef_open").GetValue<string>(), transform.position);
            CUtil.Log("Upgraded " + ObjectIndex);
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
            RuntimeManager.PlayOneShot(Traverse.Create(mi_sceneStorage.StorageComponent).Field("eventRef_close").GetValue<string>(), transform.position);
        } 

        protected override void OnDestroy()
        {
            base.OnDestroy();
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
                        if (mi_sceneStorage.IsUpgraded)
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
                        mat.color = mi_sceneStorage.IsUpgraded ? Color.red : Color.white;
                    }
                }
                rend.materials = materials;
            }
        }
        
        private void SendUpgradeState(bool _isUpgraded)
        {
            var netMsg = new Message_Battery_OnOff(
                        Messages.Battery_OnOff,
                        RAPI.GetLocalPlayer().Network.NetworkIDManager,
                        RAPI.GetLocalPlayer().steamID,
                        ObjectIndex,
                        (int)EStorageRequestType.TOGGLE,
                        _isUpgraded);

            if (Semih_Network.IsHost)
            {
                mi_network.RPC(netMsg, Target.Other, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
                return;
            }
            mi_network.SendP2P(mi_network.HostID, netMsg, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
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
