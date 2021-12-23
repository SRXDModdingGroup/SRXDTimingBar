using BepInEx.Bootstrap;

namespace SRXDTimingBar {
    public static class ScoreModWrapper {
        public static bool ShowModdedScore => scoreModLoaded && showModdedScore;
        private static bool showModdedScore => ScoreMod.ModState.ShowModdedScore;

        private static bool scoreModLoaded;

        public static void Initialize() {
            scoreModLoaded = Chainloader.PluginInfos.ContainsKey("SRXD.ScoreMod");
            
            if (scoreModLoaded)
                Main.Logger.LogMessage("Found ScoreMod");
            else
                Main.Logger.LogMessage("ScoreMod not found");
        }
    }
}