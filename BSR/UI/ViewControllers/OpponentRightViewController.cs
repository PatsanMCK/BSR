using System;
using System.Linq;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using BSR.Managers;
using BSR.Services;
using HMUI;
using Newtonsoft.Json.Linq;
using Zenject;

namespace BSR.UI.ViewControllers
{
    [ViewDefinition("BSR.UI.Views.OpponentRightView.bsml")]
    public class OpponentRightViewController : BSMLAutomaticViewController
    {
        private static readonly string[] AllUiProps = typeof(OpponentRightViewController)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => Attribute.IsDefined(p, typeof(UIValue)))
            .Select(p => p.Name)
            .ToArray();

        [Inject] private readonly RankedMatchManager _match;
        [Inject] private readonly RankedNetworkClient _net;

        [UIComponent("opponent-pfp-img")] private readonly ImageView _pfpImage;

        [UIValue("opponent-name")] public string OpponentName => _match.EnemyName;
        [UIValue("opponent-tier")] public string OpponentTier => _match.GetRankDetails(_match.EnemyElo) is var d ? $"<color={d.colorHex}>{d.tier}</color>" : "B";
        [UIValue("opponent-elo")] public string OpponentElo => $"{_match.EnemyElo}";
        [UIValue("opponent-avg-stars")] public string OpponentAvgStars => $"Top Page Avg: {_match.EnemyAvgStars:F2} ★";
        [UIValue("opponent-hp-text")] public string OpponentHpText => _match.EnemyHp <= 0 ? $"<color=#FF3333>HP: 0 / {RankedMatchManager.MaxHp:N0} (KO!)</color>" : $"HP: {_match.EnemyHp:N0} / {RankedMatchManager.MaxHp:N0}";
        [UIValue("can-forfeit")] public bool CanForfeit { get; set; } = true;
        [UIValue("forfeit-btn-text")] public string ForfeitBtnText { get; set; } = "<color=#FF4444>Forfeit Match</color>";

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            if (firstActivation) _net.OnPacketReceived += HandlePacket;
            RefreshUI();
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
            await _net.SendPacketAsync("FORFEIT_MATCH");
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

        public void RefreshUI()
        {
            foreach (string prop in AllUiProps) NotifyPropertyChanged(prop);
            if (_pfpImage != null && !string.IsNullOrEmpty(_match.EnemyPfp))
                _pfpImage.SetImage(_match.EnemyPfp);
        }
    }
}