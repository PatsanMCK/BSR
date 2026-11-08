using System;
using System.Collections;
using System.Linq;
using BeatSaberMarkupLanguage;
using BSR.Managers;
using BSR.UI.ViewControllers;
using HMUI;
using IPA.Utilities;
using Newtonsoft.Json.Linq;
using UnityEngine;
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
                showBackButton = true;

                _mainMatchView.OnMatchFoundEvent += HandleMatchFound;
                _mainMatchView.OnMatchConcludedEvent += HandleMatchConcluded;

                // Lobby state: Center = Queue Hub, Left = Your Profile, Right = Top 5 Leaderboard
                ProvideInitialViewControllers(_mainMatchView, _profileLeftView, _leaderboardRightView);
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

            // 1. LEFT SCREEN: Swap from Profile to Gameplay Settings (Player Options, Colors, Environments)
            SetLeftScreenViewController(_gameplaySetupViewController, ViewController.AnimationType.In);
            StartCoroutine(HideModifiersTabCoroutine());

            // 2. RIGHT SCREEN: Swap from Leaderboard to Opponent Profile
            _opponentRightView.SetOpponentData(matchData["enemy"] as JObject);
            SetRightScreenViewController(_opponentRightView, ViewController.AnimationType.In);

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
            SetModifiersTabVisible(true);

            // 2. LEFT SCREEN: Swap back to your Profile
            SetLeftScreenViewController(_profileLeftView, ViewController.AnimationType.In);

            // 3. RIGHT SCREEN: Swap back to Top 5 Leaderboard
            SetRightScreenViewController(_leaderboardRightView, ViewController.AnimationType.In);
        }

        private IEnumerator HideModifiersTabCoroutine()
        {
            SetModifiersTabVisible(false);
            yield return null;
            SetModifiersTabVisible(false);
        }

        private void SetModifiersTabVisible(bool visible)
        {
            try
            {
                var segmentedComponent = _gameplaySetupViewController.GetComponentsInChildren<Component>(true)
                    .FirstOrDefault(c => c != null && c.GetType().Name.Contains("SegmentedControl"));

                if (segmentedComponent != null)
                {
                    var segmentedTransform = segmentedComponent.transform;
                    if (segmentedTransform.childCount > 0)
                    {
                        var modifiersCell = segmentedTransform.GetChild(0).gameObject;
                        modifiersCell.SetActive(visible);

                        if (!visible)
                        {
                            var selectMethod = segmentedComponent.GetType().GetMethod("SelectCellWithNumber");
                            selectMethod?.Invoke(segmentedComponent, new object[] { 1 });
                        }
                    }
                }

                var modifiersPanel = _gameplaySetupViewController.GetComponentInChildren<GameplayModifiersPanelController>(true);
                if (modifiersPanel != null && !visible)
                {
                    modifiersPanel.gameObject.SetActive(false);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[BS Ranked] Could not toggle modifiers tab: {ex.Message}");
            }
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            SetModifiersTabVisible(true);
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            if (RankedMatchManager.IsInRankedMatch)
                return;

            SetModifiersTabVisible(true);
            BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
        }
    }
}