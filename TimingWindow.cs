using UnityEngine;

namespace SRXDTimingBar {
    public readonly struct TimingWindow {
        public Color Color { get; }
        public float LowerBound { get; }
        public float UpperBound { get; }

        public TimingWindow(Color color, float lowerBound, float upperBound) {
            Color = color;
            LowerBound = lowerBound;
            UpperBound = upperBound;
        }
    }
}