namespace Dota2.Samples.LobbyDump.Bots.DOTABot.Enums
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