namespace Common_Utilities.EventHandlers
{
    using System.Collections.Generic;
    using System.Linq;
    using Exiled.API.Features;
    using Exiled.API.Features.Items;
    using Exiled.Events.EventArgs;
    using Exiled.Events.EventArgs.Server;
    using Exiled.Events.EventArgs.Warhead;
    using InventorySystem.Items.Pickups;
    using MEC;
    using UnityEngine;
    
    public class ServerHandlers
    {
        private readonly Plugin _plugin;
        public ServerHandlers(Plugin plugin) => this._plugin = plugin;

        public Vector3 EscapeZone = Vector3.zero;
        private bool friendlyFireDisable = false;
        
        public void OnRoundStarted()
        {
            if (_plugin.Config.AutonukeTime > -1)
                _plugin.Coroutines.Add(Timing.RunCoroutine(AutoNuke()));
            
            if (_plugin.Config.RagdollCleanupDelay > 0)
                _plugin.Coroutines.Add(Timing.RunCoroutine(RagdollCleanup()));
            
            if (_plugin.Config.ItemCleanupDelay > 0)
                _plugin.Coroutines.Add(Timing.RunCoroutine(ItemCleanup()));
            
            if (_plugin.Config.DisarmSwitchTeams)
                _plugin.Coroutines.Add(Timing.RunCoroutine(BetterDisarm()));
        }

        private IEnumerator<float> BetterDisarm()
        {
            for (;;)
            {
                yield return Timing.WaitForSeconds(1.5f);

                foreach (Player player in Player.List)
                {
                    if (EscapeZone == Vector3.zero)
                        EscapeZone = player.GameObject.GetComponent<Escape>().worldPosition;

                    if (!player.IsCuffed || (player.Role.Team != Team.CHI && player.Role.Team != Team.MTF) || (EscapeZone - player.Position).sqrMagnitude > 400f)
                        continue;

                    switch (player.Role.Type)
                    {
                        case RoleType.FacilityGuard:
                        case RoleType.NtfPrivate:
                        case RoleType.NtfSergeant:
                        case RoleType.NtfCaptain:
                        case RoleType.NtfSpecialist:
                            _plugin.Coroutines.Add(Timing.RunCoroutine(DropItems(player, player.Items.ToList())));
                            player.SetRole(RoleType.ChaosConscript);
                            break;
                        case RoleType.ChaosConscript:
                        case RoleType.ChaosMarauder:
                        case RoleType.ChaosRepressor:
                        case RoleType.ChaosRifleman:
                            _plugin.Coroutines.Add(Timing.RunCoroutine(DropItems(player, player.Items.ToList())));
                            player.SetRole(RoleType.NtfPrivate);
                            break;
                    }
                }
            }
        }

        public IEnumerator<float> DropItems(Player player, IEnumerable<Item> items)
        {
            yield return Timing.WaitForSeconds(1f);

            foreach (Item item in items)
                item.CreatePickup(player.Position, default);
        }

        public void OnWaitingForPlayers()
        {
            if (_plugin.Config.AfkLimit > 0)
            {
                _plugin.AfkDict.Clear();
                _plugin.Coroutines.Add(Timing.RunCoroutine(AfkCheck()));
            }

            if (friendlyFireDisable)
            {
                Log.Debug($"{nameof(OnWaitingForPlayers)}: Disabling friendly fire.", _plugin.Config.Debug);
                Server.FriendlyFire = false;
                friendlyFireDisable = false;
            }

            if (_plugin.Config.TimedBroadcastDelay > 0)
                _plugin.Coroutines.Add(Timing.RunCoroutine(ServerBroadcast()));
            
            Warhead.IsLocked = false;
        }
        
        public void OnRoundEnded(RoundEndedEventArgs ev)
        {
            if (_plugin.Config.FriendlyFireOnRoundEnd && !Server.FriendlyFire)
            {
                Log.Debug($"{nameof(OnRoundEnded)}: Enabling friendly fire.", _plugin.Config.Debug);
                Server.FriendlyFire = true;
                friendlyFireDisable = true;
            }

            foreach (CoroutineHandle coroutine in _plugin.Coroutines)
                Timing.KillCoroutines(coroutine);
            _plugin.Coroutines.Clear();
        }

        private IEnumerator<float> ServerBroadcast()
        {
            for (;;)
            {
                yield return Timing.WaitForSeconds(_plugin.Config.TimedBroadcastDelay);
                
                Map.Broadcast(_plugin.Config.TimedBroadcastDuration, _plugin.Config.TimedBroadcast);
            }
        }

        private IEnumerator<float> ItemCleanup()
        {
            for (;;)
            {
                yield return Timing.WaitForSeconds(_plugin.Config.ItemCleanupDelay);

                foreach (ItemPickupBase item in Object.FindObjectsOfType<ItemPickupBase>())
                    if (!_plugin.Config.ItemCleanupOnlyPocket || item.NetworkInfo.Position.y < -1500f)
                        item.DestroySelf();
            }
        }

        private IEnumerator<float> RagdollCleanup()
        {
            for (;;)
            {
                yield return Timing.WaitForSeconds(_plugin.Config.RagdollCleanupDelay);
                
                foreach (Ragdoll ragdoll in Map.Ragdolls)
                    if (!_plugin.Config.RagdollCleanupOnlyPocket || ragdoll.Position.y < -1500f)
                        ragdoll.Delete();
            }
        }

        private IEnumerator<float> AutoNuke()
        {
            yield return Timing.WaitForSeconds(_plugin.Config.AutonukeTime);

            switch (_plugin.Config.AutonukeBroadcast.Duration)
            {
                case 0:
                    break;
                case 1:
                    Cassie.Message(_plugin.Config.AutonukeBroadcast.Content);
                    break;
                default:
                    Map.Broadcast(_plugin.Config.AutonukeBroadcast);
                    break;
            }
            
            Warhead.Start();

            if (_plugin.Config.AutonukeLock)
                Warhead.IsLocked = true;
        }

        private IEnumerator<float> AfkCheck()
        {
            for (;;)
            {
                yield return Timing.WaitForSeconds(1f);

                foreach (Player player in Player.List)
                {
                    if (!_plugin.AfkDict.ContainsKey(player))
                        _plugin.AfkDict.Add(player, 0);

                    if (player.Role == RoleType.Spectator)
                        continue;

                    if (_plugin.AfkDict[player] >= _plugin.Config.AfkLimit)
                        player.Kick("You were kicked by a plugin for being AFK.");
                    else if (_plugin.AfkDict[player] >= (_plugin.Config.AfkLimit / 2))
                        player.Broadcast(10,
                            $"You have been AFK for {_plugin.AfkDict[player]} seconds. You will be automatically kicked if you remain AFK for a total of {_plugin.Config.AfkLimit} seconds.");
                    else
                        _plugin.AfkDict[player]++;
                }
            }
        }

        public void OnRestartingRound()
        {
            foreach (CoroutineHandle coroutine in _plugin.Coroutines)
                Timing.KillCoroutines(coroutine);
            _plugin.Coroutines.Clear();
        }

        public void OnWarheadStarting(StartingEventArgs ev)
        {
            foreach (Room room in Room.List)
                room.Color = _plugin.Config.WarheadColor;
        }

        public void OnWarheadStopping(StoppingEventArgs ev)
        {
            foreach (Room room in Room.List)
                room.ResetColor();
        }
    }
}