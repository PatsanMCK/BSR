using System;
using System.Reflection;
using System.Threading.Tasks;
using BS_Utils.Gameplay;
using BSR.Configuration;
using BSR.Managers;
using BSR.Services;
using BSR.UI;
using BSR.UI.ViewControllers;
using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using SiraUtil.Zenject;
using Zenject;
using IPALogger = IPA.Logging.Logger;

namespace BSR
{
    [Plugin(RuntimeOptions.DynamicInit)]
    public class Plugin
    {
        internal static IPALogger Log { get; private set; }
        internal static string LocalSteamId { get; private set; } = "76561198000000000";
        internal static string LocalUserName { get; private set; } = "Player";

        private Harmony _harmony;

        [Init]
        public void Init(IPALogger logger, Config config, Zenjector zenjector)
        {
            Log = logger;
            PluginConfig.Instance = config.Generated<PluginConfig>();
            _harmony = new Harmony("com.patsanmck.bsr");

            // 1. App Container
            zenjector.Install(Location.App, container =>
            {
                container.Bind<RankedApiService>().AsSingle();
                container.Bind<RankedMatchManager>().AsSingle();
            });

            // 2. Menu Container
            zenjector.Install(Location.Menu, container =>
            {
                container.BindInterfacesAndSelfTo<RankedNetworkClient>().AsSingle();
                container.Bind<MapLauncherService>().AsSingle();
                container.Bind<ProfileLeftViewController>().FromNewComponentAsViewController().AsSingle();
                container.Bind<LeaderboardRightViewController>().FromNewComponentAsViewController().AsSingle();
                container.Bind<OpponentRightViewController>().FromNewComponentAsViewController().AsSingle();
                container.Bind<MainMatchViewController>().FromNewComponentAsViewController().AsSingle();
                container.Bind<RankedFlowCoordinator>().FromNewComponentOnNewGameObject().AsSingle();
                container.BindInterfacesTo<MenuButtonManager>().AsSingle();
            });
        }

        [OnEnable]
        public void OnEnable()
        {
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            PatchThirdPartyModConflicts();
            _ = FetchUserInfoAsync();
        }

        private void PatchThirdPartyModConflicts()
        {
            try
            {
                var yeet2Type = AccessTools.TypeByName("NoteMovementFix.Patches.Yeet2");
                if (yeet2Type != null)
                {
                    var prefixMethod = AccessTools.Method(yeet2Type, "Prefix");
                    var finalizer = AccessTools.Method(typeof(Plugin), nameof(SuppressYeet2NullRefFinalizer));
                    if (prefixMethod != null && finalizer != null)
                    {
                        _harmony.Patch(prefixMethod, finalizer: new HarmonyMethod(finalizer));
                        Log.Info("[BS Ranked] Applied JDFixer (NoteMovementFix.Patches.Yeet2) NullReferenceException shield!");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[BS Ranked] Could not apply third-party mod shield: {ex.Message}");
            }
        }

        private static Exception SuppressYeet2NullRefFinalizer(Exception __exception)
        {
            if (__exception != null && RankedMatchManager.IsInRankedMatch)
            {
                return null;
            }
            return __exception;
        }

        private async Task FetchUserInfoAsync()
        {
            try
            {
                var userInfo = await GetUserInfo.GetUserAsync();
                if (userInfo != null && !string.IsNullOrEmpty(userInfo.platformUserId))
                {
                    LocalSteamId = userInfo.platformUserId;
                    LocalUserName = userInfo.userName ?? "Player";
                    Log.Info($"[BS Ranked] Fetched Steam User via BS_Utils: {LocalUserName} ({LocalSteamId})");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[BS Ranked] Could not fetch user info via BS_Utils: {ex.Message}");
            }
        }

        [OnDisable]
        public void OnDisable() => _harmony.UnpatchSelf();
    }
}