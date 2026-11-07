using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using BSR.Managers;
using BSR.Services;
using HMUI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Zenject;

namespace BSR.UI.ViewControllers
{
    [ViewDefinition("BSR.UI.Views.MainMatchView.bsml")]
    public class MainMatchViewController : BSMLAutomaticViewController
    {
        private static readonly string[] AllUiProps = typeof(MainMatchViewController)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => Attribute.IsDefined(p, typeof(UIValue)))
            .Select(p => p.Name)
            .ToArray();

        [Inject] private readonly RankedMatchManager _match;
        [Inject] private readonly RankedNetworkClient _net;
        [Inject] private readonly ProfileLeftViewController _profileView;
        [Inject] private readonly MapLauncherService _mapLauncher;

        public event Action<JObject> OnMatchFoundEvent;
        public event Action OnMatchConcludedEvent;

        private List<MapCardData> _currentCards = new List<MapCardData>();
        private JObject _pendingMatchOverData;
        private bool _isQueueing;
        private int _playersInQueue;

        private int _displayedP1Hp = RankedMatchManager.MaxHp;
        private int _displayedP2Hp = RankedMatchManager.MaxHp;
        private int _currentRound = 1;
        private int _currentMultiplier = 1;
        private string _pickerName = "Opponent";
        private bool _canReroll = true;

        [UIComponent("card1-img")] private readonly ImageView _card1Img;
        [UIComponent("card2-img")] private readonly ImageView _card2Img;
        [UIComponent("card3-img")] private readonly ImageView _card3Img;
        [UIComponent("ready-map-img")] private readonly ImageView _readyMapImg;
        [UIComponent("p1-img")] private readonly ImageView _p1Img;
        [UIComponent("p2-img")] private readonly ImageView _p2Img;
        [UIComponent("p2-over-img")] private readonly ImageView _p2OverImg;

        [UIValue("is-idle-state")] public bool IsIdleState { get; set; } = true;
        [UIValue("is-draft-state")] public bool IsDraftState { get; set; }
        [UIValue("is-ready-state")] public bool IsReadyState { get; set; }
        [UIValue("is-round-result-state")] public bool IsRoundResultState { get; set; }
        [UIValue("is-match-over-state")] public bool IsMatchOverState { get; set; }

        // --- CENTER QUEUE HUB PROPERTIES ---
        [UIValue("server-status-text")] public string ServerStatusText => _net.IsConnected ? "<color=#00FF66>● Server Online</color>" : "<color=#FF4444>● Server Offline</color>";
        [UIValue("is-server-online")] public bool IsServerOnline => _net.IsConnected;
        [UIValue("is-server-offline")] public bool IsServerOffline => !_net.IsConnected;
        [UIValue("is-queueing")] public bool IsQueueing => _net.IsConnected && _isQueueing;
        [UIValue("queue-status")] public string QueueStatus => _net.IsConnected ? $"In Queue: {_playersInQueue}" : "Cannot connect to BS Ranked Server";
        [UIValue("queue-btn-text")] public string QueueBtnText { get; set; } = "FIND MATCH";

        // --- DRAFT PROPERTIES ---
        [UIValue("is-my-turn")] public bool IsMyTurn { get; set; }
        [UIValue("can-reroll-and-turn")] public bool CanRerollAndTurn => _canReroll && IsMyTurn;
        [UIValue("select-btn-text")] public string SelectBtnText => IsMyTurn ? "Select" : "Waiting...";
        [UIValue("reroll-btn-text")] public string RerollBtnText { get; set; } = "Reroll Hand (1 Left)";
        [UIValue("match-pool-header")] public string MatchPoolHeader => $"Round {_currentRound} ({_currentMultiplier}x DMG)  |  Pool: {_match.TargetMatchStars:F2} ★";
        [UIValue("draft-turn-text")] public string DraftTurnText => IsMyTurn ? "<color=#00FF66>YOUR TURN TO PICK A MAP</color>" : $"<color=#FFAA00>Waiting for {_pickerName} to pick...</color>";
        [UIValue("draft-timer-text")] public string DraftTimerText { get; set; } = "Pick Time Left: 90s";

        [UIValue("card1-title")] public string Card1Title { get; set; } = "Loading...";
        [UIValue("card1-mapper")] public string Card1Mapper { get; set; } = "by Mapper";
        [UIValue("card1-stars")] public string Card1Stars { get; set; } = "0.00 ★";
        [UIValue("card2-title")] public string Card2Title { get; set; } = "Loading...";
        [UIValue("card2-mapper")] public string Card2Mapper { get; set; } = "by Mapper";
        [UIValue("card2-stars")] public string Card2Stars { get; set; } = "0.00 ★";
        [UIValue("card3-title")] public string Card3Title { get; set; } = "Loading...";
        [UIValue("card3-mapper")] public string Card3Mapper { get; set; } = "by Mapper";
        [UIValue("card3-stars")] public string Card3Stars { get; set; } = "0.00 ★";

        // --- READY ROOM PROPERTIES ---
        [UIValue("ready-map-title")] public string ReadyMapTitle { get; set; } = "Selected Map";
        [UIValue("ready-map-mapper")] public string ReadyMapMapper { get; set; } = "Mapped by Mapper";
        [UIValue("ready-map-stars")] public string ReadyMapStars { get; set; } = "0.00 ★";
        [UIValue("ready-status-text")] public string ReadyStatusText { get; set; } = "Players Ready: 0 / 2";
        [UIValue("ready-timer-text")] public string ReadyTimerText { get; set; } = "Downloading Map...";
        [UIValue("ready-btn-text")] public string ReadyBtnText { get; set; } = "READY!";
        [UIValue("can-click-ready")] public bool CanClickReady { get; set; }

        // --- ROUND RESULTS & MATCH OVER PROPERTIES ---
        [UIValue("round-result-header")] public string RoundResultHeader => _currentMultiplier > 1 ? $"ROUND {_currentRound} DAMAGE REPORT ({_currentMultiplier}x MULTIPLIER!)" : $"ROUND {_currentRound} DAMAGE REPORT";
        [UIValue("p1-round-score")] public string P1RoundScore { get; set; } = "Score: 0";
        [UIValue("p1-hp-bar")] public string P1HpBar => FormatHpBar(_displayedP1Hp);
        [UIValue("p1-hp-text")] public string P1HpText => FormatHpText(_displayedP1Hp);
        [UIValue("p1-damage-taken")] public string P1DamageTaken { get; set; } = "0 DMG";

        [UIValue("p2-name")] public string P2Name => _match.EnemyName;
        [UIValue("p2-round-score")] public string P2RoundScore { get; set; } = "Score: 0";
        [UIValue("p2-hp-bar")] public string P2HpBar => FormatHpBar(_displayedP2Hp);
        [UIValue("p2-hp-text")] public string P2HpText => FormatHpText(_displayedP2Hp);
        [UIValue("p2-damage-taken")] public string P2DamageTaken { get; set; } = "0 DMG";

        [UIValue("has-next-round-timer")] public bool HasNextRoundTimer => _pendingMatchOverData == null && _match.MyHp > 0 && _match.EnemyHp > 0;
        [UIValue("post-round-timer-text")] public string PostRoundTimerText { get; set; } = "Next Round in 60s";
        [UIValue("can-continue-round")] public bool CanContinueRound { get; set; }
        [UIValue("continue-btn-text")] public string ContinueBtnText { get; set; } = "Calculating Damage...";

        [UIValue("match-outcome-title")] public string MatchOutcomeTitle { get; set; } = "VICTORY!";
        [UIValue("p2-final-elo")] public string P2FinalElo { get; set; } = "1000 ELO";
        [UIValue("elo-change-text")] public string EloChangeText { get; set; } = "<color=#00FF66>+16 ELO</color>";
        [UIValue("new-total-elo-text")] public string NewTotalEloText { get; set; } = "1016 ELO";

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            if (firstActivation)
            {
                _net.OnConnectionStatusChanged += HandleConnectionChanged;
                _net.OnPacketReceived += HandleServerPacket;
            }
            HandleConnectionChanged(_net.IsConnected);
        }

        protected override void OnDestroy()
        {
            if (_net != null)
            {
                _net.OnConnectionStatusChanged -= HandleConnectionChanged;
                _net.OnPacketReceived -= HandleServerPacket;
            }
            base.OnDestroy();
        }

        private void RefreshUI()
        {
            foreach (string prop in AllUiProps) NotifyPropertyChanged(prop);
        }

        private void RefreshHpBars()
        {
            NotifyPropertyChanged(nameof(P1HpBar));
            NotifyPropertyChanged(nameof(P1HpText));
            NotifyPropertyChanged(nameof(P2HpBar));
            NotifyPropertyChanged(nameof(P2HpText));
        }

        private void SetState(bool idle = false, bool draft = false, bool ready = false, bool roundResult = false, bool matchOver = false)
        {
            IsIdleState = idle; IsDraftState = draft; IsReadyState = ready; IsRoundResultState = roundResult; IsMatchOverState = matchOver;
            RefreshUI();
        }

        private void HandleConnectionChanged(bool connected)
        {
            if (!connected)
            {
                _isQueueing = false;
                _playersInQueue = 0;
            }
            QueueBtnText = _isQueueing ? "CANCEL QUEUE" : "FIND MATCH";
            RefreshUI();
        }

        private static string GetHpColor(int hp) => (float)hp / RankedMatchManager.MaxHp is var p && p > 0.5f ? "#00FF66" : p > 0.2f ? "#FFCC00" : "#FF3333";
        private static string FormatHpBar(int hp) => Mathf.RoundToInt(Mathf.Clamp01((float)hp / RankedMatchManager.MaxHp) * 20f) is var f ? $"<color={GetHpColor(hp)}>[{new string('█', f)}{new string('░', 20 - f)}]</color>" : "";
        private static string FormatHpText(int hp) => hp <= 0 ? $"<color=#FF3333>HP: 0 / {RankedMatchManager.MaxHp:N0} (KO!)</color>" : $"<color={GetHpColor(hp)}>HP: {hp:N0} / {RankedMatchManager.MaxHp:N0}</color>";

        public void StartMatchDraft(JObject matchData = null)
        {
            RankedMatchManager.IsInRankedMatch = true;
            _pendingMatchOverData = null;
            _match.MyHp = _match.EnemyHp = _displayedP1Hp = _displayedP2Hp = RankedMatchManager.MaxHp;
            _canReroll = true;
            RerollBtnText = "Reroll Hand (1 Left)";
            DraftTimerText = $"Pick Time Left: {(int?)matchData?["timeLeft"] ?? 90}s";

            if (matchData != null)
            {
                if (matchData["enemy"] is JObject enemy)
                {
                    _match.EnemyName = enemy["name"]?.ToString() ?? "Opponent";
                    _match.EnemyPfp = enemy["pfp"]?.ToString() ?? "";
                    _match.EnemyElo = (int?)enemy["elo"] ?? 1000;
                    _match.EnemyAvgStars = (double?)enemy["avgStars"] ?? 6.0;
                }
                _currentRound = (int?)matchData["roundNumber"] ?? 1;
                _currentMultiplier = (int?)matchData["multiplier"] ?? 1;
                _pickerName = matchData["pickerName"]?.ToString() ?? "Opponent";
                IsMyTurn = (bool?)matchData["isMyTurn"] ?? false;
                ApplyCardsFromServer(matchData["cards"] as JArray);
            }
            SetState(draft: true);
        }

        private async void HandleServerPacket(string type, JObject data)
        {
            switch (type)
            {
                case "QUEUE_STATUS":
                    _playersInQueue = (int?)data?["count"] ?? 0;
                    RefreshUI();
                    break;

                case "MATCH_FOUND":
                    _isQueueing = false;
                    QueueBtnText = "FIND MATCH";
                    OnMatchFoundEvent?.Invoke(data);
                    break;

                case "START_DRAFT":
                    _canReroll = true;
                    RerollBtnText = "Reroll Hand (1 Left)";
                    _currentRound = (int?)data["roundNumber"] ?? (_currentRound + 1);
                    _currentMultiplier = (int?)data["multiplier"] ?? 1;
                    _pickerName = data["pickerName"]?.ToString() ?? "Opponent";
                    IsMyTurn = (bool?)data["isMyTurn"] ?? false;
                    DraftTimerText = $"Pick Time Left: {(int?)data["timeLeft"] ?? 90}s";
                    ApplyCardsFromServer(data["cards"] as JArray);
                    SetState(draft: true);
                    break;

                case "DRAFT_TICK":
                    int draftSec = (int?)data["timeLeft"] ?? 0;
                    DraftTimerText = draftSec <= 15
                        ? $"<color=#FF4444>Pick Time Left: {draftSec}s </color>"
                        : $"Pick Time Left: {draftSec}s";
                    NotifyPropertyChanged(nameof(DraftTimerText));
                    break;

                case "MAP_LOCKED":
                    var map = data["map"];
                    string hash = map["Hash"]?.ToString();
                    ReadyMapTitle = map["Title"]?.ToString() ?? "Ranked Map";
                    ReadyMapMapper = $"Mapped by: {map["Mapper"] ?? "Unknown Mapper"}";
                    ReadyMapStars = $"{(double?)map["Stars"] ?? 0.0:F2} ★ ({map["Difficulty"] ?? "ExpertPlus"})";
                    ReadyTimerText = "Downloading Map...";
                    ReadyStatusText = "Installing map locally...";
                    CanClickReady = false;
                    ReadyBtnText = "Installing Map...";
                    _readyMapImg?.SetImage(map["CoverUrl"]?.ToString() ?? "");
                    SetState(ready: true);

                    await _mapLauncher.EnsureMapDownloadedAsync(hash, ReadyMapTitle, map["DownloadUrl"]?.ToString());
                    ReadyStatusText = "Waiting for opponent to finish downloading...";
                    ReadyBtnText = "Waiting for Opponent...";
                    RefreshUI();
                    await _net.SendPacketAsync("MAP_DOWNLOADED");
                    break;

                case "DOWNLOADS_COMPLETE":
                    ReadyTimerText = $"Starting in {(int?)data["timeLeft"] ?? 15}s";
                    ReadyStatusText = "Both players have the map! (0 / 2 Ready)";
                    CanClickReady = true;
                    ReadyBtnText = "READY!";
                    RefreshUI();
                    break;

                case "READY_TICK":
                    ReadyTimerText = $"Starting in {(int?)data["timeLeft"] ?? 0}s";
                    ReadyStatusText = $"Players Ready: {(int?)data["readyCount"] ?? 0} / 2";
                    RefreshUI();
                    break;

                case "START_MAP":
                    _ = _mapLauncher.DownloadAndStartMapAsync(
                        data["Hash"]?.ToString(),
                        data["Title"]?.ToString() ?? "Ranked Map",
                        data["DownloadUrl"]?.ToString(),
                        data["Difficulty"]?.ToString() ?? "ExpertPlus",
                        data["Characteristic"]?.ToString() ?? "Standard",
                        myScore => _ = _net.SendPacketAsync("SUBMIT_SCORE", new { score = myScore }),
                        () => _ = _net.SendPacketAsync("FORFEIT_MATCH"));
                    break;

                case "CARDS_DEALT":
                    ApplyCardsFromServer(data["cards"] as JArray);
                    break;

                case "ROUND_RESULT":
                    _currentRound = (int?)data["roundNumber"] ?? _currentRound;
                    _currentMultiplier = (int?)data["multiplier"] ?? _currentMultiplier;
                    int oldP1 = _match.MyHp, oldP2 = _match.EnemyHp;
                    _match.MyHp = (int?)data["myHp"] ?? RankedMatchManager.MaxHp;
                    _match.EnemyHp = (int?)data["enemyHp"] ?? RankedMatchManager.MaxHp;

                    PostRoundTimerText = $"Next Round in {(int?)data["timeLeft"] ?? 60}s";
                    P1RoundScore = $"Score: {(int?)data["myScore"] ?? 0:N0}";
                    P2RoundScore = $"Score: {(int?)data["enemyScore"] ?? 0:N0}";
                    P1DamageTaken = P2DamageTaken = "Calculating...";
                    _p1Img?.SetImage(_match.MyPfp);
                    _p2Img?.SetImage(_match.EnemyPfp);
                    SetState(roundResult: true);

                    StopAllCoroutines();
                    StartCoroutine(AnimateHpLossCoroutine(oldP1, _match.MyHp, oldP2, _match.EnemyHp, (int?)data["myDmgTaken"] ?? 0, (int?)data["enemyDmgTaken"] ?? 0));
                    break;

                case "POST_ROUND_TICK":
                    int postSec = (int?)data["timeLeft"] ?? 0;
                    PostRoundTimerText = postSec <= 10
                        ? $"<color=#FF4444>Next Round in {postSec}s</color>"
                        : $"Next Round in {postSec}s";
                    NotifyPropertyChanged(nameof(PostRoundTimerText));
                    break;

                case "MATCH_OVER":
                    bool byForfeit = (bool?)data["byForfeit"] ?? false;
                    if (byForfeit && _mapLauncher.IsCurrentlyInGameplay) _mapLauncher.ForceExitActiveMap();
                    if (IsRoundResultState && !byForfeit)
                    {
                        _pendingMatchOverData = data;
                        NotifyPropertyChanged(nameof(HasNextRoundTimer));
                    }
                    else ShowMatchOverScreen(data);
                    break;
            }
        }

        private IEnumerator AnimateHpLossCoroutine(int s1, int e1, int s2, int e2, int myDmg, int enemyDmg)
        {
            CanContinueRound = false;
            ContinueBtnText = "Draining Health...";
            _displayedP1Hp = s1; _displayedP2Hp = s2;
            RefreshUI();

            yield return new WaitForSeconds(0.6f);

            string mult = _currentMultiplier > 1 ? $" ({_currentMultiplier}x!)" : "!";
            P1DamageTaken = myDmg > 0 ? $"-{myDmg:N0} DMG{mult}" : "0 DMG (Won Round)";
            P2DamageTaken = enemyDmg > 0 ? $"-{enemyDmg:N0} DMG{mult}" : "0 DMG (Won Round)";

            for (float el = 0f; el < 1.8f; el += Time.deltaTime)
            {
                float ease = 1f - Mathf.Pow(1f - Mathf.Clamp01(el / 1.8f), 3f);
                _displayedP1Hp = Mathf.RoundToInt(Mathf.Lerp(s1, e1, ease));
                _displayedP2Hp = Mathf.RoundToInt(Mathf.Lerp(s2, e2, ease));
                RefreshHpBars();
                yield return null;
            }

            _displayedP1Hp = e1; _displayedP2Hp = e2;
            CanContinueRound = true;
            ContinueBtnText = (_pendingMatchOverData != null || e1 <= 0 || e2 <= 0) ? "View Final Match Summary" : "Next Round (Draft)";
            RefreshUI();
        }

        private void ShowMatchOverScreen(JObject data)
        {
            RankedMatchManager.IsInRankedMatch = false;
            bool won = (bool?)data["won"] ?? false;
            bool byForfeit = (bool?)data["byForfeit"] ?? false;
            int delta = (int?)data["eloChange"] ?? 0;
            _match.MyElo = (int?)data["newElo"] ?? _match.MyElo;

            MatchOutcomeTitle = won ? (byForfeit ? "VICTORY! (ENEMY LEFT)" : "VICTORY!") : (byForfeit ? "DEFEAT (ABANDONED)" : "DEFEAT");
            EloChangeText = delta >= 0 ? $"<color=#00FF66>+{delta} ELO</color>" : $"<color=#FF4444>{delta} ELO</color>";
            NewTotalEloText = $"New Rating: {_match.MyElo} ELO";
            P2FinalElo = $"Rating: {(int?)data["enemyNewElo"] ?? _match.EnemyElo} ELO";

            _p2OverImg?.SetImage(_match.EnemyPfp);
            _profileView.RefreshProfileUI();
            SetState(matchOver: true);
        }

        private void ApplyCardsFromServer(JArray cardsArray)
        {
            if (cardsArray == null || cardsArray.Count < 3) return;
            _currentCards = cardsArray.ToObject<List<MapCardData>>();

            Card1Title = _currentCards[0].Title; Card1Mapper = $"by {_currentCards[0].Mapper}"; Card1Stars = $"{_currentCards[0].Stars:F2} ★";
            Card2Title = _currentCards[1].Title; Card2Mapper = $"by {_currentCards[1].Mapper}"; Card2Stars = $"{_currentCards[1].Stars:F2} ★";
            Card3Title = _currentCards[2].Title; Card3Mapper = $"by {_currentCards[2].Mapper}"; Card3Stars = $"{_currentCards[2].Stars:F2} ★";

            _card1Img?.SetImage(_currentCards[0].CoverUrl);
            _card2Img?.SetImage(_currentCards[1].CoverUrl);
            _card3Img?.SetImage(_currentCards[2].CoverUrl);
            RefreshUI();
        }

        [UIAction("on-queue-click")]
        private async void OnQueueClick()
        {
            if (!_net.IsConnected) return;
            _isQueueing = !_isQueueing;
            QueueBtnText = _isQueueing ? "CANCEL QUEUE" : "FIND MATCH";
            RefreshUI();
            await _net.SendPacketAsync(_isQueueing ? "JOIN_QUEUE" : "LEAVE_QUEUE");
        }

        [UIAction("on-reconnect-click")]
        private async void OnReconnectClick()
        {
            RefreshUI();
            await _net.ConnectAsync();
        }

        [UIAction("on-ready-click")]
        private async void OnReadyClick()
        {
            CanClickReady = false; ReadyBtnText = "Waiting for opponent..."; RefreshUI();
            await _net.SendPacketAsync("PLAYER_READY");
        }

        [UIAction("on-reroll-click")]
        private async void OnRerollClick()
        {
            if (!IsMyTurn || !_canReroll) return;
            _canReroll = false; RerollBtnText = "Reroll Used"; RefreshUI();
            await _net.SendPacketAsync("REROLL_CARDS");
        }

        [UIAction("pick-card-1")] private void PickCard1() => OnMapSelected(0);
        [UIAction("pick-card-2")] private void PickCard2() => OnMapSelected(1);
        [UIAction("pick-card-3")] private void PickCard3() => OnMapSelected(2);

        private async void OnMapSelected(int index)
        {
            if (IsMyTurn) await _net.SendPacketAsync("PICK_CARD", new { cardIndex = index });
        }

        [UIAction("on-continue-after-round")]
        private async void OnContinueAfterRound()
        {
            if (_pendingMatchOverData != null)
            {
                var data = _pendingMatchOverData;
                _pendingMatchOverData = null;
                ShowMatchOverScreen(data);
            }
            else
            {
                CanContinueRound = false; ContinueBtnText = "Waiting for opponent..."; RefreshUI();
                await _net.SendPacketAsync("READY_FOR_NEXT_ROUND");
            }
        }

        [UIAction("on-return-queue-click")]
        private void OnReturnQueueClick()
        {
            HandleConnectionChanged(_net.IsConnected);
            SetState(idle: true);
            OnMatchConcludedEvent?.Invoke();
        }
    }
}