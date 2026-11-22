using System.Collections.Generic;
using UdonSharpEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using AdzukiSoft.ALPS.Vrsl;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Turns an authoring scene into the scene VRChat runs: the show is compiled into an
    /// Udon player created under the director, the director switches to the build timeline,
    /// whose projection tracks are bound to Animators above the fixtures, the fixtures'
    /// volumetric materials switch to copies with cone length via DMX on, the projections
    /// draw between the world and the players where a container asks for it
    /// (<see cref="AlpsRenderOrder"/>), and the
    /// authoring only components are removed. Only ever run on a scene copy that is not saved
    /// back.
    /// </summary>
    public static class AlpsShowApplier
    {
        public static List<PlayableDirector> FindShowDirectors(Scene scene)
        {
            var result = new List<PlayableDirector>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var director in root.GetComponentsInChildren<PlayableDirector>(true))
                {
                    if (AlpsBuildTimeline.HasAlpsTracks(director.playableAsset as TimelineAsset))
                    {
                        result.Add(director);
                    }
                }
            }

            return result;
        }

        /// <summary>Compiles every show in the scene and reports what would stop a build.</summary>
        public static void Validate(Scene scene, List<string> errors, List<string> warnings)
        {
            var directors = FindShowDirectors(scene);
            foreach (var director in directors)
            {
                var show = AlpsShowCompiler.Compile(director);
                errors.AddRange(show.errors);
                warnings.AddRange(show.warnings);
            }

            AssignDmxRows(directors, errors);
        }

        /// <summary>
        /// Applies every show in <paramref name="scene"/>. With
        /// <paramref name="generateTimelines"/> off, build timelines must already exist,
        /// which is the case during a real build after the preflight. Other components that
        /// name an authoring timeline are pointed at its build copy. The authoring
        /// components are stripped from every scene, with a show or without one.
        /// </summary>
        public static bool Apply(Scene scene, bool generateTimelines, List<string> errors)
        {
            var startErrors = errors.Count;
            var builds = new Dictionary<TimelineAsset, TimelineAsset>();
            var directors = FindShowDirectors(scene);
            var rows = AssignDmxRows(directors, errors);
            var shared = errors.Count == startErrors && directors.Count > 0 ? AlpsGpuAssets.CreateShared() : null;
            if (shared != null && !shared.IsComplete)
            {
                errors.Add("The show player's materials and targets could not be created.");
            }
            else if (shared != null)
            {
                for (var i = 0; i < directors.Count; i++)
                {
                    ApplyDirector(directors[i], i + 1, generateTimelines, builds, rows, shared, errors);
                }

                if (AlpsRenderOrder.IsOn(scene))
                {
                    AlpsRenderOrder.Apply(scene);
                }
            }

            AlpsBuildReferences.Retarget(scene, builds);
            StripAuthoringComponents(scene);
            return errors.Count == startErrors;
        }

        private static Dictionary<AlpsFixture, int> AssignDmxRows(List<PlayableDirector> directors, List<string> errors)
        {
            var shows = new List<List<AlpsFixture>>();
            var names = new List<string>();
            foreach (var director in directors)
            {
                shows.Add(AlpsShowCompiler.Compile(director).fixtures);
                names.Add(director.name);
            }

            return AssignDmxRows(shows, names, errors);
        }

        /// <summary>
        /// Hands every fixture of the scene's shows a row of VRSL's DMX grid, which all the
        /// shows share. Only one show is on the grid at a time, so the rows are counted per
        /// show and fixtures of different shows share them: a show that is not playing keeps
        /// its fixtures inactive, or they would show the playing one. A fixture in several shows
        /// keeps one row, free in each of them. A fixture that reads nothing from the grid,
        /// a penlight slot, takes no row. The segments of a multi light bar share the rows
        /// handed to their bar (<see cref="AlpsFixture.DmxOwner"/>), four in a row, and each
        /// is given the first of them. The grid's rows come in three bands
        /// (<see cref="AlpsShowPlayer.FitDmxRows"/>), and a bar's rows stay in one.
        /// </summary>
        public static Dictionary<AlpsFixture, int> AssignDmxRows(List<List<AlpsFixture>> shows, List<string> names, List<string> errors)
        {
            var showsOf = new Dictionary<Object, List<int>>();
            var rowsOf = new Dictionary<Object, int>();
            var order = new List<Object>();
            var tooMany = new bool[shows.Count];
            for (var s = 0; s < shows.Count; s++)
            {
                var count = 0;
                foreach (var fixture in shows[s])
                {
                    if (fixture == null || !fixture.UsesDmxRow)
                    {
                        continue;
                    }

                    var owner = fixture.DmxOwner;
                    if (!showsOf.TryGetValue(owner, out var of))
                    {
                        of = new List<int>();
                        showsOf.Add(owner, of);
                        rowsOf.Add(owner, Mathf.Max(1, fixture.DmxRowCount));
                        order.Add(owner);
                    }

                    if (!of.Contains(s))
                    {
                        of.Add(s);
                        count += rowsOf[owner];
                    }
                }

                if (count > AlpsShowPlayer.GpuMaxFixtures)
                {
                    tooMany[s] = true;
                    errors.Add($"A show's fixtures take {AlpsShowPlayer.GpuMaxFixtures} rows of VRSL's DMX grid at most, one each and {AlpsVRSLMultiLightBar.RowCount} for a multi light bar. {names[s]} takes {count}.");
                }
            }

            var used = new List<HashSet<int>>();
            foreach (var _ in shows)
            {
                used.Add(new HashSet<int>());
            }

            var first = new Dictionary<Object, int>();
            var overflow = new bool[shows.Count];
            foreach (var owner in order)
            {
                var size = rowsOf[owner];
                var row = AlpsShowPlayer.FitDmxRows(0, size);
                while (row < AlpsShowPlayer.GpuDmxRowSpan && showsOf[owner].Exists(s => Taken(used[s], row, size)))
                {
                    row = AlpsShowPlayer.FitDmxRows(row + 1, size);
                }

                if (row >= AlpsShowPlayer.GpuDmxRowSpan)
                {
                    // Left without a row, which the errors below report.
                    foreach (var s in showsOf[owner])
                    {
                        overflow[s] = true;
                    }

                    first.Add(owner, -1);
                    continue;
                }

                foreach (var s in showsOf[owner])
                {
                    for (var i = 0; i < size; i++)
                    {
                        used[s].Add(row + i);
                    }
                }

                first.Add(owner, row);
            }

            // The show's rows add up to what the grid holds, but its bars or the fixtures it
            // shares with other shows left no room in a band.
            for (var s = 0; s < shows.Count; s++)
            {
                if (overflow[s] && !tooMany[s])
                {
                    errors.Add($"{names[s]} does not fit in VRSL's DMX grid. Its rows come in three bands of {AlpsShowPlayer.DmxBandRows(0)}, {AlpsShowPlayer.DmxBandRows(1)} and {AlpsShowPlayer.DmxBandRows(2)}, a multi light bar's {AlpsVRSLMultiLightBar.RowCount} rows stay in one band, and a fixture several shows share keeps one row in each of them.");
                }
            }

            var rows = new Dictionary<AlpsFixture, int>();
            foreach (var show in shows)
            {
                foreach (var fixture in show)
                {
                    if (fixture != null && fixture.UsesDmxRow && !rows.ContainsKey(fixture))
                    {
                        rows.Add(fixture, first[fixture.DmxOwner]);
                    }
                }
            }

            return rows;
        }

        private static bool Taken(HashSet<int> used, int row, int size)
        {
            for (var i = 0; i < size; i++)
            {
                if (used.Contains(row + i))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyDirector(PlayableDirector director, int showToken, bool generateTimelines,
            Dictionary<TimelineAsset, TimelineAsset> builds, Dictionary<AlpsFixture, int> rows, AlpsGpuAssets.Shared shared,
            List<string> errors)
        {
            var source = (TimelineAsset)director.playableAsset;
            var show = AlpsShowCompiler.Compile(director);
            if (show.errors.Count > 0)
            {
                errors.AddRange(show.errors);
                return;
            }

            var player = AlpsShowSetup.GetOrCreatePlayer(director);
            if (player == null)
            {
                errors.Add($"The show player for director '{director.name}' could not be created.");
                return;
            }

            // Directors sharing a timeline share its build copy. Generating it again would
            // replace the asset the earlier director already points at.
            if (!builds.TryGetValue(source, out var build))
            {
                build = generateTimelines
                    ? AlpsBuildTimeline.Generate(source, show, errors)
                    : UnityEditor.AssetDatabase.LoadAssetAtPath<TimelineAsset>(AlpsBuildTimeline.PathFor(source));
                if (build == null)
                {
                    errors.Add($"The build timeline for '{source.name}' is missing.");
                    return;
                }

                builds.Add(source, build);
            }

            CopyShowToPlayer(show, player, director);
            player.dmxRows = new int[show.fixtures.Count];
            player.showToken = showToken;
            for (var i = 0; i < show.fixtures.Count; i++)
            {
                var fixture = show.fixtures[i];
                player.dmxRows[i] = fixture != null && rows.TryGetValue(fixture, out var row) ? row : (fixture != null && !fixture.UsesDmxRow ? -1 : i);
            }

            player.framesMaterial = shared.framesMaterial;
            player.gridMaterial = shared.gridMaterial;
            player.penlightMaterial = shared.penlightMaterial;
            player.frames = shared.frames;
            player.framesPrevious = shared.framesPrevious;
            player.grid = shared.grid;
            player.spin = shared.spin;
            player.penlightTarget = shared.penlight;
            player.gameObject.SetActive(true);
            UdonSharpEditorUtility.CopyProxyToUdon(player);
            // Play mode processes a scene after its objects woke up, so a director that plays on
            // awake is already playing here, and taking another timeline stops it. A build
            // processes the scene before anything wakes up.
            var playing = Application.isPlaying && director.state == PlayState.Playing;
            var time = director.time;
            AlpsBuildTimeline.SwapAndRebind(director, source, build);
            AlpsProjectionTracks.Bind(director, build, show, errors);
            if (playing)
            {
                director.time = time;
                director.Play();
            }
        }

        public static void CopyShowToPlayer(AlpsCompiledShow show, AlpsShowPlayer player, PlayableDirector director)
        {
            player.director = director;
            player.clips = show.clips;
            player.effects = show.effects;
            player.parameters = show.parameters;
            player.colors = show.colors;
            player.gobos = show.gobos;
            player.userNames = show.userNames;
            player.positions = show.positions;
            player.bucketStart = show.bucketStart;
            player.bucketClips = show.bucketClips;
            player.bucketSeconds = show.bucketSeconds;
            player.groupCount = show.groupCount;
            player.groupIndex = show.groupIndex;
            player.penlight = show.penlight;
            player.penlightTransforms = AlpsPenlight.Transforms(show);

            var count = show.fixtures.Count;
            player.fixtureAdapter = new int[count];
            player.vrslFixtures = new VRStageLighting_DMX_Static[count];
            player.fixtureSegment = new int[count];
            player.aimTransforms = new Transform[count];
            for (var i = 0; i < count; i++)
            {
                var fixture = show.fixtures[i];
                if (fixture == null)
                {
                    continue;
                }

                player.fixtureAdapter[i] = fixture.AdapterKind;
                player.aimTransforms[i] = fixture.AimTransform;
                if (fixture is AlpsVRSLFixture vrslFixture)
                {
                    player.vrslFixtures[i] = vrslFixture.ResolveTarget();
                }
                else if (fixture is AlpsVRSLMultiLightBarSegment segment)
                {
                    // The player sets the bar up once per segment, which comes to the same.
                    var bar = segment.Bar;
                    player.vrslFixtures[i] = bar != null ? bar.ResolveTarget() : null;
                    player.fixtureSegment[i] = segment.index;
                }
            }
        }

        /// <summary>
        /// Containers and fixture components only describe the show and place objects while
        /// editing. VRChat runs none of them, and the transforms they left stay. A penlight
        /// slot and a segment of a multi light bar are nothing but their component, so their
        /// objects go too.
        /// </summary>
        private static void StripAuthoringComponents(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var fixture in root.GetComponentsInChildren<AlpsFixture>(true))
                {
                    var isSlot = fixture is AlpsPenlightSlot || fixture is AlpsVRSLMultiLightBarSegment;
                    if (isSlot && fixture.transform.childCount == 0 && fixture.GetComponents<Component>().Length == 2)
                    {
                        Object.DestroyImmediate(fixture.gameObject);
                    }
                }

                foreach (var target in root.GetComponentsInChildren<AlpsTarget>(true))
                {
                    Object.DestroyImmediate(target);
                }
            }
        }
    }
}
