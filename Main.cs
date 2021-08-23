using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SRXDTimingBar {
    [BepInPlugin("TimingBar", "TimingBar", "1.0.0.0")]
    public class Main : BasePlugin {
        public static ManualLogSource Logger { get; private set; }
        public static ConfigFile ConfigFile { get; private set; }
        public static ConfigEntry<float> BarPositionX { get; private set; }
        public static ConfigEntry<float> BarPositionY { get; private set; }
        public static ConfigEntry<bool> OrientVertically { get; private set; }
        public static ConfigEntry<int> TimingSamples { get; private set; }
        public static ConfigEntry<float> MedianSmoothing { get; private set; }
        
        public override void Load() {
            Logger = Log;
            BarPositionX = Config.Bind("Transform", "BarPositionX", 0f, "The X position of the timing bar. Recommended to keep this value between -0.5 and 0.5");
            BarPositionY = Config.Bind("Transform", "BarPositionY", -0.35f, "The Y position of the timing bar. Recommended to keep this value between -0.5 and 0.5");
            OrientVertically = Config.Bind("Transform", "OrientVertically", false, "Orient the timing bar vertically instead of horizontally");
            TimingSamples = Config.Bind("Data", "TimingSamples", 16, "The maximum number of timing samples to display on the bar at once");
            MedianSmoothing = Config.Bind("Data", "MedianSmoothing", 0.1f, "The amount of smoothing to apply to the movement of the median pointer. A value of 0 will make it jump instantaneously");
            ConfigFile = Config;

            var harmony = new Harmony("TimingBar");
            
            harmony.PatchAll(typeof(Mod));
        }
    }

    public class Mod {
        private enum Layer {
            GoodBar,
            PerfectBar,
            ZeroLine,
            TimingTick,
            MedianPointer
        }
        
        private static readonly float BAR_WIDTH = 1.6f;
        private static readonly float BAR_HEIGHT = 0.04f;
        private static float TICK_SPAN = 1.25f;
        
        private static bool rootCreated;
        private static bool playing;
        private static bool pendingBeat;
        private static int tickCount;
        private static int currentTick;
        private static float beatOffset;
        private static float targetMedian;
        private static float medianPointerX;
        private static float medianPointerY;
        private static float medianSmoothing;
        private static Transform root;
        private static Sprite rectSprite;
        private static GameObject[] tickPool;
        private static Transform medianPointer;
        private static List<KeyValuePair<int, float>> timingHistory;

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
            
            if (rootCreated) {
                root.gameObject.SetActive(true);

                foreach (var tick in tickPool)
                    tick.SetActive(false);

                targetMedian = 0f;
                medianPointerX = 0f;
                medianPointer.localPosition = new Vector3(0f, medianPointerY, 0f);
                timingHistory.Clear();
                currentTick = 0;

                return;
            }
            
            root = new GameObject().transform;
            root.parent = __instance.cameraContainerTransform.GetComponentInChildren<Camera>().transform;
            root.localPosition = new Vector3(Main.BarPositionX.Value, Main.BarPositionY.Value, 0.5f);
            
            if (Main.OrientVertically.Value)
                root.localRotation = Quaternion.Euler(0f, 0f, 90f);
            else
                root.localRotation = Quaternion.identity;

            root.localScale = Vector3.one;
            rectSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 4f);
            CreateRectangle(Vector3.zero, new Vector3(BAR_WIDTH, BAR_HEIGHT, 1f), Color.yellow * 0.8f, Layer.GoodBar);
            CreateRectangle(Vector3.zero, new Vector3(0.5f * BAR_WIDTH, BAR_HEIGHT, 1f), Color.cyan * 0.8f, Layer.PerfectBar);
            CreateRectangle(Vector3.zero, new Vector3(0.5f * BAR_HEIGHT, 3f * BAR_HEIGHT, 1f), Color.white, Layer.ZeroLine);
            tickCount = Main.TimingSamples.Value;
            tickPool = new GameObject[tickCount];
            medianSmoothing = Main.MedianSmoothing.Value;

            for (int i = 0; i < tickCount; i++) {
                var newTick = CreateRectangle(Vector3.zero, new Vector3(0.35f * BAR_HEIGHT, 2f * BAR_HEIGHT, 1f), new Color(1f, 1f, 1f, 0.5f), Layer.TimingTick);
                
                newTick.SetActive(false);
                tickPool[i] = newTick;
            }

            medianPointerY = 0.65f * BAR_HEIGHT;
            medianPointer = CreateRectangle(new Vector3(0f, medianPointerY, 0f), new Vector3(BAR_HEIGHT, BAR_HEIGHT, 1f), Color.white, Layer.MedianPointer).transform;
            timingHistory = new List<KeyValuePair<int, float>>();
            rootCreated = true;
        }

        [HarmonyPatch(typeof(Track), nameof(Track.CompleteSong)), HarmonyPostfix]
        private static void Track_CompleteSong_Postfix() {
            if (!rootCreated)
                return;
            
            root.gameObject.SetActive(false);
            playing = false;
        }
        
        [HarmonyPatch(typeof(Track), nameof(Track.FailSong)), HarmonyPostfix]
        private static void Track_FailSong_Postfix() {
            if (!rootCreated)
                return;
            
            root.gameObject.SetActive(false);
            playing = false;
        }

        [HarmonyPatch(typeof(XDPauseMenu), nameof(XDPauseMenu.ExitButtonPressed)), HarmonyPostfix]
        private static void XDPauseMenu_ExitButtonPressed_Postfix() {
            if (!rootCreated)
                return;
            
            root.gameObject.SetActive(false);
            playing = false;
        }
        
        [HarmonyPatch(typeof(GameplayVariables), nameof(GameplayVariables.GetTimingAccuracy)), HarmonyPostfix]
        private static void GameplayVariables_GetTimingAccuracy_Postfix(float timeOffset) {
            if (!rootCreated)
                return;
            
            PlaceTickAtTime(timeOffset);
        }

        [HarmonyPatch(typeof(GameplayVariables), nameof(GameplayVariables.GetTimingAccuracyForBeat)), HarmonyPostfix]
        private static void GameplayVariables_GetTimingAccuracyForBeat_Postfix(float timeOffset) {
            if (!rootCreated)
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

        private static void PlaceTickAtTime(float timeOffset) {
            var tick = tickPool[currentTick];
            
            if (timeOffset < -0.1f || timeOffset > 0.1f)
                tick.SetActive(false);
            else {
                tick.SetActive(true);
                tick.transform.localPosition = new Vector3(TICK_SPAN * BAR_WIDTH * timeOffset, 0f, 0f);
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
        
        private static GameObject CreateRectangle(Vector3 position, Vector3 scale, Color color, Layer layer) {
            var rectangle = new GameObject();

            rectangle.transform.parent = root;
            rectangle.transform.localPosition = position;
            rectangle.transform.localRotation = Quaternion.identity;
            rectangle.transform.localScale = scale;
            
            var spriteRenderer = rectangle.AddComponent<SpriteRenderer>();
            
            spriteRenderer.sprite = rectSprite;
            spriteRenderer.color = color;
            spriteRenderer.sortingOrder = (int) layer;

            return rectangle;
        }
    }
}