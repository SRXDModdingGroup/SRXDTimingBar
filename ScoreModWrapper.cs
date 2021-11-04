using System;
using System.IO;

namespace SRXDTimingBar
{
    public static class ScoreModWrapper
    {
        public static bool ShowModdedScore => showModdedScore();
        
        private static Func<bool> showModdedScore;

        public static void Initialize()
        {
            try
            {
                GetScoreModFunctions();
                Main.Logger.LogMessage("Found ScoreMod");
            }
            catch (Exception)
            {
                showModdedScore = () => false;
                Main.Logger.LogMessage("ScoreMod not found");
            }
        }

        private static void GetScoreModFunctions()
        {
            _ = ScoreMod.ModState.ShowModdedScore;
            showModdedScore = () => ScoreMod.ModState.ShowModdedScore;
        }
    }
}