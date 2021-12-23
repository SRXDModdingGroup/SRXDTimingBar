using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SRXDTimingBar {
    [BepInPlugin("SRXD.TimingBar", "TimingBar", "1.1.0.0")]
    [BepInDependency("SRXD.ScoreMod", BepInDependency.DependencyFlags.SoftDependency)]
    public class Main : BaseUnityPlugin {
        public new static ManualLogSource Logger { get; private set; }
        public static ConfigFile ConfigFile { get; private set; }
        public static ConfigEntry<float> BarPositionX { get; private set; }
        public static ConfigEntry<float> BarPositionY { get; private set; }
        public static ConfigEntry<bool> OrientVertically { get; private set; }
        public static ConfigEntry<bool> ColoredTicks { get; private set; }
        public static ConfigEntry<int> TimingSamples { get; private set; }
        public static ConfigEntry<float> MedianSmoothing { get; private set; }
        
        private void Awake() {
            Logger = base.Logger;
            BarPositionX = Config.Bind("Transform", "BarPositionX", 0f, "The X position of the timing bar. Recommended to keep this value between -0.5 and 0.5");
            BarPositionY = Config.Bind("Transform", "BarPositionY", -0.35f, "The Y position of the timing bar. Recommended to keep this value between -0.5 and 0.5");
            OrientVertically = Config.Bind("Transform", "OrientVertically", false, "Orient the timing bar vertically instead of horizontally");
            ColoredTicks = Config.Bind("Style", "ColoredTicks", true, "Color timing ticks based on the window they land in");
            TimingSamples = Config.Bind("Data", "TimingSamples", 16, "The maximum number of timing samples to display on the bar at once");
            MedianSmoothing = Config.Bind("Data", "MedianSmoothing", 0.1f, "The amount of smoothing to apply to the movement of the median pointer. A value of 0 will make it jump instantaneously");
            ConfigFile = Config;

            var harmony = new Harmony("TimingBar");
            
            harmony.PatchAll(typeof(Mod));
            ScoreModWrapper.Initialize();
        }
    }

    public class Mod {
        private enum Layer {
            OkayBar,
            GoodBar,
            GreatBar,
            PerfectBar,
            TimingTick,
            ZeroLine,
            MedianPointer
        }
        
        private static readonly float BAR_WIDTH = 1.6f;
        private static readonly float BAR_HEIGHT = 0.04f;
        private static readonly float TICK_HEIGHT = 2.5f;
        private static readonly float TICK_SPAN = 1.25f;
        private static readonly float TICK_OPACITY = 0.5f;
        private static readonly float TICK_LIGHTNESS = 0.1f;
        private static readonly float SEGMENT_LIGHTNESS = 0.75f;
        private static bool initialized;
        private static bool playing;
        private static bool pendingBeat;
        private static bool coloredTicks;
        private static int tickCount;
        private static int currentTick;
        private static float beatOffset;
        private static float targetMedian;
        private static float medianPointerX;
        private static float medianPointerY;
        private static float medianSmoothing;
        private static Transform root;
        private static Sprite rectSprite;
        private static List<GameObject> segments;
        private static SpriteRenderer[] tickPool;
        private static Transform medianPointer;
        private static List<KeyValuePair<int, float>> timingHistory;
        private static List<KeyValuePair<float, Color>> windows;

        [HarmonyPatch(typeof(Game), nameof(Game.Update)), HarmonyPostfix]
        private static void Game_Update_Postfix() {
            if (!playing)
                return;

            if (medianSmoothing > 0f)
                medianPointerX = Mathf.Lerp(TICK_SPAN * BAR_WIDTH * targetMedian, medianPointerX, Mathf.Exp(-UnityEngine.Time.unscaledDeltaTime / medianSmoothing));
            else
                medianPointerX = TICK_SPAN * BAR_WIDTH * targetMedian;

            medianPointer.localPosition = new Vector3(medianPointerX, medianPointerY, 0f);

            if (Track.Instance.IsInEditMode || !Input.GetKey(KeyCode.LeftControl))
                return;
            
            var shift = Vector3.zero;

            if (Input.GetKeyDown(KeyCode.LeftArrow))
                shift += Vector3.left;
            
            if (Input.GetKeyDown(KeyCode.RightArrow))
                shift += Vector3.right;
            
            if (Input.GetKeyDown(KeyCode.UpArrow))
                shift += Vector3.up;

            if (Input.GetKeyDown(KeyCode.DownArrow))
                shift += Vector3.down;

            if (shift == Vector3.zero)
                return;
            
            if (Input.GetKey(KeyCode.LeftShift))
                root.localPosition += 0.01f * shift;
            else
                root.localPosition += 0.1f * shift;

            Main.BarPositionX.Value = root.localPosition.x;
            Main.BarPositionY.Value = root.localPosition.y;
            Main.ConfigFile.Save();
        }
        
        [HarmonyPatch(typeof(Track), nameof(Track.PlayTrack)), HarmonyPostfix]
        private static void Track_PlayTrack_Postfix(Track __instance) {
            playing = true;
            
            if (initialized) {
                root.gameObject.SetActive(true);

                foreach (var tick in tickPool)
                    tick.gameObject.SetActive(false);

                targetMedian = 0f;
                medianPointerX = 0f;
                medianPointer.localPosition = new Vector3(0f, medianPointerY, 0f);
                timingHistory.Clear();
                currentTick = 0;
            }
            else
                Initialize(__instance);
            
            CreateTimingBar();
        }

        [HarmonyPatch(typeof(Track), nameof(Track.CompleteSong)), HarmonyPostfix]
        private static void Track_CompleteSong_Postfix() {
            if (!initialized)
                return;
            
            root.gameObject.SetActive(false);
            playing = false;
        }
        
        [HarmonyPatch(typeof(Track), nameof(Track.FailSong)), HarmonyPostfix]
        private static void Track_FailSong_Postfix() {
            if (!initialized)
                return;
            
            root.gameObject.SetActive(false);
            playing = false;
        }

        [HarmonyPatch(typeof(XDPauseMenu), nameof(XDPauseMenu.ExitButtonPressed)), HarmonyPostfix]
        private static void XDPauseMenu_ExitButtonPressed_Postfix() {
            if (!initialized)
                return;
            
            root.gameObject.SetActive(false);
            playing = false;
        }
        
        [HarmonyPatch(typeof(GameplayVariables), nameof(GameplayVariables.GetTimingAccuracy)), HarmonyPostfix]
        private static void GameplayVariables_GetTimingAccuracy_Postfix(float timeOffset) {
            if (!initialized)
                return;
            
            PlaceTickAtTime(timeOffset);
        }

        [HarmonyPatch(typeof(GameplayVariables), nameof(GameplayVariables.GetTimingAccuracyForBeat)), HarmonyPostfix]
        private static void GameplayVariables_GetTimingAccuracyForBeat_Postfix(float timeOffset) {
            if (!initialized)
                return;

            pendingBeat = true;
            beatOffset = timeOffset;
        }

        [HarmonyPatch(typeof(PlayState.ScoreState), nameof(PlayState.ScoreState.AddScore)), HarmonyPostfix]
        private static void ScoreState_AddScore_Postfix(int amount) {
            if (pendingBeat && amount == 16)
                PlaceTickAtTime(beatOffset);

            pendingBeat = false;
        }

        private static void Initialize(Track track) {
            root = new GameObject().transform;
            root.parent = track.cameraContainerTransform.GetComponentInChildren<Camera>().transform;
            root.localPosition = new Vector3(Main.BarPositionX.Value, Main.BarPositionY.Value, 0.5f);
            
            if (Main.OrientVertically.Value)
                root.localRotation = Quaternion.Euler(0f, 0f, 90f);
            else
                root.localRotation = Quaternion.identity;

            root.localScale = Vector3.one;
            rectSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 4f);
            tickCount = Main.TimingSamples.Value;
            tickPool = new SpriteRenderer[tickCount];
            medianSmoothing = Main.MedianSmoothing.Value;
            coloredTicks = Main.ColoredTicks.Value;

            for (int i = 0; i < tickCount; i++) {
                var newTick = CreateRectangle(Vector3.zero, new Vector3(0.35f * BAR_HEIGHT, TICK_HEIGHT * BAR_HEIGHT, 1f), new Color(1f, 1f, 1f, TICK_OPACITY), Layer.TimingTick);
                
                newTick.gameObject.SetActive(false);
                tickPool[i] = newTick;
            }

            medianPointerY = 0.75f * BAR_HEIGHT;
            medianPointer = CreateRectangle(new Vector3(0f, medianPointerY, 0f), new Vector3(BAR_HEIGHT, BAR_HEIGHT, 1f), Color.white, Layer.MedianPointer).transform;
            timingHistory = new List<KeyValuePair<int, float>>();
            windows = new List<KeyValuePair<float, Color>>();
            segments = new List<GameObject>();
            CreateRectangle(Vector3.zero, new Vector3(0.35f * BAR_HEIGHT, 3.5f * BAR_HEIGHT, 1f), Color.white, Layer.ZeroLine);
            initialized = true;
        }

        private static void CreateTimingBar() {
            if (!initialized)
                return;

            foreach (var segment in segments)
                GameObject.Destroy(segment);
            
            windows.Clear();
            segments.Clear();

            if (ScoreModWrapper.ShowModdedScore)
                CreateModTimingBar();
            else {
                AddSegment(0.05f, Color.cyan, Layer.PerfectBar);
                AddSegment(0.1f, Color.yellow, Layer.GoodBar);
            }
        }

        private static void CreateModTimingBar() {
            var profileWindows = ScoreMod.ModState.CurrentContainer.Profile.PressNoteWindows;
            var previousLayer = Layer.ZeroLine;

            for (int i = 0; i < profileWindows.Count; i++) {
                var window = profileWindows[i];
                float upperBound;

                if (i == profileWindows.Count - 1)
                    upperBound = 0.1f;
                else
                    upperBound = profileWindows[i + 1].LowerBound;
                
                Color color;
                Layer layer;

                switch (window.Accuracy) {
                    case ScoreMod.Accuracy.Perfect:
                        color = Color.cyan;
                        layer = Layer.PerfectBar;
                        
                        break;
                    case ScoreMod.Accuracy.Great:
                        color = Color.green;
                        layer = Layer.GreatBar;
                        
                        break;
                    case ScoreMod.Accuracy.Good:
                        color = Color.yellow;
                        layer = Layer.GoodBar;
                        
                        break;
                    default:
                        color = Color.gray;
                        layer = Layer.OkayBar;
                        
                        break;
                }
                
                if (layer == previousLayer)
                    continue;

                previousLayer = layer;
                AddSegment(upperBound, color, layer);
            }
        }

        private static void AddSegment(float window, Color color, Layer layer) {
            segments.Add(CreateRectangle(Vector3.zero, new Vector3(10f * window * BAR_WIDTH, BAR_HEIGHT, 1f), SEGMENT_LIGHTNESS * color, layer).gameObject);

            color = Color.Lerp(color, Color.white, TICK_LIGHTNESS);
            color.a = TICK_OPACITY;
            
            windows.Add(new KeyValuePair<float, Color>(window, color));
        }

        private static void PlaceTickAtTime(float timeOffset) {
            var tick = tickPool[currentTick];
            
            if (timeOffset < -0.1f || timeOffset > 0.1f)
                tick.gameObject.SetActive(false);
            else {
                tick.gameObject.SetActive(true);
                tick.transform.localPosition = new Vector3(TICK_SPAN * BAR_WIDTH * timeOffset, 0f, 0f);

                if (coloredTicks) {
                    var color = Color.white;

                    float absOffset = Mathf.Abs(timeOffset);

                    foreach (var pair in windows) {
                        if (absOffset > pair.Key)
                            continue;

                        color = pair.Value;

                        break;
                    }

                    tick.color = color;
                }
            }
            
            if (timingHistory.Count >= tickCount - 1) {
                for (int i = 0; i < timingHistory.Count; i++) {
                    if (timingHistory[i].Key != currentTick)
                        continue;

                    timingHistory.RemoveAt(i);

                    break;
                }
            }

            bool added = false;

            for (int i = 0; i < timingHistory.Count; i++) {
                if (timeOffset > timingHistory[i].Value)
                    continue;
                
                timingHistory.Insert(i, new KeyValuePair<int, float>(currentTick, timeOffset));
                added = true;

                break;
            }
            
            if (!added)
                timingHistory.Add(new KeyValuePair<int, float>(currentTick, timeOffset));

            targetMedian = timingHistory[timingHistory.Count / 2].Value;

            if (timingHistory.Count % 2 == 0)
                targetMedian = 0.5f * (targetMedian + timingHistory[timingHistory.Count / 2 - 1].Value);

            if (targetMedian < -0.1f)
                targetMedian = 0.1f;
            else if (targetMedian > 0.1f)
                targetMedian = 0.1f;

            currentTick++;

            if (currentTick == tickCount)
                currentTick = 0;
        }

        private static SpriteRenderer CreateRectangle(Vector3 position, Vector3 scale, Color color, Layer layer) {
            var rectangle = new GameObject();

            rectangle.transform.parent = root;
            rectangle.transform.localPosition = position;
            rectangle.transform.localRotation = Quaternion.identity;
            rectangle.transform.localScale = scale;
            
            var renderer = rectangle.AddComponent<SpriteRenderer>();
            
            renderer.sprite = rectSprite;
            renderer.color = color;
            renderer.sortingOrder = (int) layer;

            return renderer;
        }
    }
}