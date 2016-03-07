namespace Dota2.LobbyDump.Bots.Enums
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