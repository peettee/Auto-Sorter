using HarmonyLib;
using Newtonsoft.Json;
using pp.RaftMods.AutoSorter;
using pp.RaftMods.AutoSorter.Protocol;
using Steamworks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AutoSorter.Manager
{
    public class CNetwork
    {
        private static Dictionary<uint, CStorageBehaviour> mi_registeredNetworkBehaviours = new Dictionary<uint, CStorageBehaviour>();
        private static Raft_Network mi_network = ComponentManager<Raft_Network>.Value;

        private static short mi_modMessagesFloor = short.MaxValue;
        private static short mi_modMessagesCeil = short.MinValue;

        static CNetwork()
        {
            foreach (EStorageRequestType m in System.Enum.GetValues(typeof(EStorageRequestType)))
            {
                mi_modMessagesFloor = (short)Mathf.Min(mi_modMessagesFloor, (short)m);
                mi_modMessagesCeil = (short)Mathf.Max(mi_modMessagesCeil, (short)m);
            }
        }

        public static void Clear()
        {
            mi_registeredNetworkBehaviours.Clear();
        }

        public static void UnregisterNetworkBehaviour(CStorageBehaviour _behaviour)
        {
            mi_registeredNetworkBehaviours.Remove(_behaviour.BehaviourIndex);
        }

        public static void SendTo(CDTO _object, Network_UserId _id)
        {
            CUtil.LogD("Sending " + _object.Type + " to " + _id.Id + ".");
            mi_network.SendP2P(_id, CreateCarrierDTO(_object), EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        }

        public static void SendToHost(CDTO _object) => SendTo(_object, mi_network.HostID);

        public static void Broadcast(CDTO _object)
        {
            CUtil.LogD("Broadcasting " + _object.Type + " to others.");
            mi_network.RPC(CreateCarrierDTO(_object), Target.Other, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        }

        public static void BroadcastInventoryState(CStorageBehaviour _storageBehaviour)
        {
            CUtil.LogD("Broadcasting storage inventory change to others.");
            mi_network.RPC(new Message_Storage_Close((Messages)EStorageRequestType.STORAGE_INVENTORY_UPDATE, _storageBehaviour.LocalPlayer.StorageManager, _storageBehaviour.SceneStorage.StorageComponent), Target.Other, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        }

        public static void RegisterNetworkBehaviour(CStorageBehaviour _behaviour)
        {
            if (mi_registeredNetworkBehaviours.ContainsKey(_behaviour.ObjectIndex))
            {
                CUtil.LogW("Behaviour with ID" + _behaviour.ObjectIndex + " \"" + _behaviour.name + "\" was already registered.");
                return;
            }

            mi_registeredNetworkBehaviours.Add(_behaviour.ObjectIndex, _behaviour);
        }

        private static Message CreateCarrierDTO(CDTO _object)
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

        [HarmonyPatch(typeof(NetworkUpdateManager), "DeserializeSingleMessage")]
        private class CHarmonyPatch_NetworkUpdateManager_DeserializeSingleMessage
        {
            [HarmonyPrefix]
            private static bool DeserializeSingleMessage(Message msg, Network_UserId remoteID)
            {
                if (msg.t > mi_modMessagesCeil || msg.t < mi_modMessagesFloor)
                {
                    return true; //this is a message type not from this mod, ignore this package.
                }

                var inventoryUpdate = msg as Message_Storage_Close;
                var genericMsg = msg as Message_InitiateConnection;
                if (genericMsg == null && inventoryUpdate == null)
                {
                    CUtil.LogW("Invalid auto-sorter mod message received. Make sure all connected players use the same mod version.");
                    return false;
                }

                try
                {
                    if (inventoryUpdate != null)
                    {
                        if (!mi_registeredNetworkBehaviours.ContainsKey(inventoryUpdate.storageObjectIndex))
                        {
                            CUtil.LogW("No receiver with ID " + inventoryUpdate.storageObjectIndex + " found.");
                            return false;
                        }
                        mi_registeredNetworkBehaviours[inventoryUpdate.storageObjectIndex].OnInventoryUpdateReceived(inventoryUpdate);
                        return false;
                    }

                    CDTO modMessage = JsonConvert.DeserializeObject<CDTO>(genericMsg.password);
                    if (modMessage == null)
                    {
                        CUtil.LogW("Invalid network message received. Update the AutoSorter mod or make sure all connected players use the same version.");
                        return false;
                    }

                    if (!mi_registeredNetworkBehaviours.ContainsKey(modMessage.ObjectIndex))
                    {
                        CUtil.LogW("No receiver with ID " + modMessage.ObjectIndex + " found.");
                        return false;
                    }

                    if (modMessage.Info != null)
                    {
                        modMessage.Info.OnAfterDeserialize();
                    }

                    CUtil.LogD($"Received {modMessage.Type}({msg.t}) message from \"{remoteID}\".");
                    mi_registeredNetworkBehaviours[modMessage.ObjectIndex].OnNetworkMessageReceived(modMessage, remoteID);
                }
                catch (System.Exception _e)
                {
                    CUtil.LogW($"Failed to read mod network message ({msg.Type}) as {(Raft_Network.IsHost ? "host" : "client")}. You or one of your fellow players might have to update the mod.");
                    CUtil.LogD(_e.Message);
                    CUtil.LogD(_e.StackTrace);
                }

                return false;
            }
        }
    }
}
