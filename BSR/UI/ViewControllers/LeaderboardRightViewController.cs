using BeatSaberMarkupLanguage.Attributes;
using BSR.Managers;
using BSR.Services;
using Newtonsoft.Json.Linq;
using Zenject;

namespace BSR.UI.ViewControllers
{
    [ViewDefinition("BSR.UI.Views.LeaderboardRightView.bsml")]
    public class LeaderboardRightViewController : RankedViewControllerBase
    {
        [Inject] private readonly RankedMatchManager _match;
        [Inject] private readonly RankedNetworkClient _net;

        // Top #1 Champion Row
        [UIValue("top1-left")] public string Top1Left { get; set; } = "<color=#FFD700>#1</color>";
        [UIValue("top1-right")] public string Top1Right { get; set; } = "<color=#888888>---</color>";

        // 5 Neighborhood Rows (2 Above, You, 2 Below)
        [UIValue("row1-left")] public string Row1Left { get; set; } = "";
        [UIValue("row1-right")] public string Row1Right { get; set; } = "<color=#888888>---</color>";

        [UIValue("row2-left")] public string Row2Left { get; set; } = "";
        [UIValue("row2-right")] public string Row2Right { get; set; } = "<color=#888888>---</color>";

        [UIValue("row3-left")] public string Row3Left { get; set; } = "";
        [UIValue("row3-right")] public string Row3Right { get; set; } = "<color=#888888>---</color>";

        [UIValue("row4-left")] public string Row4Left { get; set; } = "";
        [UIValue("row4-right")] public string Row4Right { get; set; } = "<color=#888888>---</color>";

        [UIValue("row5-left")] public string Row5Left { get; set; } = "";
        [UIValue("row5-right")] public string Row5Right { get; set; } = "<color=#888888>---</color>";

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
                // 1. Format #1 Global Champion
                if (data["topOne"] is JObject top1)
                {
                    string name = top1["name"]?.ToString() ?? "Player";
                    int elo = (int?)top1["elo"] ?? 1000;
                    bool isMe = (bool?)top1["isMe"] ?? false;
                    var d = _match.GetRankDetails(elo);

                    Top1Left = isMe
                        ? $"<color=#00FFCC><b>#1</b> [{d.tier}]</color>"
                        : $"<color=#FFD700><b>#1</b></color>  <color={d.colorHex}>[{d.tier}]</color>";

                    Top1Right = isMe
                        ? $"<color=#00FFCC><b>{name}</b> ({elo} ELO)</color>"
                        : $"{name} <color=#AAAAAA>({elo} ELO)</color>";
                }
                else
                {
                    Top1Left = "<color=#FFD700>#1</color>";
                    Top1Right = "<color=#888888>---</color>";
                }

                // 2. Format 5 Neighborhood Rows (2 above, you, 2 below)
                var neighborhood = data["neighborhood"] as JArray;

                FormatNeighborhoodRow(0, neighborhood, out string l1, out string r1); Row1Left = l1; Row1Right = r1;
                FormatNeighborhoodRow(1, neighborhood, out string l2, out string r2); Row2Left = l2; Row2Right = r2;
                FormatNeighborhoodRow(2, neighborhood, out string l3, out string r3); Row3Left = l3; Row3Right = r3;
                FormatNeighborhoodRow(3, neighborhood, out string l4, out string r4); Row4Left = l4; Row4Right = r4;
                FormatNeighborhoodRow(4, neighborhood, out string l5, out string r5); Row5Left = l5; Row5Right = r5;

                RefreshUI();
            }
        }

        private void FormatNeighborhoodRow(int index, JArray list, out string leftText, out string rightText)
        {
            if (list != null && index < list.Count && list[index] is JObject p)
            {
                int rank = (int?)p["rank"] ?? (index + 1);
                string name = p["name"]?.ToString() ?? "Player";
                int elo = (int?)p["elo"] ?? 1000;
                bool isMe = (bool?)p["isMe"] ?? false;
                var tierDetails = _match.GetRankDetails(elo);

                if (isMe)
                {
                    // Highlight YOU in bright cyan!
                    leftText = $"<color=#00FFCC><b>#{rank}</b>  [{tierDetails.tier}]</color>";
                    rightText = $"<color=#00FFCC><b>{name}</b> ({elo})</color>";
                }
                else
                {
                    leftText = $"<b>#{rank}</b>  <color={tierDetails.colorHex}>[{tierDetails.tier}]</color>";
                    rightText = $"{name} <color=#AAAAAA>({elo})</color>";
                }
            }
            else
            {
                leftText = "<color=#555555>#--</color>";
                rightText = "<color=#555555>---</color>";
            }
        }
    }
}