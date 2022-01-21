using System;
using System.Collections.Generic;
using SRXDScoreMod;

namespace SRXDTimingBar {
    public static class ScoreModWrapper {
        public static void SubscribeToOnScoreSystemChanged(Action action) => ScoreMod.OnScoreSystemChanged += _ => action.Invoke();
        
        public static TimingWindow[] GetTimingWindows() {
            var fromTimingWindows = ScoreMod.CurrentScoreSystem.TimingWindowsForDisplay;
            var toTimingWindows = new List<TimingWindow>();

            for (int i = 0; i < fromTimingWindows.Length; i++) {
                var fromWindow = fromTimingWindows[i];
                float upperBound = fromWindow.UpperBound;
                
                if (upperBound <= -Main.Bounds)
                    continue;

                if (upperBound > Main.Bounds)
                    upperBound = Main.Bounds;
                
                float lowerBound;

                if (i > 0)
                    lowerBound = fromTimingWindows[i - 1].UpperBound;
                else
                    lowerBound = -Main.Bounds;
                
                if (lowerBound >= Main.Bounds)
                    break;

                if (lowerBound < -Main.Bounds)
                    lowerBound = Main.Bounds;
                
                toTimingWindows.Add(new TimingWindow(fromWindow.TimingAccuracy.Color, lowerBound, upperBound));
            }

            return toTimingWindows.ToArray();
        }
    }
}