using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ManeuverForVRC
{
    /// <summary>A clip for <see cref="MfvShowCompiler.CompileStandalone"/>.</summary>
    public class MfvStandaloneClip
    {
        public MfvClipEffectSet set;
        public float start;
        public float end = 1f;
        public float mixIn;
        public float mixOut;
        public int layer;
        public int seed;
    }

    /// <summary>
    /// Flattens the MFV tracks of a timeline into a <see cref="MfvCompiledShow"/>. The
    /// effect parameters are copied as they are, nothing is sampled over time.
    /// </summary>
    public static class MfvShowCompiler
    {
        public static MfvCompiledShow Compile(PlayableDirector director)
        {
            var show = new MfvCompiledShow();
            var timeline = director != null ? director.playableAsset as TimelineAsset : null;
            if (timeline == null)
            {
                return show;
            }

            var builder = new Builder(director, show);
            foreach (var track in timeline.GetRootTracks())
            {
                builder.VisitTrack(track, false);
            }

            builder.Finish();
            return show;
        }

        /// <summary>
        /// Compiles clips without a timeline, for one group of <paramref name="fixtureCount"/>
        /// fixtures. Clips must be listed in layer order. Mix curves are linear.
        /// </summary>
        public static MfvCompiledShow CompileStandalone(int fixtureCount, float bpm, float beatOrigin, params MfvStandaloneClip[] clips)
        {
            var show = new MfvCompiledShow();
            var builder = new Builder(null, show);
            builder.AddStandaloneGroup(fixtureCount);
            foreach (var clip in clips)
            {
                builder.AddClipRow(clip.set, clip.start, clip.end, clip.mixIn, clip.mixOut, null, null, clip.layer, 0, clip.seed, bpm, beatOrigin);
            }

            builder.Finish();
            return show;
        }

        /// <summary>Every MFV track of a timeline, parents before their override tracks.</summary>
        public static List<MfvTimelineTrack> CollectTracks(TimelineAsset timeline)
        {
            var result = new List<MfvTimelineTrack>();
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

        private static void CollectTracks(TrackAsset track, List<MfvTimelineTrack> result)
        {
            if (track is MfvTimelineTrack mfvTrack)
            {
                result.Add(mfvTrack);
            }

            foreach (var child in track.GetChildTracks())
            {
                CollectTracks(child, result);
            }
        }

        /// <summary>A stable seed for a clip, so random order and noise match between runs.</summary>
        public static int SeedFor(TimelineClip clip, int layer)
        {
            return (Mathf.RoundToInt((float)(clip.start * 1000.0)) % 65536) + layer * 7919;
        }

        private sealed class Builder
        {
            private readonly PlayableDirector _director;
            private readonly MfvCompiledShow _show;

            private readonly List<float> _clips = new List<float>();
            private readonly List<float> _effects = new List<float>();
            private readonly List<float> _parameters = new List<float>();
            private readonly List<float> _colors = new List<float>();
            private readonly List<float> _gobos = new List<float>();
            private readonly List<string> _userNames = new List<string>();

            private readonly List<MfvFixtureGroup> _groups = new List<MfvFixtureGroup>();
            private readonly List<int> _groupStart = new List<int>();
            private readonly List<int> _groupCount = new List<int>();
            private readonly List<int> _groupFixtures = new List<int>();

            private int _layer;

            public Builder(PlayableDirector director, MfvCompiledShow show)
            {
                _director = director;
                _show = show;
            }

            public void VisitTrack(TrackAsset track, bool parentMuted)
            {
                var muted = parentMuted || track.muted;
                if (track is MfvTimelineTrack mfvTrack && !muted)
                {
                    AddTrack(mfvTrack);
                }

                foreach (var child in track.GetChildTracks())
                {
                    VisitTrack(child, muted);
                }
            }

            private void AddTrack(MfvTimelineTrack track)
            {
                var root = track.RootTrack;
                var group = _director.GetGenericBinding(root) as MfvFixtureGroup;
                if (group == null)
                {
                    _show.warnings.Add($"Track '{track.name}' is not bound to an MFV Fixture Group, so it plays nothing.");
                    return;
                }

                var groupIndex = AddGroup(group);
                var layer = _layer++;
                foreach (var clip in track.GetMfvClips())
                {
                    AddClip(clip, (MfvTimelineClip)clip.asset, root, layer, groupIndex);
                }
            }

            private int AddGroup(MfvFixtureGroup group)
            {
                var existing = _groups.IndexOf(group);
                if (existing >= 0)
                {
                    return existing;
                }

                _groups.Add(group);
                _groupStart.Add(_groupFixtures.Count);
                var count = 0;
                foreach (var fixture in group.fixtures)
                {
                    if (fixture == null)
                    {
                        continue;
                    }

                    if (!fixture.IsReady)
                    {
                        _show.errors.Add($"Fixture '{fixture.name}' in group '{group.name}' has nothing to drive. Assign its target.");
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
                    _show.warnings.Add($"Fixture group '{group.name}' has no fixtures.");
                }

                _groupCount.Add(count);
                return _groups.Count - 1;
            }

            private void AddClip(TimelineClip clip, MfvTimelineClip asset, MfvTimelineTrack root, int layer, int groupIndex)
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
                    SeedFor(clip, layer),
                    root.bpm,
                    root.beatOrigin);
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
                MfvClipEffectSet set,
                float start,
                float end,
                float mixInDuration,
                float mixOutDuration,
                AnimationCurve mixInCurve,
                AnimationCurve mixOutCurve,
                int layer,
                int groupIndex,
                int seed,
                float bpm,
                float beatOrigin)
            {
                if (set == null)
                {
                    return;
                }

                var row = new float[MfvShowEvaluator.ClipStride];
                row[MfvShowEvaluator.ClipStart] = start;
                row[MfvShowEvaluator.ClipEnd] = end;
                row[MfvShowEvaluator.ClipMixInDuration] = mixInDuration;
                row[MfvShowEvaluator.ClipMixOutDuration] = mixOutDuration;
                row[MfvShowEvaluator.ClipLayer] = layer;
                row[MfvShowEvaluator.ClipGroup] = groupIndex;
                row[MfvShowEvaluator.ClipEffectStart] = _effects.Count / MfvShowEvaluator.EffectStride;
                row[MfvShowEvaluator.ClipEffectCount] = set.effects.Count;
                row[MfvShowEvaluator.ClipOrder] = (int)set.order;
                row[MfvShowEvaluator.ClipSeed] = seed;
                row[MfvShowEvaluator.ClipBpm] = bpm;
                row[MfvShowEvaluator.ClipBeatOrigin] = beatOrigin;
                WritePhase(row, MfvShowEvaluator.ClipPhase, set.phase);
                WriteCurve(row, MfvShowEvaluator.ClipMixInCurve, mixInCurve, 0f, 1f);
                WriteCurve(row, MfvShowEvaluator.ClipMixOutCurve, mixOutCurve, 1f, 0f);
                _clips.AddRange(row);

                foreach (var effect in set.effects)
                {
                    AddEffect(effect ?? new MfvEffect());
                }
            }

            private void AddEffect(MfvEffect effect)
            {
                var row = new float[MfvShowEvaluator.EffectStride];
                row[MfvShowEvaluator.EffectKind] = (int)effect.kind;
                row[MfvShowEvaluator.EffectParity] = (int)effect.parity;
                row[MfvShowEvaluator.EffectParamStart] = _parameters.Count / MfvShowEvaluator.ParamStride;

                switch (effect.kind)
                {
                    case MfvEffectKind.Move:
                        AddParameter(effect.tilt);
                        AddParameter(effect.pan);
                        AddParameter(effect.circleCenterTilt);
                        AddParameter(effect.circleCenterPan);
                        AddParameter(effect.circleRadius);
                        row[MfvShowEvaluator.EffectScalarA] = (int)effect.moveMode;
                        row[MfvShowEvaluator.EffectScalarB] = effect.panTiltPhaseOffsetDegrees;
                        row[MfvShowEvaluator.EffectScalarC] = effect.trackSpeed;
                        row[MfvShowEvaluator.EffectScalarD] = AddUserName(effect.trackUserName);
                        row[MfvShowEvaluator.EffectScalarE] = effect.circleAspect;
                        break;
                    case MfvEffectKind.Cone:
                        AddParameter(effect.coneWidth);
                        AddParameter(effect.coneLength);
                        break;
                    case MfvEffectKind.Color:
                        AddParameter(effect.colorPhasing);
                        row[MfvShowEvaluator.EffectPaletteStart] = _colors.Count / MfvShowEvaluator.ColorStride;
                        row[MfvShowEvaluator.EffectPaletteCount] = effect.colorStops.Count;
                        foreach (var stop in effect.colorStops)
                        {
                            AddColor(stop ?? new MfvColorStop());
                        }

                        break;
                    case MfvEffectKind.Brightness:
                        AddParameter(effect.brightness);
                        row[MfvShowEvaluator.EffectScalarA] = effect.blackoutOnReturn ? 1f : 0f;
                        row[MfvShowEvaluator.EffectScalarB] = Mathf.Clamp(effect.blackoutFadeIn, 0f, 0.5f);
                        row[MfvShowEvaluator.EffectScalarC] = Mathf.Clamp(effect.blackoutFadeOut, 0f, 0.5f);
                        break;
                    case MfvEffectKind.Flicker:
                        row[MfvShowEvaluator.EffectScalarA] = effect.flickerSpeed;
                        row[MfvShowEvaluator.EffectScalarB] = effect.flickerStrength;
                        row[MfvShowEvaluator.EffectScalarC] = effect.flickerFixtureStagger;
                        break;
                    case MfvEffectKind.Gobo:
                        AddParameter(effect.goboPhasing);
                        row[MfvShowEvaluator.EffectPaletteStart] = _gobos.Count;
                        row[MfvShowEvaluator.EffectPaletteCount] = effect.goboStops.Count;
                        foreach (var stop in effect.goboStops)
                        {
                            _gobos.Add(stop != null ? Mathf.Clamp(stop.goboIndex, MfvGoboStop.OffIndex, MfvGoboStop.MaxIndex) : MfvGoboStop.OffIndex);
                        }

                        row[MfvShowEvaluator.EffectScalarA] = effect.goboRotationBeats;
                        row[MfvShowEvaluator.EffectScalarB] = effect.goboFixtureStaggerDegrees;
                        break;
                }

                _effects.AddRange(row);
            }

            private void AddParameter(MfvAnimatableValue value)
            {
                if (value == null)
                {
                    value = new MfvAnimatableValue();
                }

                var row = new float[MfvShowEvaluator.ParamStride];
                row[MfvShowEvaluator.ParamValue] = value.value;
                row[MfvShowEvaluator.ParamRangeMin] = value.range.x;
                row[MfvShowEvaluator.ParamRangeMax] = value.range.y;
                row[MfvShowEvaluator.ParamIsRange] = value.isRange ? 1f : 0f;
                row[MfvShowEvaluator.ParamSpread] = value.spread;
                row[MfvShowEvaluator.ParamSpreadMin] = value.spreadRange.x;
                row[MfvShowEvaluator.ParamSpreadMax] = value.spreadRange.y;
                row[MfvShowEvaluator.ParamHasSpread] = value.hasSpread ? 1f : 0f;
                row[MfvShowEvaluator.ParamTiming] = (int)value.timing;
                row[MfvShowEvaluator.ParamUseOwnPhase] = value.useOwnPhase ? 1f : 0f;
                WritePhase(row, MfvShowEvaluator.ParamOwnPhase, value.ownPhase ?? new MfvPhaseSettings());
                _parameters.AddRange(row);
            }

            private void AddColor(MfvColorStop stop)
            {
                var row = new float[MfvShowEvaluator.ColorStride];
                row[MfvShowEvaluator.ColorIsGradient] = stop.isGradient ? 1f : 0f;
                row[MfvShowEvaluator.ColorSolid] = stop.color.r;
                row[MfvShowEvaluator.ColorSolid + 1] = stop.color.g;
                row[MfvShowEvaluator.ColorSolid + 2] = stop.color.b;
                for (var i = 0; i < MfvShowEvaluator.CurveSamples; i++)
                {
                    var color = stop.Evaluate(i / (float)(MfvShowEvaluator.CurveSamples - 1));
                    var offset = MfvShowEvaluator.ColorGradient + i * 3;
                    row[offset] = color.r;
                    row[offset + 1] = color.g;
                    row[offset + 2] = color.b;
                }

                _colors.AddRange(row);
            }

            private int AddUserName(string userName)
            {
                if (userName == null)
                {
                    userName = string.Empty;
                }

                var index = _userNames.IndexOf(userName);
                if (index >= 0)
                {
                    return index;
                }

                _userNames.Add(userName);
                return _userNames.Count - 1;
            }

            private static void WritePhase(float[] row, int offset, MfvPhaseSettings phase)
            {
                row[offset + MfvShowEvaluator.PhaseMode] = (int)phase.mode;
                row[offset + MfvShowEvaluator.PhaseEase] = (int)phase.ease;
                row[offset + MfvShowEvaluator.PhaseRatio] = phase.pingPongRatio;
                row[offset + MfvShowEvaluator.PhaseGroupSize] = Mathf.Max(1, phase.fixtureGroupSize);
                row[offset + MfvShowEvaluator.PhaseDelay] = phase.delay;
                row[offset + MfvShowEvaluator.PhaseBeatsPerCycle] = Mathf.Max(0f, phase.beatsPerCycle);
                row[offset + MfvShowEvaluator.PhaseInverse] = phase.inverse ? 1f : 0f;
            }

            private static void WriteCurve(float[] row, int offset, AnimationCurve curve, float from, float to)
            {
                for (var i = 0; i < MfvShowEvaluator.CurveSamples; i++)
                {
                    var t = i / (float)(MfvShowEvaluator.CurveSamples - 1);
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
                _show.groupCount = _groupCount.ToArray();

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
        }
    }
}
