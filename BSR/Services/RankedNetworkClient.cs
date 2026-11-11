using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BS_Utils.Gameplay;
using BSR.Configuration;
using BSR.Managers;
using IPA.Utilities.Async;
using Newtonsoft.Json.Linq;
using Zenject;

namespace BSR.Services
{
    public class RankedNetworkClient : IInitializable, IDisposable
    {
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly RankedMatchManager _matchManager;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _connectLock = new SemaphoreSlim(1, 1);

        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;

        public event Action<bool> OnConnectionStatusChanged;
        public event Action<string, JObject> OnPacketReceived;

        public RankedNetworkClient(RankedMatchManager matchManager)
        {
            _matchManager = matchManager;
        }

        public void Initialize()
        {
            _ = ConnectAsync();
        }

        public async Task ConnectAsync()
        {
            await _connectLock.WaitAsync();
            try
            {
                if (IsConnected) return;

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                _ws?.Dispose();
                _ws = new ClientWebSocket();

                string serverUrl = PluginConfig.Instance?.ServerUrl ?? "ws://ranked.beatsabercis.dpdns.org:8080";
                Plugin.Log.Info($"[BS Ranked] Connecting to {serverUrl}...");

                await _ws.ConnectAsync(new Uri(serverUrl), _cts.Token);
                Plugin.Log.Info("[BS Ranked] Connected to server!");
                OnConnectionStatusChanged?.Invoke(true);

                var userInfo = await GetUserInfo.GetUserAsync();
                if (userInfo != null && !string.IsNullOrEmpty(userInfo.platformUserId))
                {
                    _matchManager.MySteamId = userInfo.platformUserId;
                    _matchManager.MyName = userInfo.userName ?? "Player";
                }
                Plugin.Log.Info($"[BS Ranked] Authenticating with SteamID: {_matchManager.MySteamId}");

                _ = ReceiveLoopAsync(_cts.Token);

                await SendPacketAsync("AUTH", new { steamId = _matchManager.MySteamId });
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[BS Ranked] Could not connect to server: {ex.Message}");
                OnConnectionStatusChanged?.Invoke(false);
            }
            finally
            {
                _connectLock.Release();
            }
        }

        public async Task SendPacketAsync(string type, object data = null)
        {
            if (!IsConnected) return;

            await _sendLock.WaitAsync();
            try
            {
                if (!IsConnected) return;

                string payload = JObject.FromObject(new { type, data = data ?? new { } }).ToString();
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[BS Ranked] Failed sending packet '{type}': {ex.Message}");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[8192];
            try
            {
                while (_ws != null && _ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                            return;

                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    try
                    {
                        string json = Encoding.UTF8.GetString(ms.ToArray());
                        var msg = JObject.Parse(json);
                        string type = msg["type"]?.ToString();
                        var data = msg["data"] as JObject;

                        if (type == "PROFILE_DATA" && data != null)
                        {
                            _matchManager.MyName = data["name"]?.ToString() ?? _matchManager.MyName;
                            _matchManager.MyPfp = data["pfp"]?.ToString() ?? _matchManager.MyPfp;
                            _matchManager.MyElo = (int?)data["elo"] ?? _matchManager.MyElo;
                            _matchManager.MyAvgStars = (double?)data["avgStars"] ?? _matchManager.MyAvgStars;
                            _matchManager.MyLeaderboardRank = (int?)data["leaderboardRank"] ?? _matchManager.MyLeaderboardRank;
                            _matchManager.MyWins = (int?)data["wins"] ?? _matchManager.MyWins;
                            _matchManager.MyLosses = (int?)data["losses"] ?? _matchManager.MyLosses;
                            _matchManager.MyColorTag = data["colorTag"]?.ToString() ?? _matchManager.MyColorTag;
                            Plugin.Log.Info($"[BS Ranked] Loaded Server Profile: {_matchManager.MyName} | #{_matchManager.MyLeaderboardRank} | ELO: {_matchManager.MyElo} | ColorTag: {_matchManager.MyColorTag}");
                        }

                        _ = UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                        {
                            OnPacketReceived?.Invoke(type, data);
                        });
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warn($"[BS Ranked] Error parsing packet: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Plugin.Log.Warn($"[BS Ranked] Disconnected from server: {ex.Message}");
            }
            finally
            {
                _ = UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                {
                    OnConnectionStatusChanged?.Invoke(false);
                });
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _ws?.Dispose();
            _sendLock?.Dispose();
            _connectLock?.Dispose();
        }
    }
}