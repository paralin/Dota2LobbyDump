using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Appccelerate.StateMachine;
using Appccelerate.StateMachine.Machine;
using Dota2.GC.Dota.Internal;
using Dota2.LobbyDump.Bots;
using Dota2.LobbyDump.Bots.Enums;
using KellermanSoftware.CompareNetObjects;
using log4net;
using log4net.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace Dota2.LobbyDump
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            BasicConfigurator.Configure();

            Console.Write("Enter a username: ");
            string username = Console.ReadLine();
            Console.Write("Enter a password: ");
            string password = Console.ReadLine();

            string lpass = "cow";

            bool keepRunning = true;

            var bot = new LobbyBot(new SteamUser.LogOnDetails { Username = username, Password = password });
            bot.fsm.AddExtension(new BotExtension(bot, lpass));
            bot.Start();

            var compare = new CompareLogic
            {
                Config =
                {
                    MaxDifferences = Int32.MaxValue,
                    TreatStringEmptyAndNullTheSame = true
                }
            };

            CSODOTALobby oldLobby = null;
            bot.LobbyUpdate += lobby =>
            {
                var ol = oldLobby;
                oldLobby = lobby;
                if (lobby == null) return;
                if (ol == null)
                {
                    bot.dota.InviteToLobby(76561198029304414L);
                    bot.dota.JoinTeam(DOTA_GC_TEAM.DOTA_GC_TEAM_PLAYER_POOL);
                }
                File.AppendAllText("snapshots.json", JObject.FromObject(lobby).ToString(Formatting.None) + "\n");
                var diffb = compare.Compare(ol, lobby);
                if (!diffb.AreEqual)
                {
                    File.AppendAllText("differences.txt", diffb.DifferencesString + "\n");
                    Console.WriteLine(diffb.DifferencesString);
                }

                if (lobby.state == CSODOTALobby.State.UI)
                {
                    if(lobby.members.Any(
                        m =>
                            (m.team == DOTA_GC_TEAM.DOTA_GC_TEAM_BAD_GUYS ||
                             m.team == DOTA_GC_TEAM.DOTA_GC_TEAM_GOOD_GUYS) &&
                            m.id != bot.dota.SteamClient.SteamID.ConvertToUInt64()))
                        bot.StartGame();
                }
                if (lobby.state != CSODOTALobby.State.POSTGAME) return;
                bot.leaveLobby();
                bot.Destroy();
                keepRunning = false;
            };

            while (keepRunning)
            {
                Thread.Sleep(1000);
            }
        }

        private class BotExtension : IExtension<States, Events>
        {
            private readonly LobbyBot bot;
            private readonly ILog log;
            private readonly string password;

            public BotExtension(LobbyBot bot, string password)
            {
                this.password = password;
                this.bot = bot;
                log = LogManager.GetLogger("LobbyBotE");
            }

            public void StartedStateMachine(IStateMachineInformation<States, Events> stateMachine)
            {
            }

            public void StoppedStateMachine(IStateMachineInformation<States, Events> stateMachine)
            {
            }

            public void EventQueued(IStateMachineInformation<States, Events> stateMachine, Events eventId,
                object eventArgument)
            {
            }

            public void EventQueuedWithPriority(IStateMachineInformation<States, Events> stateMachine, Events eventId,
                object eventArgument)
            {
            }

            public void SwitchedState(IStateMachineInformation<States, Events> stateMachine,
                IState<States, Events> oldState,
                IState<States, Events> newState)
            {
                log.Debug("Switched state to " + newState.Id);
                if (newState.Id == States.DotaMenu)
                {
                    bot.CreateLobby(password);
                }
            }

            public void InitializingStateMachine(IStateMachineInformation<States, Events> stateMachine,
                ref States initialState)
            {
            }

            public void InitializedStateMachine(IStateMachineInformation<States, Events> stateMachine,
                States initialState)
            {
            }

            public void EnteringInitialState(IStateMachineInformation<States, Events> stateMachine, States state)
            {
            }

            public void EnteredInitialState(IStateMachineInformation<States, Events> stateMachine, States state,
                ITransitionContext<States, Events> context)
            {
            }

            public void FiringEvent(IStateMachineInformation<States, Events> stateMachine, ref Events eventId,
                ref object eventArgument)
            {
            }

            public void FiredEvent(IStateMachineInformation<States, Events> stateMachine,
                ITransitionContext<States, Events> context)
            {
            }

            public void HandlingEntryActionException(IStateMachineInformation<States, Events> stateMachine,
                IState<States, Events> state, ITransitionContext<States, Events> context,
                ref Exception exception)
            {
            }

            public void HandledEntryActionException(IStateMachineInformation<States, Events> stateMachine,
                IState<States, Events> state, ITransitionContext<States, Events> context,
                Exception exception)
            {
            }

            public void HandlingExitActionException(IStateMachineInformation<States, Events> stateMachine,
                IState<States, Events> state, ITransitionContext<States, Events> context,
                ref Exception exception)
            {
            }

            public void HandledExitActionException(IStateMachineInformation<States, Events> stateMachine,
                IState<States, Events> state, ITransitionContext<States, Events> context,
                Exception exception)
            {
            }

            public void HandlingGuardException(IStateMachineInformation<States, Events> stateMachine,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> transitionContext, ref Exception exception)
            {
            }

            public void HandledGuardException(IStateMachineInformation<States, Events> stateMachine,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> transitionContext, Exception exception)
            {
            }

            public void HandlingTransitionException(IStateMachineInformation<States, Events> stateMachine,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> context, ref Exception exception)
            {
            }

            public void HandledTransitionException(IStateMachineInformation<States, Events> stateMachine,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> transitionContext, Exception exception)
            {
            }

            public void SkippedTransition(IStateMachineInformation<States, Events> stateMachineInformation,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> context)
            {
            }

            public void ExecutedTransition(IStateMachineInformation<States, Events> stateMachineInformation,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> context)
            {
            }

            public void ExecutingTransition(IStateMachineInformation<States, Events> stateMachineInformation,
                ITransition<States, Events> transition,
                ITransitionContext<States, Events> context)
            {
            }
        }
    }
}