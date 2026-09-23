using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS
{
    /// <summary>A clip for <see cref="AlpsShowCompiler.CompileStandalone"/>.</summary>
    public class AlpsStandaloneClip
    {
        public AlpsClipEffectSet set;
        public float start;
        public float end = 1f;
        public float mixIn;
        public float mixOut;
        public int layer;
        public int seed;
    }

    /// <summary>
    /// Flattens the ALPS tracks of a timeline into a <see cref="AlpsCompiledShow"/>. The
    /// effect parameters are copied as they are, nothing is sampled over time.
    /// </summary>
    public static class AlpsShowCompiler
    {
        /// <summary>Shortest time bucket. Shorter buckets hold fewer clips each but list long clips more often.</summary>
        public const float MinBucketSeconds = 0.5f;

        /// <summary>Most time buckets a show gets. Longer shows get longer buckets.</summary>
        public const int MaxBuckets = 4096;

        public static AlpsCompiledShow Compile(PlayableDirector director)
        {
            var show = new AlpsCompiledShow();
            var timeline = director != null ? director.playableAsset as TimelineAsset : null;
            if (timeline == null)
            {
                return show;
            }

            var builder = new Builder(director, show, AlpsTimelineTrack.ShowBpm(timeline));
            foreach (var track in timeline.GetRootTracks())
            {
                builder.VisitTrack(track, false);
            }

            builder.Finish();
            return show;
        }

        /// <summary>
        /// Compiles clips without a timeline, for one target of <paramref name="fixtureCount"/>
        /// fixtures at the show tempo <paramref name="bpm"/>. Clips must be listed in layer
        /// order. Mix curves are linear.
        /// </summary>
        public static AlpsCompiledShow CompileStandalone(int fixtureCount, float bpm, params AlpsStandaloneClip[] clips)
        {
            var show = new AlpsCompiledShow();
            var builder = new Builder(null, show, bpm);
            builder.AddStandaloneGroup(fixtureCount);
            foreach (var clip in clips)
            {
                builder.AddClipRow(clip.set, clip.start, clip.end, clip.mixIn, clip.mixOut, null, null, clip.layer, 0, clip.seed);
            }

            builder.Finish();
            return show;
        }

        /// <summary>Every ALPS track of a timeline, parents before their override tracks.</summary>
        public static List<AlpsTimelineTrack> CollectTracks(TimelineAsset timeline)
        {
            var result = new List<AlpsTimelineTrack>();
            if (timeline == null)
            {
                return result;
            }

            foreach (var track in timeline.GetRootTracks())
            {
                CollectTracks(track, result);
            }

            return result;
        }

        private static void CollectTracks(TrackAsset track, List<AlpsTimelineTrack> result)
        {
            if (track is AlpsTimelineTrack alpsTrack)
            {
                result.Add(alpsTrack);
            }

            foreach (var child in track.GetChildTracks())
            {
                CollectTracks(child, result);
            }
        }

        /// <summary>
        /// The layer <see cref="Compile"/> gives <paramref name="target"/>, or -1 when the track
        /// plays nothing because it is muted or its root is not bound to a container or fixture.
        /// Follows the same walk and skip rules as the compiler, so seeds derived from it match.
        /// </summary>
        public static int LayerOf(PlayableDirector director, AlpsTimelineTrack target)
        {
            var timeline = director != null ? director.playableAsset as TimelineAsset : null;
            if (timeline == null || target == null)
            {
                return -1;
            }

            var layer = 0;
            foreach (var track in timeline.GetRootTracks())
            {
                var found = FindLayer(director, track, target, false, ref layer);
                if (found != -2)
                {
                    return found;
                }
            }

            return -1;
        }

        /// <summary>The layer of the target, -1 if it plays nothing, or -2 if it is not below <paramref name="track"/>.</summary>
        private static int FindLayer(PlayableDirector director, TrackAsset track, AlpsTimelineTrack target, bool parentMuted, ref int layer)
        {
            var muted = parentMuted || track.muted;
            if (track is AlpsTimelineTrack alpsTrack)
            {
                var plays = !muted && (director.GetGenericBinding(alpsTrack.RootTrack) as AlpsTarget) != null;
                if (alpsTrack == target)
                {
                    return plays ? layer : -1;
                }

                if (plays)
                {
                    layer++;
                }
            }

            foreach (var child in track.GetChildTracks())
            {
                var found = FindLayer(director, child, target, muted, ref layer);
                if (found != -2)
                {
                    return found;
                }
            }

            return -2;
        }

        /// <summary>A stable seed for a clip, so random order and noise match between runs.</summary>
        public static int SeedFor(TimelineClip clip, int layer)
        {
            return (Mathf.RoundToInt((float)(clip.start * 1000.0)) % 65536) + layer * 7919;
        }

        private sealed class Builder
        {
            private readonly PlayableDirector _director;
            private readonly AlpsCompiledShow _show;
            private readonly float _showBpm;

            private readonly List<float> _clips = new List<float>();
            private readonly List<float> _effects = new List<float>();
            private readonly List<float> _parameters = new List<float>();
            private readonly List<float> _colors = new List<float>();
            private readonly List<float> _gobos = new List<float>();
            private readonly List<string> _userNames = new List<string>();
            private readonly List<int> _positions = new List<int>();
            private readonly Dictionary<(int group, int order, int groupSize, int seed), int> _positionRows =
                new Dictionary<(int group, int order, int groupSize, int seed), int>();

            private readonly List<AlpsTarget> _groups = new List<AlpsTarget>();
            private readonly List<int> _groupStart = new List<int>();
            private readonly List<int> _groupCount = new List<int>();
            private readonly List<int> _groupFixtures = new List<int>();

            private int _layer;

            // The clip being encoded, for the spread division its phase blocks and value spreads share.
            private int _clipOrder;
            private int _clipFixtureCount;
            private int _clipGroupSize = 1;

            public Builder(PlayableDirector director, AlpsCompiledShow show, float showBpm)
            {
                _director = director;
                _show = show;
                _showBpm = showBpm;
            }

            public void VisitTrack(TrackAsset track, bool parentMuted)
            {
                var muted = parentMuted || track.muted;
                if (track is AlpsTimelineTrack alpsTrack && !muted)
                {
                    AddTrack(alpsTrack);
                }

                foreach (var child in track.GetChildTracks())
                {
                    VisitTrack(child, muted);
                }
            }

            private void AddTrack(AlpsTimelineTrack track)
            {
                var root = track.RootTrack;
                var group = _director.GetGenericBinding(root) as AlpsTarget;
                if (group == null)
                {
                    _show.warnings.Add($"Track '{track.name}' is not bound to an ALPS Container or a fixture, so it plays nothing.");
                    return;
                }

                var groupIndex = AddGroup(group);
                var layer = _layer++;
                foreach (var clip in track.GetAlpsClips())
                {
                    AddClip(clip, (AlpsTimelineClip)clip.asset, layer, groupIndex);
                }
            }

            private int AddGroup(AlpsTarget group)
            {
                var existing = _groups.IndexOf(group);
                if (existing >= 0)
                {
                    return existing;
                }

                _groups.Add(group);
                _groupStart.Add(_groupFixtures.Count);
                var count = 0;
                foreach (var fixture in group.Fixtures())
                {
                    if (fixture == null)
                    {
                        continue;
                    }

                    if (!fixture.IsReady)
                    {
                        _show.errors.Add($"Fixture '{fixture.name}' in '{group.name}' has nothing to drive. Assign its target.");
                    }

                    var index = _show.fixtures.IndexOf(fixture);
                    if (index < 0)
                    {
                        index = _show.fixtures.Count;
                        _show.fixtures.Add(fixture);
                    }

                    _groupFixtures.Add(index);
                    count++;
                }

                if (count == 0)
                {
                    _show.warnings.Add($"'{group.name}' has no fixtures.");
                }

                _groupCount.Add(count);
                return _groups.Count - 1;
            }

            private void AddClip(TimelineClip clip, AlpsTimelineClip asset, int layer, int groupIndex)
            {
                AddClipRow(
                    asset.EffectiveData,
                    (float)clip.start,
                    (float)clip.end,
                    (float)clip.mixInDuration,
                    (float)clip.mixOutDuration,
                    clip.mixInCurve,
                    clip.mixOutCurve,
                    layer,
                    groupIndex,
                    SeedFor(clip, layer));
            }

            public void AddStandaloneGroup(int fixtureCount)
            {
                _groupStart.Add(_groupFixtures.Count);
                _groupCount.Add(fixtureCount);
                for (var i = 0; i < fixtureCount; i++)
                {
                    _groupFixtures.Add(i);
                }
            }

            public void AddClipRow(
                AlpsClipEffectSet set,
                float start,
                float end,
                float mixInDuration,
                float mixOutDuration,
                AnimationCurve mixInCurve,
                AnimationCurve mixOutCurve,
                int layer,
                int groupIndex,
                int seed)
            {
                if (set == null)
                {
                    return;
                }

                // A clip's own tempo replaces the show's. Beats always count from the clip's
                // start, which the evaluator reads from the start column.
                var bpm = set.bpm > 0f ? set.bpm : _showBpm;

                _clipOrder = (int)set.order;
                _clipFixtureCount = groupIndex >= 0 && groupIndex < _groupCount.Count ? _groupCount[groupIndex] : 0;
                _clipGroupSize = Mathf.Max(1, set.phase.fixtureGroupSize);

                var row = new float[AlpsShowLayout.ClipStride];
                row[AlpsShowLayout.ClipStart] = start;
                row[AlpsShowLayout.ClipEnd] = end;
                row[AlpsShowLayout.ClipMixInDuration] = mixInDuration;
                row[AlpsShowLayout.ClipMixOutDuration] = mixOutDuration;
                row[AlpsShowLayout.ClipLayer] = layer;
                row[AlpsShowLayout.ClipGroup] = groupIndex;
                row[AlpsShowLayout.ClipEffectStart] = _effects.Count / AlpsShowLayout.EffectStride;
                row[AlpsShowLayout.ClipEffectCount] = set.effects.Count;
                row[AlpsShowLayout.ClipOrder] = (int)set.order;
                row[AlpsShowLayout.ClipSeed] = seed;
                row[AlpsShowLayout.ClipBpm] = bpm;
                row[AlpsShowLayout.ClipFadeIn] = Mathf.Max(0f, set.fadeInBeats);
                row[AlpsShowLayout.ClipFadeOut] = Mathf.Max(0f, set.fadeOutBeats);
                row[AlpsShowLayout.ClipPositionStart] = AddPositions(groupIndex, seed);
                WritePhase(row, AlpsShowLayout.ClipPhase, set.phase);
                WriteCurve(row, AlpsShowLayout.ClipMixInCurve, mixInCurve, 0f, 1f);
                WriteCurve(row, AlpsShowLayout.ClipMixOutCurve, mixOutCurve, 1f, 0f);
                _clips.AddRange(row);

                foreach (var effect in set.effects)
                {
                    AddEffect(effect ?? new AlpsEffect());
                }
            }

            /// <summary>
            /// Writes the order position and mirror flag of every member of the clip's group,
            /// and returns where they start. Only a random order depends on the seed, so the
            /// other clips of a group with the same order and grouping share one row.
            /// </summary>
            private int AddPositions(int groupIndex, int seed)
            {
                var key = (groupIndex, _clipOrder, _clipGroupSize, _clipOrder == AlpsShowLayout.OrderRandom ? seed : 0);
                if (_positionRows.TryGetValue(key, out var existing))
                {
                    return existing;
                }

                var start = _positions.Count;
                for (var i = 0; i < _clipFixtureCount; i++)
                {
                    _positions.Add(AlpsShowLayout.OrderPosition(_clipOrder, seed, i, _clipFixtureCount, _clipGroupSize));
                    _positions.Add(AlpsShowLayout.IsMirrored(_clipOrder, i, _clipFixtureCount, _clipGroupSize) ? 1 : 0);
                }

                _positionRows.Add(key, start);
                return start;
            }

            private void AddEffect(AlpsEffect effect)
            {
                var row = new float[AlpsShowLayout.EffectStride];
                row[AlpsShowLayout.EffectKind] = (int)effect.kind;
                row[AlpsShowLayout.EffectParity] = (int)effect.parity;
                row[AlpsShowLayout.EffectPhaseOffset] = Mathf.Clamp01(effect.phaseOffset);
                row[AlpsShowLayout.EffectParamStart] = _parameters.Count / AlpsShowLayout.ParamStride;

                switch (effect.kind)
                {
                    case AlpsEffectKind.Move:
                        AddParameter(effect.tilt);
                        AddParameter(effect.pan);
                        AddParameter(effect.circleCenterTilt);
                        AddParameter(effect.circleCenterPan);
                        AddParameter(effect.circleRadius);
                        row[AlpsShowLayout.EffectScalarA] = (int)effect.moveMode;
                        row[AlpsShowLayout.EffectScalarB] = effect.panTiltPhaseOffsetDegrees;
                        row[AlpsShowLayout.EffectScalarC] = effect.trackSpeed;
                        row[AlpsShowLayout.EffectScalarD] = AddUserName(effect.trackUserName);
                        row[AlpsShowLayout.EffectScalarE] = effect.circleAspect;
                        break;
                    case AlpsEffectKind.Cone:
                        AddParameter(effect.coneWidth);
                        AddParameter(effect.coneLength);
                        break;
                    case AlpsEffectKind.Color:
                        AddParameter(effect.colorPhasing);
                        row[AlpsShowLayout.EffectPaletteStart] = _colors.Count / AlpsShowLayout.ColorStride;
                        row[AlpsShowLayout.EffectPaletteCount] = effect.colorStops.Count;
                        foreach (var stop in effect.colorStops)
                        {
                            AddColor(stop ?? new AlpsColorStop());
                        }

                        break;
                    case AlpsEffectKind.Brightness:
                        AddParameter(effect.brightness);
                        row[AlpsShowLayout.EffectScalarA] = effect.blackoutOnReturn ? 1f : 0f;
                        row[AlpsShowLayout.EffectScalarB] = Mathf.Clamp(effect.blackoutFadeIn, 0f, 0.5f);
                        row[AlpsShowLayout.EffectScalarC] = Mathf.Clamp(effect.blackoutFadeOut, 0f, 0.5f);
                        break;
                    case AlpsEffectKind.Flicker:
                        row[AlpsShowLayout.EffectScalarA] = effect.flickerSpeed;
                        row[AlpsShowLayout.EffectScalarB] = effect.flickerStrength;
                        row[AlpsShowLayout.EffectScalarC] = effect.flickerFixtureStagger;
                        break;
                    case AlpsEffectKind.Gobo:
                        AddParameter(effect.goboPhasing);
                        row[AlpsShowLayout.EffectPaletteStart] = _gobos.Count;
                        row[AlpsShowLayout.EffectPaletteCount] = effect.goboStops.Count;
                        foreach (var stop in effect.goboStops)
                        {
                            _gobos.Add(stop != null ? Mathf.Clamp(stop.goboIndex, AlpsGoboStop.OffIndex, AlpsGoboStop.MaxIndex) : AlpsGoboStop.OffIndex);
                        }

                        row[AlpsShowLayout.EffectScalarA] = effect.goboRotationBeats;
                        row[AlpsShowLayout.EffectScalarB] = effect.goboFixtureStaggerDegrees;
                        break;
                }

                _effects.AddRange(row);
            }

            /// <summary>Encodes one parameter. A palette's phasing is one too, read only for its phase.</summary>
            private void AddParameter(AlpsAnimatableValue value)
            {
                if (value == null)
                {
                    value = new AlpsAnimatableValue();
                }

                var row = new float[AlpsShowLayout.ParamStride];
                if (value.hasSpread)
                {
                    // The spread's first value takes the value's place and its last becomes a
                    // step per order position, divided with the clip's order and grouping.
                    var start = value.spreadRange;
                    var end = value.spreadRangeEnd;
                    var step = SpreadStep(start);
                    row[AlpsShowLayout.ParamValue] = start.x;
                    row[AlpsShowLayout.ParamRangeMin] = start.x;
                    row[AlpsShowLayout.ParamRangeMax] = end.x;
                    row[AlpsShowLayout.ParamSpread] = step;
                    row[AlpsShowLayout.ParamSpreadMin] = step;
                    row[AlpsShowLayout.ParamSpreadMax] = SpreadStep(end);
                }
                else
                {
                    row[AlpsShowLayout.ParamValue] = value.value;
                    row[AlpsShowLayout.ParamRangeMin] = value.range.x;
                    row[AlpsShowLayout.ParamRangeMax] = value.range.y;
                }

                row[AlpsShowLayout.ParamIsRange] = value.isRange ? 1f : 0f;
                row[AlpsShowLayout.ParamHasSpread] = value.hasSpread ? 1f : 0f;
                row[AlpsShowLayout.ParamTiming] = (int)value.timing;
                row[AlpsShowLayout.ParamUseOwnPhase] = value.useOwnPhase ? 1f : 0f;
                WritePhase(row, AlpsShowLayout.ParamOwnPhase, value.ownPhase ?? new AlpsPhaseSettings());
                _parameters.AddRange(row);
            }

            private float SpreadStep(Vector2 spread)
            {
                return AlpsShowLayout.StepFromSpread(spread.x, spread.y, _clipOrder, _clipFixtureCount, _clipGroupSize);
            }

            private void AddColor(AlpsColorStop stop)
            {
                var row = new float[AlpsShowLayout.ColorStride];
                row[AlpsShowLayout.ColorIsGradient] = stop.isGradient ? 1f : 0f;
                row[AlpsShowLayout.ColorSolid] = stop.color.r;
                row[AlpsShowLayout.ColorSolid + 1] = stop.color.g;
                row[AlpsShowLayout.ColorSolid + 2] = stop.color.b;
                for (var i = 0; i < AlpsShowLayout.CurveSamples; i++)
                {
                    var color = stop.Evaluate(i / (float)(AlpsShowLayout.CurveSamples - 1));
                    var offset = AlpsShowLayout.ColorGradient + i * 3;
                    row[offset] = color.r;
                    row[offset + 1] = color.g;
                    row[offset + 2] = color.b;
                }

                _colors.AddRange(row);
            }

            /// <summary>The index of a tracked user's name, or -1 for none.</summary>
            private int AddUserName(string userName)
            {
                if (string.IsNullOrEmpty(userName))
                {
                    return -1;
                }

                var index = _userNames.IndexOf(userName);
                if (index >= 0)
                {
                    return index;
                }

                _userNames.Add(userName);
                return _userNames.Count - 1;
            }

            /// <summary>
            /// Encodes one phase block. The spread is divided here, with the clip's order
            /// and fixture grouping: the evaluator takes the order position k once per clip
            /// from the clip's own phase, so an own phase has to be divided the same way or
            /// its step would not match the k it is multiplied by.
            /// </summary>
            private void WritePhase(float[] row, int offset, AlpsPhaseSettings phase)
            {
                row[offset + AlpsShowLayout.PhaseMode] = (int)phase.mode;
                row[offset + AlpsShowLayout.PhaseEase] = (int)phase.ease;
                row[offset + AlpsShowLayout.PhaseRise] = phase.rise;
                row[offset + AlpsShowLayout.PhaseHoldHigh] = phase.holdHigh;
                row[offset + AlpsShowLayout.PhaseFall] = phase.fall;
                row[offset + AlpsShowLayout.PhaseGroupSize] = Mathf.Max(1, phase.fixtureGroupSize);
                row[offset + AlpsShowLayout.PhaseDelay] = AlpsShowLayout.DelayFromSpread(
                    phase.SpreadCycles,
                    _clipOrder,
                    _clipFixtureCount,
                    _clipGroupSize);
                row[offset + AlpsShowLayout.PhaseBeatsPerCycle] = Mathf.Max(0f, phase.beatsPerCycle);
                row[offset + AlpsShowLayout.PhaseInverse] = phase.inverse ? 1f : 0f;
                row[offset + AlpsShowLayout.PhaseFallEase] = (int)phase.fallEase;
            }

            private static void WriteCurve(float[] row, int offset, AnimationCurve curve, float from, float to)
            {
                for (var i = 0; i < AlpsShowLayout.CurveSamples; i++)
                {
                    var t = i / (float)(AlpsShowLayout.CurveSamples - 1);
                    row[offset + i] = curve != null && curve.length > 0 ? curve.Evaluate(t) : Mathf.Lerp(from, to, t);
                }
            }

            public void Finish()
            {
                _show.clips = _clips.ToArray();
                _show.effects = _effects.ToArray();
                _show.parameters = _parameters.ToArray();
                _show.colors = _colors.ToArray();
                _show.gobos = _gobos.ToArray();
                _show.userNames = _userNames.ToArray();
                if (_userNames.Count > AlpsShowPlayer.GpuMaxTrackedUsers)
                {
                    _show.errors.Add($"A show tracks {AlpsShowPlayer.GpuMaxTrackedUsers} users at most, this one tracks {_userNames.Count}.");
                }

                _show.positions = _positions.ToArray();
                _show.groupCount = _groupCount.ToArray();
                BuildTimeIndex();

                var fixtureCount = 0;
                foreach (var fixture in _groupFixtures)
                {
                    fixtureCount = Mathf.Max(fixtureCount, fixture + 1);
                }

                _show.fixtureCount = fixtureCount;
                _show.groupIndex = new int[_groupCount.Count * fixtureCount];
                for (var i = 0; i < _show.groupIndex.Length; i++)
                {
                    _show.groupIndex[i] = -1;
                }

                for (var group = 0; group < _groupCount.Count; group++)
                {
                    for (var i = 0; i < _groupCount[group]; i++)
                    {
                        _show.groupIndex[group * fixtureCount + _groupFixtures[_groupStart[group] + i]] = i;
                    }
                }
            }

            /// <summary>
            /// Splits the show into time buckets and lists the clips overlapping each one, so
            /// playback only weighs the clips near the current time. A clip from start to end
            /// is listed in every bucket from the one its start falls in to the one its end
            /// falls in, found with <see cref="AlpsShowLayout.TimeBucket"/> as the GPU does.
            /// </summary>
            private void BuildTimeIndex()
            {
                var stride = AlpsShowLayout.ClipStride;
                var clipCount = _clips.Count / stride;
                var span = 0f;
                for (var clip = 0; clip < clipCount; clip++)
                {
                    span = Mathf.Max(span, _clips[clip * stride + AlpsShowLayout.ClipEnd]);
                }

                var seconds = Mathf.Max(MinBucketSeconds, span / MaxBuckets);
                // Rounding can put span / seconds a hair above MaxBuckets. The show's very end
                // then falls in the last bucket, which TimeBucket clamps to as well.
                var buckets = Mathf.Clamp(Mathf.CeilToInt(span / seconds), 1, MaxBuckets);
                var bucketStart = new int[buckets + 1];

                // Count first, then fill, so every bucket's clips stay in clip order.
                for (var clip = 0; clip < clipCount; clip++)
                {
                    var first = AlpsShowLayout.TimeBucket(bucketStart, seconds, _clips[clip * stride + AlpsShowLayout.ClipStart]);
                    var last = AlpsShowLayout.TimeBucket(bucketStart, seconds, _clips[clip * stride + AlpsShowLayout.ClipEnd]);
                    for (var bucket = first; bucket <= last; bucket++)
                    {
                        bucketStart[bucket + 1]++;
                    }
                }

                for (var bucket = 0; bucket < buckets; bucket++)
                {
                    bucketStart[bucket + 1] += bucketStart[bucket];
                }

                var bucketClips = new int[bucketStart[buckets]];
                var filled = new int[buckets];
                for (var clip = 0; clip < clipCount; clip++)
                {
                    var first = AlpsShowLayout.TimeBucket(bucketStart, seconds, _clips[clip * stride + AlpsShowLayout.ClipStart]);
                    var last = AlpsShowLayout.TimeBucket(bucketStart, seconds, _clips[clip * stride + AlpsShowLayout.ClipEnd]);
                    for (var bucket = first; bucket <= last; bucket++)
                    {
                        bucketClips[bucketStart[bucket] + filled[bucket]++] = clip;
                    }
                }

                _show.bucketStart = bucketStart;
                _show.bucketClips = bucketClips;
                _show.bucketSeconds = seconds;
            }
        }
    }
}
