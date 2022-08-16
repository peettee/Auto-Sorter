namespace pp.RaftMods.AutoSorter
{
    /// <summary>
    /// Used on DTO objects to quickly identify the message being sent by other clients.
    /// </summary>
    public enum EStorageRequestType
    {
        /// <summary>
        /// Request the current state/settings of all auto-sorters from the host.
        /// </summary>
        REQUEST_STATE               = -41,
        /// <summary>
        /// Response after a state/setting request, sent by the host, which contains the auto-sorter configuration.
        /// </summary>
        RESPOND_STATE               = -42,
        /// <summary>
        /// Sent if a player upgraded a regular storage to an auto-sorter.
        /// </summary>
        UPGRADE                     = -43,
        /// <summary>
        /// Sent if any player closes the auto-sorter UI to send possible configuration changes to all players.
        /// </summary>
        STORAGE_DATA_UPDATE         = -44,
        /// <summary>
        /// Special message which is sent whenever an auto-sorter transfers items to sync up storage inventories between players.
        /// </summary>
        STORAGE_INVENTORY_UPDATE    = -45,
        /// <summary>
        /// Sent whenever the ignore state of a storage changed.
        /// </summary>
        STORAGE_IGNORE_UPDATE       = -46,
    }
}
