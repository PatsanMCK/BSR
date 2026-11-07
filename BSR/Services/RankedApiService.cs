using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BSR.Services
{
    public class MapCardData
    {
        public string Hash { get; set; }
        public string Title { get; set; }
        public string Mapper { get; set; } = "Unknown Mapper";
        public string CoverUrl { get; set; }
        public string DownloadUrl { get; set; }
        public double Stars { get; set; }
        public string Difficulty { get; set; }
        public string Characteristic { get; set; }
    }

    public class RankedApiService
    {
        private readonly HttpClient _http = new HttpClient();

        public RankedApiService()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("BSRanked/1.0.0");
        }

        public async Task<(string name, string avatarUrl, double avgStars)> GetScoreSaberStatsAsync(string steamId)
        {
            try
            {
                string profileJson = await _http.GetStringAsync($"https://scoresaber.com/api/player/{steamId}/basic");
                var profile = JObject.Parse(profileJson);
                string name = profile["name"]?.ToString() ?? "Player";
                string avatar = profile["profilePicture"]?.ToString() ?? "https://scoresaber.com/imports/images/usr-avatars/default.jpg";

                string scoresJson = await _http.GetStringAsync($"https://scoresaber.com/api/player/{steamId}/scores?sort=top&page=1");
                var scores = JObject.Parse(scoresJson)["playerScores"] as JArray;

                double avgStars = scores != null
                    ? scores.Select(s => (double?)s["leaderboard"]?["stars"] ?? 0)
                            .Where(st => st > 0)
                            .DefaultIfEmpty(5.5)
                            .Average()
                    : 5.5;

                return (name, avatar, avgStars);
            }
            catch
            {
                return ("LocalPlayer", "https://scoresaber.com/imports/images/usr-avatars/default.jpg", 6.0);
            }
        }
    }
}