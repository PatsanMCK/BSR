using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BSR.Managers;
using BSR.Services;
using HMUI;
using Newtonsoft.Json.Linq;
using TMPro;
using Zenject;

namespace BSR.UI.ViewControllers
{
    [ViewDefinition("BSR.UI.Views.OpponentRightView.bsml")]
    public class OpponentRightViewController : RankedViewControllerBase
    {
        [Inject] private readonly RankedMatchManager _match;
        [Inject] private readonly RankedNetworkClient _net;

        [UIComponent("opponent-pfp-img")] private readonly ImageView _pfpImage;
        [UIComponent("opponent-name-txt")] private readonly TextMeshProUGUI _nameText;

        [UIValue("opponent-name")] public string OpponentName => $"<color={_match.EnemyColorTag}>{(string.IsNullOrEmpty(_match.EnemyName) ? "Opponent" : _match.EnemyName)}</color>";
        [UIValue("opponent-tier")] public string OpponentTier => _match.GetRankDetails(_match.EnemyElo) is var d ? $"<color={d.colorHex}>{d.tier}</color>" : "B";
        [UIValue("opponent-elo")] public string OpponentElo => $"{_match.EnemyElo}";
        [UIValue("opponent-avg-stars")] public string OpponentAvgStars => $"Top Page Avg: {_match.EnemyAvgStars:F2} ★";
        [UIValue("opponent-hp-text")] public string OpponentHpText => _match.EnemyHp <= 0 ? $"<color=#FF3333>HP: 0 / {RankedMatchManager.MaxHp:N0} (KO!)</color>" : $"HP: {_match.EnemyHp:N0} / {RankedMatchManager.MaxHp:N0}";
        [UIValue("can-forfeit")] public bool CanForfeit { get; set; } = true;
        [UIValue("forfeit-btn-text")] public string ForfeitBtnText { get; set; } = "<color=#FF4444>Forfeit Match</color>";

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            _net.OnPacketReceived -= HandlePacket;
            _net.OnPacketReceived += HandlePacket;
            RefreshUI();
        }

        protected override void OnDestroy()
        {
            if (_net != null)
            {
                _net.OnPacketReceived -= HandlePacket;
            }
            base.OnDestroy();
        }

        public void SetOpponentData(JObject enemyData)
        {
            CanForfeit = true;
            ForfeitBtnText = "<color=#FF4444>Forfeit Match</color>";
            if (enemyData != null)
            {
                _match.EnemyName = enemyData["name"]?.ToString() ?? "Opponent";
                _match.EnemyPfp = enemyData["pfp"]?.ToString() ?? "";
                _match.EnemyElo = (int?)enemyData["elo"] ?? 1000;
                _match.EnemyAvgStars = (double?)enemyData["avgStars"] ?? 6.0;
                _match.EnemyColorTag = enemyData["colorTag"]?.ToString() ?? "#FFFFFF";
                _match.EnemyHp = RankedMatchManager.MaxHp;
            }
            RefreshUI();
        }

        [UIAction("on-forfeit-click")]
        private async void OnForfeitClick()
        {
            if (!RankedMatchManager.IsInRankedMatch || !CanForfeit) return;
            CanForfeit = false;
            ForfeitBtnText = "Forfeiting...";
            RefreshUI();
            try
            {
                await _net.SendPacketAsync("FORFEIT_MATCH");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.Warn($"[BS Ranked] Forfeit error: {ex.Message}");
            }
        }

        private void HandlePacket(string type, JObject data)
        {
            if (type == "ROUND_RESULT")
            {
                _match.EnemyHp = (int?)data["enemyHp"] ?? _match.EnemyHp;
                RefreshUI();
            }
            else if (type == "MATCH_OVER")
            {
                CanForfeit = false;
                ForfeitBtnText = "Match Ended";
                _match.EnemyElo = (int?)data["enemyNewElo"] ?? _match.EnemyElo;
                RefreshUI();
            }
        }

        public override void RefreshUI()
        {
            base.RefreshUI();
            if (_nameText != null)
            {
                _nameText.text = OpponentName;
            }
            if (_pfpImage != null && !string.IsNullOrEmpty(_match.EnemyPfp))
            {
                _pfpImage.SetImage(_match.EnemyPfp);
            }
        }
    }
}