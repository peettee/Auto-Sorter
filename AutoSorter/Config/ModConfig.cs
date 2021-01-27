namespace pp.RaftMods.AutoSorter
{
    [System.Serializable]
    public class CModConfig
    {
        public int CheckIntervalSeconds;
        public bool Debug;
        public UpgradeCost[] UpgradeCosts;
        public bool AllStoragesAllowed;
        public float ReturnItemsOnDowngradeMultiplier;

        public CModConfig()
        {
            CheckIntervalSeconds                = 2;
            Debug                               = true;
            AllStoragesAllowed                  = false;
            ReturnItemsOnDowngradeMultiplier    = 0.5f;
            UpgradeCosts                        = new[]
            {
                new UpgradeCost("Plastic", 20),
                new UpgradeCost("Scrap", 10),
                new UpgradeCost("CircuitBoard", 6),
                new UpgradeCost("Battery", 1)
            };
        }
    }

    [System.Serializable]
    public class UpgradeCost
    {
        public string Name;
        public int Amount;

        public UpgradeCost() { }
        public UpgradeCost(string _name, int _amount)
        {
            Name    = _name;
            Amount  = _amount;
        }

        public Cost ToCost()
        {
            return new Cost(ItemManager.GetItemByName(Name), Amount);
        }
    }
}