using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Draws an <see cref="AlpsClipStrip"/> on every ALPS clip in the Timeline window: a
    /// color band along the bottom edge and a thin line wherever the clip's phase starts a
    /// new cycle, the way StageLightManeuver showed its clips.
    ///
    /// Strips are cached per clip and rebuilt when the clip's timing, its track tempo, its
    /// fixture count or its effects change. Any ALPS edit bumps <see cref="AlpsPreviewDriver.Revision"/>
    /// without saying which clip it touched, so after one the effects are compared as JSON
    /// and only clips that really changed are sampled again.
    /// </summary>
    [CustomTimelineEditor(typeof(AlpsTimelineClip))]
    public class AlpsTimelineClipEditor : ClipEditor
    {
        private static readonly Color CycleLineColor = new Color(0f, 1f, 0.71f, 0.2f);
        private static readonly Color BandBacking = new Color(0f, 0f, 0f, 0.5f);
        private const float LaneHeightRatio = 0.12f;
        private const float MinLaneHeight = 3f;
        private const float MinCycleLineSpacing = 4f;

        private struct Key
        {
            public AlpsClipEffectSet set;
            public float start;
            public float end;
            public float bpm;
            public float beatOrigin;
            public int fixtureCount;
            public int seed;
        }

        private sealed class Entry
        {
            public Key key;
            public int revision;
            public string content;
            public AlpsClipStrip strip;
            public Texture2D texture;
        }

        private static readonly Dictionary<AlpsTimelineClip, Entry> Cache = new Dictionary<AlpsTimelineClip, Entry>();

        [InitializeOnLoadMethod]
        private static void RegisterCleanup()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearCache;
        }

        public override void DrawBackground(TimelineClip clip, ClipBackgroundRegion region)
        {
            base.DrawBackground(clip, region);

            var asset = clip.asset as AlpsTimelineClip;
            var visible = region.endTime - region.startTime;
            if (asset == null || visible <= 0.0 || clip.duration <= 0.0 || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            var entry = GetEntry(clip, asset);
            var rect = region.position;
            var pixelsPerSecond = rect.width / (float)visible;

            float XAt(double local)
            {
                return rect.x + (float)(local - region.startTime) * pixelsPerSecond;
            }

            var strip = entry.strip;
            if (strip.secondsPerCycle * pixelsPerSecond >= MinCycleLineSpacing)
            {
                foreach (var time in strip.cycleTimes)
                {
                    if (time < region.startTime || time > region.endTime)
                    {
                        continue;
                    }

                    EditorGUI.DrawRect(new Rect(Mathf.Round(XAt(time)), rect.y, 1f, rect.height), CycleLineColor);
                }
            }

            if (entry.texture == null)
            {
                return;
            }

            var laneHeight = Mathf.Max(MinLaneHeight, rect.height * LaneHeightRatio);
            var bandHeight = Mathf.Min(rect.height, laneHeight * strip.lanes);
            var band = new Rect(rect.x, rect.yMax - bandHeight, rect.width, bandHeight);
            var u0 = (float)(region.startTime / clip.duration);
            var u1 = (float)(region.endTime / clip.duration);

            // Dark behind the band, so a dim or blacked out stretch reads as dark.
            EditorGUI.DrawRect(band, BandBacking);
            GUI.DrawTextureWithTexCoords(band, entry.texture, new Rect(u0, 0f, u1 - u0, 1f), true);
        }

        private static Entry GetEntry(TimelineClip clip, AlpsTimelineClip asset)
        {
            var key = MakeKey(clip, asset);
            var revision = AlpsPreviewDriver.Revision;
            Cache.TryGetValue(asset, out var entry);
            if (entry != null && Same(entry.key, key))
            {
                if (entry.revision == revision)
                {
                    return entry;
                }

                var content = JsonUtility.ToJson(key.set);
                entry.revision = revision;
                if (content == entry.content)
                {
                    return entry;
                }

                entry.content = content;
            }
            else
            {
                if (entry == null)
                {
                    PurgeDestroyedClips();
                    entry = new Entry();
                    Cache.Add(asset, entry);
                }

                entry.revision = revision;
                entry.content = JsonUtility.ToJson(key.set);
            }

            entry.key = key;
            entry.strip = AlpsClipStrip.Build(key.set, key.fixtureCount, key.bpm, key.beatOrigin, key.start, key.end, key.seed);
            SetTexture(entry);
            return entry;
        }

        private static Key MakeKey(TimelineClip clip, AlpsTimelineClip asset)
        {
            var track = clip.GetParentTrack() as AlpsTimelineTrack;
            var root = track != null ? track.RootTrack : null;
            var director = TimelineEditor.inspectedDirector;
            var group = director != null && root != null ? director.GetGenericBinding(root) as AlpsFixtureGroup : null;

            var fixtureCount = 0;
            if (group != null)
            {
                foreach (var fixture in group.fixtures)
                {
                    if (fixture != null)
                    {
                        fixtureCount++;
                    }
                }
            }

            var layer = track != null ? AlpsShowCompiler.LayerOf(director, track) : -1;

            return new Key
            {
                set = asset.EffectiveData,
                start = (float)clip.start,
                end = (float)clip.end,
                bpm = root != null ? root.bpm : 120f,
                beatOrigin = root != null ? root.beatOrigin : 0f,
                fixtureCount = Mathf.Max(1, fixtureCount),
                seed = AlpsShowCompiler.SeedFor(clip, Mathf.Max(0, layer)),
            };
        }

        private static bool Same(Key a, Key b)
        {
            return ReferenceEquals(a.set, b.set)
                && a.start == b.start
                && a.end == b.end
                && a.bpm == b.bpm
                && a.beatOrigin == b.beatOrigin
                && a.fixtureCount == b.fixtureCount
                && a.seed == b.seed;
        }

        private static void SetTexture(Entry entry)
        {
            var strip = entry.strip;
            if (!strip.HasColor)
            {
                DestroyTexture(entry);
                return;
            }

            if (entry.texture == null || entry.texture.width != strip.width || entry.texture.height != strip.lanes)
            {
                DestroyTexture(entry);
                entry.texture = new Texture2D(strip.width, strip.lanes, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            // Texture rows count from the bottom, strip lanes from the top.
            var pixels = new Color[strip.pixels.Length];
            for (var lane = 0; lane < strip.lanes; lane++)
            {
                System.Array.Copy(strip.pixels, lane * strip.width, pixels, (strip.lanes - 1 - lane) * strip.width, strip.width);
            }

            entry.texture.SetPixels(pixels);
            entry.texture.Apply(false);
        }

        private static void DestroyTexture(Entry entry)
        {
            if (entry.texture != null)
            {
                Object.DestroyImmediate(entry.texture);
            }

            entry.texture = null;
        }

        private static void PurgeDestroyedClips()
        {
            var dead = new List<AlpsTimelineClip>();
            foreach (var pair in Cache)
            {
                if (pair.Key == null)
                {
                    DestroyTexture(pair.Value);
                    dead.Add(pair.Key);
                }
            }

            foreach (var clip in dead)
            {
                Cache.Remove(clip);
            }
        }

        private static void ClearCache()
        {
            foreach (var entry in Cache.Values)
            {
                DestroyTexture(entry);
            }

            Cache.Clear();
        }
    }
}
