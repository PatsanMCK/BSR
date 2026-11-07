using System;
using System.Linq;
using System.Reflection;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using BSR.Managers;
using BSR.Services;
using Newtonsoft.Json.Linq;
using Zenject;

namespace BSR.UI.ViewControllers
{
    [ViewDefinition("BSR.UI.Views.LeaderboardRightView.bsml")]
    public class LeaderboardRightViewController : BSMLAutomaticViewController
    {
        private static readonly string[] AllUiProps = typeof(LeaderboardRightViewController)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => Attribute.IsDefined(p, typeof(UIValue)))
            .Select(p => p.Name)
            .ToArray();

        [Inject] private readonly RankedMatchManager _match;
        [Inject] private readonly RankedNetworkClient _net;

        [UIValue("row1-left")] public string Row1Left { get; set; } = "<color=#FFD700>#1</color>";
        [UIValue("row1-right")] public string Row1Right { get; set; } = "<color=#888888>---</color>";

        [UIValue("row2-left")] public string Row2Left { get; set; } = "<color=#C0C0C0>#2</color>";
        [UIValue("row2-right")] public string Row2Right { get; set; } = "<color=#888888>---</color>";

        [UIValue("row3-left")] public string Row3Left { get; set; } = "<color=#CD7F32>#3</color>";
        [UIValue("row3-right")] public string Row3Right { get; set; } = "<color=#888888>---</color>";

        [UIValue("row4-left")] public string Row4Left { get; set; } = "#4";
        [UIValue("row4-right")] public string Row4Right { get; set; } = "<color=#888888>---</color>";

        [UIValue("row5-left")] public string Row5Left { get; set; } = "#5";
        [UIValue("row5-right")] public string Row5Right { get; set; } = "<color=#888888>---</color>";

        [UIValue("me-left")]
        public string MeLeft
        {
            get
            {
                var d = _match.GetRankDetails(_match.MyElo);
                return $"<b>#{_match.MyLeaderboardRank}</b>  <color={d.colorHex}>[{d.tier}]</color>";
            }
        }

        [UIValue("me-right")]
        public string MeRight => $"{_match.MyName}  <color=#00FFCC>({_match.MyElo} ELO)</color>";

        protected override async void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            _net.OnPacketReceived -= HandleServerPacket;
            _net.OnPacketReceived += HandleServerPacket;

            RefreshUI();
            if (_net.IsConnected)
                await _net.SendPacketAsync("GET_LEADERBOARD");
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            _net.OnPacketReceived -= HandleServerPacket;
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
        }

        [UIAction("on-refresh-click")]
        private async void OnRefreshClick()
        {
            if (_net.IsConnected)
                await _net.SendPacketAsync("GET_LEADERBOARD");
        }

        private void HandleServerPacket(string type, JObject data)
        {
            if (type == "PROFILE_DATA")
            {
                RefreshUI();
            }
            else if (type == "LEADERBOARD_DATA" && data != null)
            {
                if (data["me"] is JObject me)
                {
                    _match.MyLeaderboardRank = (int?)me["leaderboardRank"] ?? _match.MyLeaderboardRank;
                    _match.MyElo = (int?)me["elo"] ?? _match.MyElo;
                }

                var top = data["topPlayers"] as JArray;
                FormatRow(0, top, "#FFD700", out string l1, out string r1); Row1Left = l1; Row1Right = r1;
                FormatRow(1, top, "#C0C0C0", out string l2, out string r2); Row2Left = l2; Row2Right = r2;
                FormatRow(2, top, "#CD7F32", out string l3, out string r3); Row3Left = l3; Row3Right = r3;
                FormatRow(3, top, "#CCCCCC", out string l4, out string r4); Row4Left = l4; Row4Right = r4;
                FormatRow(4, top, "#CCCCCC", out string l5, out string r5); Row5Left = l5; Row5Right = r5;

                RefreshUI();
            }
        }

        private void FormatRow(int index, JArray top, string rankColor, out string leftText, out string rightText)
        {
            int rankNum = index + 1;
            if (top != null && index < top.Count && top[index] is JObject p)
            {
                string name = p["name"]?.ToString() ?? "Player";
                int elo = (int?)p["elo"] ?? 1000;
                var tierDetails = _match.GetRankDetails(elo);

                leftText = $"<color={rankColor}><b>#{rankNum}</b></color>  <color={tierDetails.colorHex}>[{tierDetails.tier}]</color>";
                rightText = $"{name} <color=#AAAAAA>({elo})</color>";
            }
            else
            {
                leftText = $"<color={rankColor}>#{rankNum}</color>";
                rightText = "<color=#666666>---</color>";
            }
        }

        public void RefreshUI()
        {
            foreach (string prop in AllUiProps) NotifyPropertyChanged(prop);
        }
    }
}