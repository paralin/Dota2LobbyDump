namespace WLNetwork.BotEnums
{
    public enum States
    {
        Connecting,
        Disconnected,
        Connected,
        DisconnectNoRetry,
        DisconnectRetry,

        #region DOTA

        Dota,
        DotaConnect,
        DotaMenu,

        #region DOTALOBBY

        DotaLobby

        #endregion

        #endregion
    }
}