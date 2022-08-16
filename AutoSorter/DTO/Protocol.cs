
namespace pp.RaftMods.AutoSorter.Protocol
{
    /// <summary>
    /// Objects of this DTO class are transferred to other clients to inform the about auto-sorter config changes or upgrades of storages.
    /// </summary>
    [System.Serializable]
    public class CDTO
    {
        /// <summary>
        /// Type of network message.
        /// </summary>
        public EStorageRequestType Type;
        /// <summary>
        /// Object index of the storage this message refers to.
        /// </summary>
        public uint ObjectIndex;
        /// <summary>
        /// Storage data which contains the auto-sorter configuration and might be sent along to other clients.
        /// </summary>
        public CSorterStorageData Info;
        /// <summary>
        /// Storage data for all storages regardless if they are auto-sorters or not.
        /// </summary>
        public CGeneralStorageData AdditionalInfo;
        /// <summary>
        /// Is true if this message is sent on storage upgrade.
        /// </summary>
        public bool Upgrade;

        public CDTO() { }
        public CDTO(EStorageRequestType _type, uint _objectIndex)
        {
            Type = _type;
            ObjectIndex = _objectIndex;
        }
    }
}
