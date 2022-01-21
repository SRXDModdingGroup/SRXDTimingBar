using System;
using SRXDScoreMod;

namespace SRXDTimingBar {
    public static class ScoreModWrapper {
        public static void SubscribeToOnScoreSystemChanged(Action action) => ScoreMod.OnScoreSystemChanged += _ => action.Invoke();
        
        public static TimingWindow[] GetTimingWindows() {
            var fromTimingWindows = ScoreMod.CurrentScoreSystem.TimingWindowsForDisplay;
            var toTimingWindows = new TimingWindow[fromTimingWindows.Length];

            for (int i = 0; i < toTimingWindows.Length; i++) {
                var fromWindow = fromTimingWindows[i];
                float lowerBound;

                if (i > 0)
                    lowerBound = fromTimingWindows[i - 1].UpperBound;
                else
                    lowerBound = -0.13f;
                
                toTimingWindows[i] = new TimingWindow(fromWindow.TimingAccuracy.Color, lowerBound, fromWindow.UpperBound);
            }

            return toTimingWindows;
        }
    }
}