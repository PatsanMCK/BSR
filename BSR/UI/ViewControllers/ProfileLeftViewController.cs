using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BS_Utils.Gameplay;
using BSR.Configuration;
using BSR.Managers;
using BSR.Services;
using HMUI;
using Newtonsoft.Json.Linq;
using Zenject;

namespace BSR.UI.ViewControllers
{
    [ViewDefinition("BSR.UI.Views.ProfileLeftView.bsml")]
    public class ProfileLeftViewController : RankedViewControllerBase
    {
        [Inject] private readonly RankedMatchManager _matchManager;
        [Inject] private readonly RankedApiService _apiService;
        [Inject] private readonly RankedNetworkClient _net;

        [UIComponent("pfp-img")] private readonly ImageView _pfpImage;
        [UIComponent("rank-modal")] private readonly ModalView _rankModal;

        [UIValue("player-name")] public string PlayerName => _matchManager.MyName;
        [UIValue("rank-tier")] public string RankTier => _matchManager.GetRankDetails(_matchManager.MyElo) is var d ? $"<color={d.colorHex}>{d.tier}</color>" : "B";
        [UIValue("elo-clickable-text")] public string EloClickableText => $"<b>{_matchManager.MyElo}</b> ELO";
        [UIValue("avg-stars-text")] public string AvgStarsText => $"Top Page Avg: {_matchManager.MyAvgStars:F2} ★";
        [UIValue("record-text")] public string RecordText => $"Record: {_matchManager.MyWins}W / {_matchManager.MyLosses}L";

        [UIValue("enable-score-submission")]
        public bool EnableScoreSubmission
        {
            get => PluginConfig.Instance?.EnableScoreSubmission ?? false;
            set
            {
                if (PluginConfig.Instance != null)
                {
                    PluginConfig.Instance.EnableScoreSubmission = value;
                }
                NotifyPropertyChanged();
            }
        }

        protected override async void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);

            _net.OnPacketReceived -= HandleServerPacket;
            _net.OnPacketReceived += HandleServerPacket;

            RefreshProfileUI();

            try
            {
                var userInfo = await GetUserInfo.GetUserAsync();
                if (userInfo != null && !string.IsNullOrEmpty(userInfo.platformUserId))
                {
                    _matchManager.MySteamId = userInfo.platformUserId;
                    _matchManager.MyName = userInfo.userName ?? "Player";
                }

                if (_net.IsConnected)
                {
                    await _net.SendPacketAsync("AUTH", new { steamId = _matchManager.MySteamId });
                }
                else if (firstActivation)
                {
                    var stats = await _apiService.GetScoreSaberStatsAsync(_matchManager.MySteamId);
                    _matchManager.MyName = stats.name;
                    _matchManager.MyPfp = stats.avatarUrl;
                    _matchManager.MyAvgStars = stats.avgStars;
                    RefreshProfileUI();
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.Warn($"[BS Ranked] Error during ProfileLeftViewController DidActivate: {ex.Message}");
            }
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            _net.OnPacketReceived -= HandleServerPacket;
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
        }

        [UIAction("on-rank-click")] private void OnRankClick() => _rankModal?.Show(true, true);
        [UIAction("close-rank-modal")] private void CloseRankModal() => _rankModal?.Hide(true);

        private void HandleServerPacket(string type, JObject data)
        {
            if (type == "PROFILE_DATA" || type == "MATCH_OVER") RefreshProfileUI();
        }

        public void RefreshProfileUI() => RefreshUI();

        public override void RefreshUI()
        {
            base.RefreshUI();
            if (_pfpImage != null && !string.IsNullOrEmpty(_matchManager.MyPfp))
                _pfpImage.SetImage(_matchManager.MyPfp);
        }
    }
}