using System;
using System.Linq;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BSR.Managers;
using BSR.UI.ViewControllers;
using HMUI;
using IPA.Utilities;
using Newtonsoft.Json.Linq;
using Zenject;

namespace BSR.UI
{
    public class RankedFlowCoordinator : FlowCoordinator
    {
        [Inject] private readonly ProfileLeftViewController _profileLeftView;
        [Inject] private readonly LeaderboardRightViewController _leaderboardRightView;
        [Inject] private readonly OpponentRightViewController _opponentRightView;
        [Inject] private readonly MainMatchViewController _mainMatchView;
        [Inject] private readonly GameplaySetupViewController _gameplaySetupViewController;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation)
            {
                SetTitle("BS RANKED");

                _mainMatchView.OnMatchFoundEvent -= HandleMatchFound;
                _mainMatchView.OnMatchFoundEvent += HandleMatchFound;
                _mainMatchView.OnMatchConcludedEvent -= HandleMatchConcluded;
                _mainMatchView.OnMatchConcludedEvent += HandleMatchConcluded;

                if (RankedMatchManager.IsInRankedMatch)
                {
                    showBackButton = false;
                    var screenSystem = this.GetField<ScreenSystem, FlowCoordinator>("_screenSystem");
                    screenSystem?.SetBackButton(false, false);

                    ConfigureGameplaySetupViewController(showModifiers: false);
                    ProvideInitialViewControllers(_mainMatchView, _gameplaySetupViewController, _opponentRightView);
                }
                else
                {
                    showBackButton = true;
                    // Lobby state: Center = Queue Hub, Left = Your Profile, Right = Top 5 Leaderboard
                    ProvideInitialViewControllers(_mainMatchView, _profileLeftView, _leaderboardRightView);
                }
            }
            else if (RankedMatchManager.IsInRankedMatch)
            {
                // Returning from a map while still in a Ranked Match: keep Modifiers hidden & Back button disabled!
                showBackButton = false;
                var screenSystem = this.GetField<ScreenSystem, FlowCoordinator>("_screenSystem");
                screenSystem?.SetBackButton(false, false);

                ConfigureGameplaySetupViewController(showModifiers: false);
            }
        }

        private void OnDestroy()
        {
            if (_mainMatchView != null)
            {
                _mainMatchView.OnMatchFoundEvent -= HandleMatchFound;
                _mainMatchView.OnMatchConcludedEvent -= HandleMatchConcluded;
            }
        }

        private void HandleMatchFound(JObject matchData)
        {
            RankedMatchManager.IsInRankedMatch = true;
            showBackButton = false;

            var screenSystem = this.GetField<ScreenSystem, FlowCoordinator>("_screenSystem");
            screenSystem?.SetBackButton(false, false);

            // 1. LEFT SCREEN: Initialize GameplaySetupViewController with showModifiers = false!
            ConfigureGameplaySetupViewController(showModifiers: false);
            SetLeftScreenViewController(_gameplaySetupViewController, ViewController.AnimationType.In);

            // 2. RIGHT SCREEN: Swap from Leaderboard to Opponent Profile, then populate data
            SetRightScreenViewController(_opponentRightView, ViewController.AnimationType.In);
            _opponentRightView.SetOpponentData(matchData["enemy"] as JObject);

            // 3. CENTER SCREEN: Start Draft
            _mainMatchView.StartMatchDraft(matchData);
        }

        private void HandleMatchConcluded()
        {
            RankedMatchManager.IsInRankedMatch = false;
            showBackButton = true;

            var screenSystem = this.GetField<ScreenSystem, FlowCoordinator>("_screenSystem");
            screenSystem?.SetBackButton(true, true);

            // 1. Restore Modifiers tab for standard Solo play
            ConfigureGameplaySetupViewController(showModifiers: true);

            // 2. LEFT SCREEN: Swap back to your Profile
            SetLeftScreenViewController(_profileLeftView, ViewController.AnimationType.In);

            // 3. RIGHT SCREEN: Swap back to Top 5 Leaderboard
            SetRightScreenViewController(_leaderboardRightView, ViewController.AnimationType.In);
        }

        private void ConfigureGameplaySetupViewController(bool showModifiers)
        {
            try
            {
                var setupMethod = typeof(GameplaySetupViewController)
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Setup");

                if (setupMethod != null)
                {
                    var parameters = setupMethod.GetParameters();
                    var args = new object[parameters.Length];

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var p = parameters[i];
                        string pName = p.Name ?? "";

                        if (p.ParameterType == typeof(bool))
                        {
                            if (pName.IndexOf("Modifiers", StringComparison.OrdinalIgnoreCase) >= 0)
                                args[i] = showModifiers;
                            else if (pName.IndexOf("Multiplayer", StringComparison.OrdinalIgnoreCase) >= 0)
                                args[i] = false;
                            else
                                args[i] = true;
                        }
                        else if (p.ParameterType.IsEnum)
                        {
                            args[i] = Enum.IsDefined(p.ParameterType, "Singleplayer")
                                ? Enum.Parse(p.ParameterType, "Singleplayer")
                                : Enum.ToObject(p.ParameterType, 1);
                        }
                        else
                        {
                            args[i] = p.HasDefaultValue ? p.DefaultValue : null;
                        }
                    }

                    setupMethod.Invoke(_gameplaySetupViewController, args);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[BS Ranked] Could not configure GameplaySetupViewController: {ex.Message}");
            }
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            // Only restore modifiers if we are actually leaving BS Ranked (not transitioning into a song!)
            if (!RankedMatchManager.IsInRankedMatch)
            {
                ConfigureGameplaySetupViewController(showModifiers: true);
            }
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            if (RankedMatchManager.IsInRankedMatch)
                return;

            ConfigureGameplaySetupViewController(showModifiers: true);
            BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
        }
    }
}