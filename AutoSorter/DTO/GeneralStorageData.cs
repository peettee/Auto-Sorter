using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace pp.RaftMods.AutoSorter
{
    /// <summary>
    /// Class used to save none auto-sorter data if necessary.
    /// </summary>
    [System.Serializable]
    public class CGeneralStorageData
    {
        /// <summary>
        /// Name of the world this storage was saved in.
        /// </summary>
        public string SaveName;
        /// <summary>
        /// The object ID of the storage used to reapply settings on load.
        /// </summary>
        public ulong ObjectID;
        /// <summary>
        /// Priority of the auto sorter as set by the player in the auto-sorter UI.
        /// </summary>
        public bool Ignore;

        public CGeneralStorageData() { }
        public CGeneralStorageData(ulong _objectID, bool _ignore)
        {
            SaveName    = SaveAndLoad.CurrentGameFileName;
            ObjectID    = _objectID;
            Ignore      = _ignore;
        }
    }
}
