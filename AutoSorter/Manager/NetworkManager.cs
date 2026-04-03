using pp.RaftMods.AutoSorter;
using pp.RaftMods.AutoSorter.Protocol;
using System.Collections.Generic;

namespace AutoSorter.Manager
{
    public class CNetwork
    {
        private readonly Dictionary<uint, CStorageBehaviour> mi_registeredNetworkBehaviours = new Dictionary<uint, CStorageBehaviour>();
        private readonly CAutoSorter mi_mod;

        private readonly Raft_Network mi_network = ComponentManager<Raft_Network>.Value;

        public CNetwork(CAutoSorter _mod)
        {
            mi_mod = _mod;
        }

        public void Clear()
        {
            mi_registeredNetworkBehaviours.Clear();
        }

        public void UnregisterNetworkBehaviour(CStorageBehaviour _behaviour)
        {
            mi_registeredNetworkBehaviours.Remove(_behaviour.BehaviourIndex);
        }

        public void SendTo(CDTO _object, Network_UserId _id)
        {
            CUtil.LogD("Sending " + _object.Type + " to " + _id.Id + ".");
            mi_mod.SendNetworkMessageToPlayer(_object, _id);
        }

        public void SendToHost(CDTO _object) 
            => SendTo(_object, mi_network.HostID);

        public void Broadcast(CDTO _object)
        {
            CUtil.LogD("Broadcasting " + _object.Type + " to others.");
            mi_mod.SendNetworkMessage(_object);
        }

        public void BroadcastInventoryState(CStorageBehaviour _storageBehaviour)
        {
            CUtil.LogD("Broadcasting storage inventory change to others.");
            mi_mod.SendNetworkMessage(new CDTOInventoryUpdate(_storageBehaviour.ObjectIndex, _storageBehaviour.Slots));
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

        public bool OnNetworkMessage(CDTO msg, Network_UserId remoteID)
        {
            if (!mi_registeredNetworkBehaviours.TryGetValue(msg.ObjectIndex, out CStorageBehaviour storageBehaviour))
            {
                CUtil.LogW($"No receiver with ID {msg.ObjectIndex} found.");
                return true;
            }

            CUtil.LogD($"Received {msg.Type}({msg.ObjectIndex}) message from \"{remoteID}\".");
            try
            {
                storageBehaviour.OnNetworkMessageReceived(msg, remoteID);
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
