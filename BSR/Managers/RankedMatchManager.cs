namespace BSR.Managers
{
    public class RankedMatchManager
    {
        public const int MaxHp = 100000;
        public static bool IsInRankedMatch { get; set; } = false;

        // Player 1 (You)
        public string MySteamId { get; set; } = Plugin.LocalSteamId;
        public string MyName { get; set; } = Plugin.LocalUserName;
        public string MyPfp { get; set; } = "https://scoresaber.com/imports/images/usr-avatars/default.jpg";
        public int MyElo { get; set; } = 1000;
        public int MyLeaderboardRank { get; set; } = 1;
        public int MyWins { get; set; } = 0;
        public int MyLosses { get; set; } = 0;
        public double MyAvgStars { get; set; } = 6.0;
        public int MyHp { get; set; } = MaxHp;

        // Player 2 (Opponent)
        public string EnemyName { get; set; } = "EnemyPlayer";
        public string EnemyPfp { get; set; } = "https://scoresaber.com/imports/images/usr-avatars/default.jpg";
        public int EnemyElo { get; set; } = 1000;
        public double EnemyAvgStars { get; set; } = 6.0;
        public int EnemyHp { get; set; } = MaxHp;

        public double TargetMatchStars => (MyAvgStars + EnemyAvgStars) / 2.0;

        public (string tier, string colorHex) GetRankDetails(int elo)
        {
            if (elo >= 2200) return ("X", "#FF0055");
            if (elo >= 2000) return ("U", "#E02424");
            if (elo >= 1850) return ("SS", "#FF8800");
            if (elo >= 1750) return ("S+", "#FFAA00");
            if (elo >= 1650) return ("S", "#FFCC00");
            if (elo >= 1550) return ("S-", "#E6D800");
            if (elo >= 1450) return ("A+", "#00E676");
            if (elo >= 1350) return ("A", "#00C853");
            if (elo >= 1250) return ("A-", "#26A69A");
            if (elo >= 1150) return ("B+", "#00E5FF");
            if (elo >= 1050) return ("B", "#2979FF");
            if (elo >= 950)  return ("B-", "#1E88E5");
            if (elo >= 850)  return ("C+", "#BA68C8");
            if (elo >= 750)  return ("C", "#AB47BC");
            if (elo >= 650)  return ("C-", "#7B1FA2");
            if (elo >= 500)  return ("D+", "#90A4AE");
            return ("D", "#607D8B");
        }

        public string GetRankTier(int elo) => GetRankDetails(elo).tier;
    }
}