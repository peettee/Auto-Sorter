
namespace pp.RaftMods.AutoSorter.Protocol
{
    [System.Serializable]
    public class CDTO
    {
        public EStorageRequestType Type;
        public uint ObjectIndex;
        public CStorageData Info;
        public bool Upgrade;

        public CDTO() { }
        public CDTO(EStorageRequestType _type, uint _objectIndex)
        {
            Type = _type;
            ObjectIndex = _objectIndex;
        }
    }
}
