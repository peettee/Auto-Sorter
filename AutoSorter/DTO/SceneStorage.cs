namespace pp.RaftMods.AutoSorter
{ 
    /// <summary>
    /// Wrapper class is used to manage scene lights and their connected components as well as storing meta info about each light.
    /// </summary>
    public class CSceneStorage
    {
        public bool IsUpgraded => Data != null;
        public CStorageData Data;
        public CStorageBehaviour AutoSorter;
        public Storage_Small StorageComponent;
        public RaycastInteractable Raycastable;
        public int PreModColliderLayer;
    }
}
