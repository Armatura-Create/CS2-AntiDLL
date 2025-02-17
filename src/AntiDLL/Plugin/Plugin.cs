using System.Collections.Concurrent;
using AntiDLL.API;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace AntiDLL
{
    public sealed partial class Plugin : BasePlugin, IPluginConfig<PluginConfig>, IAntiDLL
    {
        public required PluginConfig Config { get; set; } = new PluginConfig();
        private IGameEventManager2? GameEventManager;
        private readonly ConcurrentDictionary<CCSPlayerController, int> detectedPlayers = new();
        private Timer? CheckTimer;
        private Timer? ResetTimer;
        private const int DetectionThreshold = 3; // Количество раз, которое игрок должен попасть в список перед киком
        private const int ResetInterval = 900; // Интервал сброса нарушений в секундах (15 минут)

        public event AntiDLLCallback? OnDetection;

        public void OnConfigParsed(PluginConfig config)
        {
            if (config.Version < this.Config.Version)
            {
                base.Logger.LogWarning("Configuration is out of date. Consider updating the plugin.");
            }

            this.Config = config;
        }

        public override void Load(bool hotReload)
        {
            IGameEventManager2.Init.Hook(this.OnIGameEventManager2_Init, HookMode.Pre);
            CSource1LegacyGameEventGameSystem.ListenBitsReceived.Hook(this.OnSource1LegacyGameEventListenBitsReceived, HookMode.Pre);

            StartCheckTimer(); // Запускаем таймер для периодической проверки нарушителей
            StartResetTimer(); // Запускаем таймер сброса нарушений
            this.RegisterAPI(this);
        }

        private void StartCheckTimer()
        {
            if (CheckTimer != null)
            {
                CheckTimer.Stop();
                CheckTimer.Dispose();
            }

            CheckTimer = new Timer(Config.CheckInterval * 1000); // Запуск таймера с интервалом из конфига
            CheckTimer.Elapsed += (sender, args) => ProcessDetectedPlayers();
            CheckTimer.AutoReset = true;
            CheckTimer.Start();
        }

        private void StartResetTimer()
        {
            if (ResetTimer != null)
            {
                ResetTimer.Stop();
                ResetTimer.Dispose();
            }

            ResetTimer = new Timer(ResetInterval * 1000); // Запуск таймера сброса нарушений раз в 10 минут
            ResetTimer.Elapsed += (sender, args) => ResetDetectedPlayers();
            ResetTimer.AutoReset = true;
            ResetTimer.Start();
        }

        private HookResult OnIGameEventManager2_Init(DynamicHook hook)
        {
            this.GameEventManager = hook.GetParam<IGameEventManager2>(0);
            return HookResult.Continue;
        }

        private HookResult OnSource1LegacyGameEventListenBitsReceived(DynamicHook hook)
        {
            if (this.GameEventManager == null)
                return HookResult.Continue;

            var pLegacyEventSystem = hook.GetParam<CSource1LegacyGameEventGameSystem>(0);
            var pMsg = hook.GetParam<CLCMsg_ListenEvents>(1);
            var slot = pMsg.GetPlayerSlot();
            var pClientProxyListener = pLegacyEventSystem.GetLegacyGameEventListener(slot);

            if (pClientProxyListener == null)
                return HookResult.Continue;

            var player = Utilities.GetPlayerFromSlot(slot);
            if (player == null || player.IsBot)
                return HookResult.Continue;

            var events = Config.Blacklist.Where(eventName => this.GameEventManager.FindListener(pClientProxyListener, eventName)).ToList();

            if (events.Any())
            {
                base.Logger.LogInformation("Player {0} has blacklisted event listener(s): {1}", player.PlayerName, string.Join(", ", events));
                // Если игрок уже в списке, увеличиваем количество нарушений
                detectedPlayers.AddOrUpdate(player, 1, (key, count) => count + 1);
            }

            return HookResult.Continue;
        }

        private void ProcessDetectedPlayers()
        {
            // Проверяем всех игроков, которые были зафиксированы в нарушениях
            foreach (var (player, count) in detectedPlayers)
            {
                if (player.IsBot || !player.IsValid)
                {
                    detectedPlayers.TryRemove(player, out _);
                    continue;
                }

                // Если игрок нарушил `DetectionThreshold` раз, применяем наказание
                if (count >= DetectionThreshold)
                {
                    if (OnDetection != null)
                    {
                        OnDetection.Invoke(player, "Multiple detections");
                    }
                    else
                    {
                        base.Logger.LogInformation("Kicking player {0} for blacklisted event listener after {1} detections.", player.PlayerName, count);
                        player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED_UNTRUSTEDACCOUNT);
                    }

                    detectedPlayers.TryRemove(player, out _); // Удаляем игрока из списка нарушителей
                }
            }
        }

        private void ResetDetectedPlayers()
        {
            // Полностью очищаем список нарушителей раз в 10 минут
            detectedPlayers.Clear();
            base.Logger.LogInformation("Resetting detection counter for all players.");
        }

        public override void Unload(bool hotReload)
        {
            CSource1LegacyGameEventGameSystem.ListenBitsReceived.Unhook(this.OnSource1LegacyGameEventListenBitsReceived, HookMode.Pre);
            IGameEventManager2.Init.Unhook(this.OnIGameEventManager2_Init, HookMode.Pre);

            if (CheckTimer != null)
            {
                CheckTimer.Stop();
                CheckTimer.Dispose();
                CheckTimer = null;
            }

            if (ResetTimer != null)
            {
                ResetTimer.Stop();
                ResetTimer.Dispose();
                ResetTimer = null;
            }
        }
    }
}