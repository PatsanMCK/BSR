using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BS_Utils.Gameplay;
using IPA.Utilities;
using IPA.Utilities.Async;
using SongCore;
using UnityEngine;
using Zenject;

namespace BSR.Services
{
    public class MapLauncherService
    {
        private static readonly MethodInfo CachedStartStandardLevelMethod = typeof(MenuTransitionsHelper)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "StartStandardLevel")
            .OrderByDescending(m => m.GetParameters().Length)
            .FirstOrDefault();

        [Inject] private readonly MenuTransitionsHelper _menuTransitionsHelper;
        [Inject] private readonly BeatmapLevelsModel _beatmapLevelsModel;
        [Inject] private readonly PlayerDataModel _playerDataModel;
        [Inject] private readonly EnvironmentsListModel _environmentsListModel;

        private readonly HttpClient _http = new HttpClient();
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public bool IsCurrentlyInGameplay { get; private set; }
        private bool _wasPulledOutByServer;

        public MapLauncherService() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("BSRanked/1.0.0");

        public async Task EnsureMapDownloadedAsync(string hash, string songTitle, string downloadUrl)
        {
            await _lock.WaitAsync();
            try
            {
                string upperHash = hash.ToUpperInvariant();
                string levelId = $"custom_level_{upperHash}";

                // Wait if SongCore is still doing its initial boot scan
                for (int i = 0; i < 50 && !Loader.AreSongsLoaded; i++)
                    await Task.Delay(200);

                if (Loader.GetLevelByHash(upperHash) != null && _beatmapLevelsModel.GetBeatmapLevel(levelId) != null)
                    return;

                string mapFolder = Path.Combine(UnityGame.InstallPath, "Beat Saber_Data", "CustomLevels", upperHash);
                bool hasInfoDat = Directory.Exists(mapFolder) &&
                    (File.Exists(Path.Combine(mapFolder, "Info.dat")) || File.Exists(Path.Combine(mapFolder, "info.dat")));

                if (!hasInfoDat)
                {
                    if (string.IsNullOrEmpty(downloadUrl))
                        downloadUrl = $"https://cdn.beatsaver.com/{hash.ToLowerInvariant()}.zip";

                    Plugin.Log.Info($"[BS Ranked] Downloading '{songTitle}' ({upperHash})...");
                    if (Directory.Exists(mapFolder)) Directory.Delete(mapFolder, true);
                    Directory.CreateDirectory(mapFolder);

                    byte[] zipBytes = await _http.GetByteArrayAsync(downloadUrl);
                    using var ms = new MemoryStream(zipBytes);
                    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

                    string fullDestinationDir = Path.GetFullPath(mapFolder);
                    if (!fullDestinationDir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                        fullDestinationDir += Path.DirectorySeparatorChar;

                    foreach (var entry in archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
                    {
                        string destinationPath = Path.GetFullPath(Path.Combine(mapFolder, entry.FullName));
                        if (!destinationPath.StartsWith(fullDestinationDir, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Zip entry attempted directory traversal outside target directory.");

                        string parent = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                            Directory.CreateDirectory(parent);

                        using var entryStream = entry.Open();
                        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        await entryStream.CopyToAsync(fileStream);
                    }
                }

                // TournamentAssistant pattern: Refresh SongCore and wait for SongsLoadedEvent + BeatmapLevelsModel registration
                await UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                {
                    Loader.Instance.RefreshSongs(false);
                });

                // Give SongCore a moment to begin refreshing, then poll until AreSongsLoaded && level is in BeatmapLevelsModel
                await Task.Delay(400);
                for (int i = 0; i < 60; i++)
                {
                    if (Loader.AreSongsLoaded &&
                        (Loader.GetLevelByHash(upperHash) != null || _beatmapLevelsModel.GetBeatmapLevel(levelId) != null))
                    {
                        break;
                    }
                    await Task.Delay(200);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[BS Ranked] Failed downloading map: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task DownloadAndStartMapAsync(
            string hash, string songTitle, string downloadUrl, string difficultyName, string characteristicName,
            Action<int> onSongFinishedScore, Action onPlayerExitedEarly)
        {
            string upperHash = hash.ToUpperInvariant();
            string expectedLevelId = $"custom_level_{upperHash}";

            await EnsureMapDownloadedAsync(hash, songTitle, downloadUrl);

            await UnityMainThreadTaskScheduler.Factory.StartNew(async () =>
            {
                try
                {
                    // Prefer BeatmapLevelsModel first (ensures CustomLevelLoader has linked the .dat note files)
                    var beatmapLevel = _beatmapLevelsModel.GetBeatmapLevel(expectedLevelId)
                                    ?? Loader.GetLevelByHash(upperHash);

                    if (beatmapLevel == null)
                    {
                        Plugin.Log.Error($"[BS Ranked] Could not find BeatmapLevel for {upperHash}");
                        return;
                    }

                    // Cross-version safe IBeatmapLevelData loader (works on both 1.39.1 and 1.40.8)
                    var beatmapLevelData = await LoadLevelDataCrossVersionAsync(beatmapLevel.levelID);
                    if (beatmapLevelData == null)
                    {
                        Plugin.Log.Error($"[BS Ranked] Could not load IBeatmapLevelData for {beatmapLevel.levelID}");
                        return;
                    }

                    if (!Enum.TryParse(difficultyName, true, out BeatmapDifficulty difficulty))
                        difficulty = BeatmapDifficulty.ExpertPlus;

                    var keys = beatmapLevel.GetBeatmapKeys();
                    var targetKey = keys.FirstOrDefault(k => k.difficulty == difficulty &&
                        k.beatmapCharacteristic.serializedName.Equals(characteristicName ?? "Standard", StringComparison.OrdinalIgnoreCase));
                    if (!targetKey.IsValid()) targetKey = keys.FirstOrDefault(k => k.difficulty == difficulty);
                    if (!targetKey.IsValid()) targetKey = keys.LastOrDefault();

                    var playerData = _playerDataModel.playerData;

                    // TournamentAssistant pattern: Use explicit 15-parameter constructor instead of .CopyWith()
                    // Prevents 1.40.8 -> 1.39.1 optional parameter shift from turning on Zen Mode (0 notes)!
                    var modifiers = new GameplayModifiers(
                        energyType: GameplayModifiers.EnergyType.Bar,
                        noFailOn0Energy: true,
                        instaFail: false,
                        failOnSaberClash: false,
                        enabledObstacleType: GameplayModifiers.EnabledObstacleType.All,
                        noBombs: false,
                        fastNotes: false,
                        strictAngles: false,
                        disappearingArrows: false,
                        songSpeed: GameplayModifiers.SongSpeed.Normal,
                        noArrows: false,
                        ghostNotes: false,
                        proMode: false,
                        zenMode: false,
                        smallCubes: false
                    );

                    var playerColorScheme = GetPlayerActiveColorScheme(playerData);

                    IsCurrentlyInGameplay = true;
                    _wasPulledOutByServer = false;
                    ScoreSubmission.DisableSubmission("BS Ranked");

                    Plugin.Log.Info($"[BS Ranked] Launching '{beatmapLevel.songName}' [{targetKey.difficulty}] (ZenMode={modifiers.zenMode}, NoFail={modifiers.noFailOn0Energy})");

                    LaunchStandardLevelClean(targetKey, beatmapLevel, beatmapLevelData, playerData, modifiers, playerColorScheme,
                        (_, results) =>
                        {
                            IsCurrentlyInGameplay = false;
                            if (_wasPulledOutByServer) { _wasPulledOutByServer = false; return; }
                            if (results == null || results.levelEndStateType != LevelCompletionResults.LevelEndStateType.Cleared)
                                onPlayerExitedEarly?.Invoke();
                            else
                                onSongFinishedScore?.Invoke(results.multipliedScore);
                        });
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[BS Ranked] Error starting level: {ex}");
                }
            });
        }

        private async Task<IBeatmapLevelData> LoadLevelDataCrossVersionAsync(string levelId)
        {
            var methods = typeof(BeatmapLevelsModel)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "LoadBeatmapLevelDataAsync")
                .OrderByDescending(m => m.GetParameters().Length);

            foreach (var method in methods)
            {
                try
                {
                    var parms = method.GetParameters();
                    var args = new object[parms.Length];
                    for (int i = 0; i < parms.Length; i++)
                    {
                        var pType = parms[i].ParameterType;
                        if (pType == typeof(string)) args[i] = levelId;
                        else if (pType == typeof(CancellationToken)) args[i] = CancellationToken.None;
                        else if (pType.IsEnum) args[i] = Enum.ToObject(pType, 0); // BeatmapLevelDataVersion.Original
                        else args[i] = parms[i].HasDefaultValue ? parms[i].DefaultValue : null;
                    }

                    if (method.Invoke(_beatmapLevelsModel, args) is not Task task) continue;
                    await task;

                    var result = task.GetType().GetProperty("Result")?.GetValue(task);
                    if (result == null) continue;
                    if (result is IBeatmapLevelData direct) return direct;

                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    var rType = result.GetType();
                    var field = rType.GetFields(flags).FirstOrDefault(f => typeof(IBeatmapLevelData).IsAssignableFrom(f.FieldType));
                    if (field?.GetValue(result) is IBeatmapLevelData fData) return fData;

                    var prop = rType.GetProperties(flags).FirstOrDefault(p => typeof(IBeatmapLevelData).IsAssignableFrom(p.PropertyType));
                    if (prop?.GetValue(result) is IBeatmapLevelData pData) return pData;
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warn($"[BS Ranked] LoadBeatmapLevelDataAsync attempt failed: {ex.Message}");
                }
            }
            return null;
        }

        private void LaunchStandardLevelClean(
            BeatmapKey key, BeatmapLevel level, IBeatmapLevelData levelData, PlayerData playerData,
            GameplayModifiers modifiers, ColorScheme playerColorScheme,
            Action<StandardLevelScenesTransitionSetupDataSO, LevelCompletionResults> onFinished)
        {
            if (CachedStartStandardLevelMethod == null) return;

            var parameters = CachedStartStandardLevelMethod.GetParameters();
            var args = new object[parameters.Length];
            int colorIdx = 0;

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var pType = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                string tName = pType.Name, pName = p.Name ?? "";

                args[i] = (pName.ToLowerInvariant(), tName) switch
                {
                    ("gamemode", _) => "Solo",
                    ("backbuttontext", _) => "Forfeit Match",
                    ("levelfinishedcallback", _) => onFinished,
                    ("levelrestartedcallback" or "beforesceneswitchcallback" or "aftersceneswitchcallback", _) => null,
                    (_, "BeatmapKey") => key,
                    (_, "BeatmapLevel") => level,
                    (_, "IBeatmapLevelData") => levelData,
                    (_, "OverrideEnvironmentSettings") => playerData.overrideEnvironmentSettings,
                    (_, "ColorScheme") => colorIdx++ == 0 ? playerColorScheme : null,
                    (_, "GameplayModifiers") => modifiers,
                    (_, "PlayerSpecificSettings") => playerData.playerSpecificSettings,
                    (_, "EnvironmentsListModel") => _environmentsListModel,
                    _ when pType == typeof(bool) => pName.IndexOf("Lightshow", StringComparison.OrdinalIgnoreCase) >= 0,
                    _ when Nullable.GetUnderlyingType(pType) != null => null,
                    _ => p.HasDefaultValue ? p.DefaultValue : null
                };
            }

            CachedStartStandardLevelMethod.Invoke(_menuTransitionsHelper, args);
        }

        private static ColorScheme GetPlayerActiveColorScheme(PlayerData playerData)
        {
            var s = playerData?.colorSchemesSettings;
            if (s == null) return null;
            var t = s.GetType();
            return (t.GetMethod("GetOverrideColorScheme")?.Invoke(s, null)
                 ?? t.GetMethod("GetSelectedColorScheme")?.Invoke(s, null)
                 ?? t.GetProperty("overrideColorScheme")?.GetValue(s)) as ColorScheme;
        }

        public void ForceExitActiveMap()
        {
            if (!IsCurrentlyInGameplay) return;
            Plugin.Log.Info("[BS Ranked] Opponent forfeited! Pulling local player out of map...");
            _wasPulledOutByServer = true;
            IsCurrentlyInGameplay = false;

            try
            {
                var pause = UnityEngine.Object.FindObjectOfType<PauseController>();
                var returnCtrl = pause != null
                    ? ReflectionUtil.GetField<IReturnToMenuController, PauseController>(pause, "_returnToMenuController")
                    : null;

                if (returnCtrl == null)
                {
                    returnCtrl = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                        .OfType<IReturnToMenuController>()
                        .FirstOrDefault();
                }

                returnCtrl?.ReturnToMenu();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[BS Ranked] Error in ForceExitActiveMap: {ex.Message}");
            }
        }
    }
}