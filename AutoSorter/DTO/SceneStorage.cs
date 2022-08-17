namespace pp.RaftMods.AutoSorter
{ 
    /// <summary>
    /// Wrapper class is used to manage scene storages and their connected components as well as storing meta info about each auto-sorter.
    /// </summary>
    public class CSceneStorage
    {
        public bool IsUpgraded => Data != null;
        public CSorterStorageData Data;
        public CGeneralStorageData AdditionalData;
        public CStorageBehaviour AutoSorter;
        public Storage_Small StorageComponent;
        public bool IsInventoryDirty;
    }
}
