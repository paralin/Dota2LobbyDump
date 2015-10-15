using System;
using System.Threading;
using Appccelerate.StateMachine;
using Appccelerate.StateMachine.Machine;
using Dota2.GC;
using Dota2.GC.Dota.Internal;
using Dota2.Samples.LobbyDump.Bots.DOTABot.Enums;
using Dota2.Samples.LobbyDump.Utils;
using log4net;
using Newtonsoft.Json.Linq;
using SteamKit2;
using Timer = System.Timers.Timer;

namespace Dota2.Samples.LobbyDump.Bots.DOTABot
{
    public class LobbyBot
    {
        public delegate void LobbyUpdateHandler(CSODOTALobby lobby);

        private readonly SteamUser.LogOnDetails details;
        private readonly ILog log;
        private readonly Timer reconnectTimer = new Timer(5000);

        private SteamClient client;
        public DotaGCHandler dota;
        private SteamFriends friends;

        public ActiveStateMachine<States, Events> fsm;

        protected bool isRunning = false;
        private ulong lobbyChannelId;
        public CallbackManager manager;

        private Thread procThread;
        private bool reconnect;
        private SteamUser user;

        /// <summary>
        ///     Setup a new bot with some details.
        /// </summary>
        /// <param name="details"></param>
        /// <param name="extensions">any extensions you want on the state machine.</param>
        public LobbyBot(SteamUser.LogOnDetails details, params IExtension<States, Events>[] extensions)
        {
            reconnect = true;
            this.details = details;

            log = LogManager.GetLogger("LobbyBot " + details.Username);
            log.Debug("Initializing a new LobbyBot, username: " + details.Username);
            reconnectTimer.Elapsed += (sender, args) =>
            {
                reconnectTimer.Stop();
                fsm.Fire(Events.AttemptReconnect);
            };
            fsm = new ActiveStateMachine<States, Events>();
            foreach (var ext in extensions) fsm.AddExtension(ext);
            fsm.DefineHierarchyOn(States.Connecting)
                .WithHistoryType(HistoryType.None);
            fsm.DefineHierarchyOn(States.Connected)
                .WithHistoryType(HistoryType.None)
                .WithInitialSubState(States.Dota);
            fsm.DefineHierarchyOn(States.Dota)
                .WithHistoryType(HistoryType.None)
                .WithInitialSubState(States.DotaConnect)
                .WithSubState(States.DotaMenu)
                .WithSubState(States.DotaLobby);
            fsm.DefineHierarchyOn(States.Disconnected)
                .WithHistoryType(HistoryType.None)
                .WithInitialSubState(States.DisconnectNoRetry)
                .WithSubState(States.DisconnectRetry);
            fsm.DefineHierarchyOn(States.DotaLobby)
                .WithHistoryType(HistoryType.None);
            fsm.In(States.Connecting)
                .ExecuteOnEntry(InitAndConnect)
                .On(Events.Connected).Goto(States.Connected)
                .On(Events.Disconnected).Goto(States.DisconnectRetry)
                .On(Events.LogonFailSteamDown).Execute(SteamIsDown)
                .On(Events.LogonFailSteamGuard).Goto(States.DisconnectNoRetry) //.Execute(() => reconnect = false)
                .On(Events.LogonFailBadCreds).Goto(States.DisconnectNoRetry);
            fsm.In(States.Connected)
                .ExecuteOnExit(DisconnectAndCleanup)
                .On(Events.Disconnected).If(ShouldReconnect).Goto(States.Connecting)
                .Otherwise().Goto(States.Disconnected);
            fsm.In(States.Disconnected)
                .ExecuteOnEntry(DisconnectAndCleanup)
                .ExecuteOnExit(ClearReconnectTimer)
                .On(Events.AttemptReconnect).Goto(States.Connecting);
            fsm.In(States.DisconnectRetry)
                .ExecuteOnEntry(StartReconnectTimer);
            fsm.In(States.Dota)
                .ExecuteOnExit(DisconnectDota);
            fsm.In(States.DotaConnect)
                .ExecuteOnEntry(ConnectDota)
                .On(Events.DotaGCReady).Goto(States.DotaMenu);
            fsm.In(States.DotaMenu)
                .ExecuteOnEntry(SetOnlinePresence);
            fsm.In(States.DotaLobby)
                .ExecuteOnEntry(EnterLobbyChat)
                .ExecuteOnEntry(EnterBroadcastChannel)
                .On(Events.DotaLeftLobby).Goto(States.DotaMenu).Execute(LeaveChatChannel);
            fsm.Initialize(States.Connecting);
        }

        private void SteamIsDown()
        {
            // To force the retry
            fsm.Fire(Events.Disconnected);
            log.Debug("Steam is down, retrying connection...");
        }

        public event LobbyUpdateHandler LobbyUpdate;

        public void CreateLobby(string password)
        {
            leaveLobby();
            log.Debug("Setting up the lobby with passcode [" + password + "]...");

            var ldetails = new CMsgPracticeLobbySetDetails
            {
                allchat = false,
#if DEBUG
                allow_cheats = true,
#else
                allow_cheats = false,
#endif
                allow_spectating = true,
                fill_with_bots = false,
                game_mode = (uint) DOTA_GameMode.DOTA_GAMEMODE_AP,
                game_name = "Test Lobby",
                game_version = DOTAGameVersion.GAME_VERSION_CURRENT
            };
            dota.CreateLobby(password, ldetails);
        }

        public void Start()
        {
            fsm.Start();
        }

        private void ClearReconnectTimer()
        {
            reconnectTimer.Stop();
        }

        private void DisconnectDota()
        {
            dota.Stop();
        }

        public void leaveLobby()
        {
            if (dota.Lobby != null)
            {
                log.Debug("Leaving lobby.");
            }
            dota.AbandonGame();
            dota.LeaveLobby();
            LeaveChatChannel();
        }

        private void LeaveChatChannel()
        {
            if (lobbyChannelId != 0)
            {
                dota.LeaveChatChannel(lobbyChannelId);
                lobbyChannelId = 0;
            }
        }

        private void EnterLobbyChat()
        {
            dota.JoinChatChannel("Lobby_" + dota.Lobby.lobby_id, DOTAChatChannelType_t.DOTAChannelType_Lobby);
        }

        private void EnterBroadcastChannel()
        {
            dota.JoinCoachSlot();
        }

        private void SwitchTeam(DOTA_GC_TEAM team = DOTA_GC_TEAM.DOTA_GC_TEAM_GOOD_GUYS)
        {
            dota.JoinTeam(team, 2);
        }

        private void StartReconnectTimer()
        {
            reconnectTimer.Start();
        }

        private static void SteamThread(object state)
        {
            var bot = state as LobbyBot;
            if (bot == null) return;
            while (bot.isRunning && bot.manager != null)
            {
                bot.manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }
        }

        private bool ShouldReconnect()
        {
            return isRunning && reconnect;
        }

        private void SetOnlinePresence()
        {
            friends.SetPersonaState(EPersonaState.Online);
            friends.SetPersonaName("Test Bot");
        }

        private void InitAndConnect()
        {
            if (client == null)
            {
                client = new SteamClient();
                DotaGCHandler.Bootstrap(client);
                user = client.GetHandler<SteamUser>();
                friends = client.GetHandler<SteamFriends>();
                dota = client.GetHandler<DotaGCHandler>();
                manager = new CallbackManager(client);

                isRunning = true;
                manager.Add<SteamClient.ConnectedCallback>(c =>
                {
                    if (c.Result != EResult.OK)
                    {
                        fsm.FirePriority(Events.Disconnected);
                        isRunning = false;
                        return;
                    }

                    user.LogOn(details);
                });
                manager.Add<SteamClient.DisconnectedCallback>(
                    c =>
                    {
                        fsm?.Fire(Events.Disconnected);
                    });
                manager.Add<SteamUser.LoggedOnCallback>(c =>
                {
                    if (c.Result != EResult.OK)
                    {
                        log.Error("Logon failure, result: " + c.Result);
                        switch (c.Result)
                        {
                            case EResult.AccountLogonDenied:
                                fsm.Fire(Events.LogonFailSteamGuard);
                                return;
                            case EResult.ServiceUnavailable:
                            case EResult.TryAnotherCM:
                                fsm.Fire(Events.LogonFailSteamDown);
                                return;
                        }
                        fsm.Fire(Events.LogonFailBadCreds);
                    }
                    else
                    {
                        fsm.Fire(Events.Connected);
                    }
                });
                manager.Add<DotaGCHandler.UnhandledDotaGCCallback>(
                    c => log.Debug("Unknown GC message: " + c.Message.MsgType));
                manager.Add<DotaGCHandler.GCWelcomeCallback>(
                    c => fsm.Fire(Events.DotaGCReady));
                manager.Add<SteamFriends.FriendsListCallback>(c => log.Debug(c.FriendList));
                manager.Add<DotaGCHandler.PracticeLobbySnapshot>(c =>
                {
                    log.DebugFormat("Lobby snapshot received with state: {0}", c.lobby.state);

                    fsm.Fire(c.lobby.state == CSODOTALobby.State.RUN
                        ? Events.DotaEnterLobbyRun
                        : Events.DotaEnterLobbyUI);

                    switch (c.lobby.state)
                    {
                        case CSODOTALobby.State.UI:
                            fsm.FirePriority(Events.DotaEnterLobbyUI);
                            break;
                        case CSODOTALobby.State.RUN:
                            fsm.FirePriority(Events.DotaEnterLobbyRun);
                            break;
                    }
                    LobbyUpdate?.Invoke(c.lobby);
                });
                manager.Add<DotaGCHandler.PingRequest>(c =>
                {
                    log.Debug("GC Sent a ping request. Sending pong!");
                    dota.Pong();
                });
                manager.Add<DotaGCHandler.Popup>(
                    c => { log.DebugFormat("Received message (popup) from GC: {0}", c.result.id); });
                manager.Add<DotaGCHandler.ConnectionStatus>(
                    c => log.DebugFormat("GC Connection Status: {0}", JObject.FromObject(c.result)));
                manager.Add<DotaGCHandler.PracticeLobbySnapshot>(c =>
                {
                    log.DebugFormat("Lobby snapshot received with state: {0}", c.lobby.state);
                    if (c.lobby != null)
                    {
                        switch (c.lobby.state)
                        {
                            case CSODOTALobby.State.UI:
                                fsm.FirePriority(Events.DotaEnterLobbyUI);
                                break;
                            case CSODOTALobby.State.RUN:
                                fsm.FirePriority(Events.DotaEnterLobbyRun);
                                break;
                        }
                    }
                    LobbyUpdate?.Invoke(c.lobby);
                });
            }
            client.Connect();
            procThread = new Thread(SteamThread);
            procThread.Start(this);
        }

        private void ConnectDota()
        {
            log.Debug("Attempting to connect to Dota...");
            dota.Start();
        }

        public void DisconnectAndCleanup()
        {
            isRunning = false;
            if (client != null)
            {
                if (user != null)
                {
                    user.LogOff();
                    user = null;
                }
                if (client.IsConnected) client.Disconnect();
                client.RemoveHandler(typeof (DotaGCHandler));
                client = null;
            }
        }

        public void Destroy()
        {
            manager = null;
            if (fsm != null)
            {
                fsm.Stop();
                fsm.ClearExtensions();
                fsm = null;
            }
            reconnect = false;
            DisconnectAndCleanup();
            user = null;
            client = null;
            friends = null;
            dota = null;
            manager = null;
            log.Debug("Bot destroyed.");
        }

        public void StartGameAndLeave()
        {
            dota.LaunchLobby();
            dota.AbandonGame();
            dota.LeaveLobby();
        }

        public void StartGame()
        {
            dota.LaunchLobby();
        }
    }
}