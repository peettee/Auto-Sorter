﻿using AutoSorter.Messaging;
using AutoSorter.Wrappers;
using HarmonyLib;
using Newtonsoft.Json;
using pp.RaftMods.AutoSorter;
using pp.RaftMods.AutoSorter.Protocol;
using SocketIO;
using Steamworks;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using UnityEngine;

namespace AutoSorter.Manager
{
    public class CASNetwork : IASNetwork, IRecipient<NetworkPackageReceivedMessage>
    {
        private Dictionary<uint, CStorageBehaviour> mi_registeredNetworkBehaviours = new Dictionary<uint, CStorageBehaviour>();

        private short mi_modMessagesFloor = short.MaxValue;
        private short mi_modMessagesCeil = short.MinValue;

        private readonly IRaftNetwork mi_network;
        private readonly IASLogger mi_logger;

        public CASNetwork(IASLogger _logger, IRaftNetwork _network)
        {
            foreach (EStorageRequestType m in System.Enum.GetValues(typeof(EStorageRequestType)))
            {
                mi_modMessagesFloor = (short)Mathf.Min(mi_modMessagesFloor, (short)m);
                mi_modMessagesCeil = (short)Mathf.Max(mi_modMessagesCeil, (short)m);
            }

            mi_network = _network;
            mi_logger = _logger;
        }

        public void Clear()
        {
            mi_registeredNetworkBehaviours.Clear();
        }

        public void UnregisterNetworkBehaviour(CStorageBehaviour _behaviour)
        {
            mi_registeredNetworkBehaviours.Remove(_behaviour.ObjectIndex);
        }

        public void SendTo(CDTO _object, CSteamID _id)
        {
            mi_logger.LogD("Sending " + _object.Type + " to " + _id.m_SteamID + ".");
            mi_network.SendP2P(_id, CreateCarrierDTO(_object), EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        }

        public void SendToHost(CDTO _object) => SendTo(_object, mi_network.HostID);

        public void Broadcast(CDTO _object)
        {
            mi_logger.LogD("Broadcasting " + _object.Type + " to others.");
            mi_network.RPC(CreateCarrierDTO(_object), Target.Other, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        }

        public void BroadcastInventoryState(ISorterBehaviour _storageBehaviour)
        {
            mi_logger.LogD("Broadcasting storage inventory change to others.");
            mi_network.RPC(
                new Message_Storage_Close(
                    (Messages)EStorageRequestType.STORAGE_INVENTORY_UPDATE, 
                    _storageBehaviour.LocalPlayer.StorageManager.Unwrap(), 
                    _storageBehaviour.SceneStorage.StorageComponent.Unwrap()), 
                    Target.Other, 
                    EP2PSend.k_EP2PSendReliable, 
                    NetworkChannel.Channel_Game);
        }

        public void RegisterNetworkBehaviour(CStorageBehaviour _behaviour)
        {
            if (mi_registeredNetworkBehaviours.ContainsKey(_behaviour.ObjectIndex))
            {
                mi_logger.LogW("Behaviour with ID" + _behaviour.ObjectIndex + " \"" + _behaviour.name + "\" was already registered.");
                return;
            }

            mi_registeredNetworkBehaviours.Add(_behaviour.ObjectIndex, _behaviour);
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

        public void Receive(NetworkPackageReceivedMessage _message)
        {
            List<Message> resultMessages = _message.Packet.messages.ToList();
            List<Message> messages = _message.Packet.messages.ToList();

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
                    mi_logger.LogW("Invalid auto-sorter mod message received. Make sure all connected players use the same mod version.");
                    continue;
                }

                resultMessages.Remove(package);

                try
                {
                    if (inventoryUpdate != null)
                    {
                        if (!mi_registeredNetworkBehaviours.ContainsKey(inventoryUpdate.storageObjectIndex))
                        {
                            mi_logger.LogW("No receiver with ID " + inventoryUpdate.storageObjectIndex + " found.");
                            continue;
                        }
                        mi_registeredNetworkBehaviours[inventoryUpdate.storageObjectIndex].OnInventoryUpdateReceived(inventoryUpdate);
                        continue;
                    }

                    CDTO modMessage = JsonConvert.DeserializeObject<CDTO>(msg.password);
                    if (modMessage == null)
                    {
                        mi_logger.LogW("Invalid network message received. Update the AutoSorter mod or make sure all connected players use the same version.");
                        continue;
                    }

                    if (!mi_registeredNetworkBehaviours.ContainsKey(modMessage.ObjectIndex))
                    {
                        mi_logger.LogW("No receiver with ID " + modMessage.ObjectIndex + " found.");
                        continue;
                    }

                    if (modMessage.Info != null)
                    {
                        modMessage.Info.OnAfterDeserialize();
                    }

                    mi_logger.LogD($"Received {modMessage.Type}({package.t}) message from \"{_message.RemoteID}\".");
                    mi_registeredNetworkBehaviours[modMessage.ObjectIndex].OnNetworkMessageReceived(modMessage, _message.RemoteID);
                }
                catch (System.Exception _e)
                {
                    mi_logger.LogW($"Failed to read mod network message ({package.Type}) as {(Raft_Network.IsHost ? "host" : "client")}. You or one of your fellow players might have to update the mod.");
                    mi_logger.LogD(_e.Message);
                    mi_logger.LogD(_e.StackTrace);
                }
            }

            if (resultMessages.Count == 0)
            {
                _message.SetResult(false); //no packages left, nothing todo. Dont even call the vanilla method
                return;
            }

            //we remove all custom messages from the provided package and reassign the modified list so it is passed to the vanilla method.
            //this is to make sure we dont lose any vanilla packages
            _message.Packet.messages = resultMessages.ToArray();
            _message.SetResult(true); //nothing for the mod left to do here, let the vanilla behaviour take over
        }
    }
}
