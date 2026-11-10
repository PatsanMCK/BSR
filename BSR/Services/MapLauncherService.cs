using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BS_Utils.Gameplay;
using BSR.Configuration;
using BSR.HarmonyPatches;
using IPA.Utilities;
using IPA.Utilities.Async;
using SongCore;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace BSR.Services
{
    public static class BeatmapExtensions
    {
        public static bool IsValid(this BeatmapKey key) => key.beatmapCharacteristic != null && !string.IsNullOrEmpty(key.levelId);

        public static IEnumerable<BeatmapKey> GetAllBeatmapKeys(this BeatmapLevel level)
        {
            if (level == null) return Enumerable.Empty<BeatmapKey>();

            var method = level.GetType().GetMethod("GetBeatmapKeys", BindingFlags.Public | BindingFlags.Instance);
            if (method != null && method.Invoke(level, null) is IEnumerable<BeatmapKey> mKeys)
                return mKeys;

            var prop = level.GetType().GetProperty("beatmapBasicData", BindingFlags.Public | BindingFlags.Instance);
            if (prop?.GetValue(level) is System.Collections.IDictionary dict)
                return dict.Keys.OfType<BeatmapKey>();

            foreach (var p in level.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (typeof(System.Collections.IDictionary).IsAssignableFrom(p.PropertyType))
                {
                    if (p.GetValue(level) is System.Collections.IDictionary d)
                    {
                        var dKeys = d.Keys.OfType<BeatmapKey>().ToList();
                        if (dKeys.Count > 0) return dKeys;
                    }
                }
            }
            return Enumerable.Empty<BeatmapKey>();
        }
    }

    public class MapLauncherService
    {
        private static readonly string[] AllowedMapExtensions =
        {
            ".dat", ".json", ".egg", ".ogg", ".wav", ".png", ".jpg", ".jpeg", ".vivify"
        };

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

                if (Loader.GetLevelByHash(upperHash) != null || _beatmapLevelsModel.GetBeatmapLevel(levelId) != null)
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

                    using var responseStream = await _http.GetStreamAsync(downloadUrl);
                    using var archive = new ZipArchive(responseStream, ZipArchiveMode.Read);

                    string fullDestinationDir = Path.GetFullPath(mapFolder);
                    if (!fullDestinationDir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                        fullDestinationDir += Path.DirectorySeparatorChar;

                    foreach (var entry in archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
                    {
                        string ext = Path.GetExtension(entry.Name);
                        if (!AllowedMapExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                            continue;

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

                await UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                {
                    Loader.Instance.RefreshSongs(false);
                });

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

            PreventPausePatch.ResetPauseAllowance();
            await EnsureMapDownloadedAsync(hash, songTitle, downloadUrl);

            await UnityMainThreadTaskScheduler.Factory.StartNew(async () =>
            {
                try
                {
                    var beatmapLevel = _beatmapLevelsModel.GetBeatmapLevel(expectedLevelId)
                                    ?? Loader.GetLevelByHash(upperHash);

                    if (beatmapLevel == null)
                    {
                        Plugin.Log.Error($"[BS Ranked] Could not find BeatmapLevel for {upperHash}");
                        return;
                    }

                    var beatmapLevelData = await LoadLevelDataCrossVersionAsync(beatmapLevel.levelID);
                    if (beatmapLevelData == null)
                    {
                        Plugin.Log.Error($"[BS Ranked] Could not load IBeatmapLevelData for {beatmapLevel.levelID}");
                        return;
                    }

                    if (!Enum.TryParse(difficultyName, true, out BeatmapDifficulty difficulty))
                        difficulty = BeatmapDifficulty.ExpertPlus;

                    var keys = beatmapLevel.GetAllBeatmapKeys().ToList();
                    var targetKey = keys.FirstOrDefault(k => k.difficulty == difficulty &&
                        k.beatmapCharacteristic != null &&
                        k.beatmapCharacteristic.serializedName.Equals(characteristicName ?? "Standard", StringComparison.OrdinalIgnoreCase));
                    if (!targetKey.IsValid()) targetKey = keys.FirstOrDefault(k => k.difficulty == difficulty);
                    if (!targetKey.IsValid()) targetKey = keys.LastOrDefault();

                    if (!targetKey.IsValid())
                    {
                        Plugin.Log.Error($"[BS Ranked] Could not find a valid BeatmapKey for {upperHash}");
                        return;
                    }

                    var playerData = _playerDataModel.playerData;
                    bool submitScores = PluginConfig.Instance?.EnableScoreSubmission ?? false;

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

                    if (!submitScores)
                    {
                        ScoreSubmission.DisableSubmission("BS Ranked");
                    }

                    Plugin.Log.Info($"[BS Ranked] Launching '{beatmapLevel.songName}' [{targetKey.difficulty}] (SubmitScores={submitScores}, NoFail={modifiers.noFailOn0Energy})");

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
                        else if (pType.IsEnum) args[i] = Enum.ToObject(pType, 0);
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
                    _ when pType == typeof(bool) => p.HasDefaultValue ? (bool)p.DefaultValue : false,
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
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var returnCtrl = pause != null
                    ? (typeof(PauseController).GetField("_returnToMenuController", flags)?.GetValue(pause) as IReturnToMenuController)
                    : null;

                if (returnCtrl == null)
                {
                    var pauseMenu = UnityEngine.Object.FindObjectOfType<PauseMenuManager>();
                    if (pauseMenu != null)
                    {
                        returnCtrl = typeof(PauseMenuManager).GetField("_returnToMenuController", flags)?.GetValue(pauseMenu) as IReturnToMenuController;
                    }
                }

                if (returnCtrl == null)
                {
                    returnCtrl = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                        .OfType<IReturnToMenuController>()
                        .FirstOrDefault();
                }

                if (returnCtrl != null)
                {
                    returnCtrl.ReturnToMenu();
                }
                else
                {
                    // Fallback: invoke the pause menu's back/forfeit button directly
                    var pauseMenu = UnityEngine.Object.FindObjectOfType<PauseMenuManager>();
                    var backBtn = pauseMenu?.GetComponentsInChildren<Button>(true)
                        .FirstOrDefault(b => b.name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0 || b.name.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0);
                    backBtn?.onClick.Invoke();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[BS Ranked] Error in ForceExitActiveMap: {ex.Message}");
            }
        }
    }
}