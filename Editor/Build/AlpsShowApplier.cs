using System.Collections.Generic;
using UdonSharpEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using VRSL;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Turns an authoring scene into the scene VRChat runs: the show is compiled into an
    /// Udon player created under the director, the director switches to the build timeline,
    /// whose projection tracks are bound to Animators above the fixtures, the fixtures'
    /// volumetric materials switch to copies with cone length via DMX on, and the authoring
    /// only components are removed. Only ever run on a scene copy that is not
    /// saved back.
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
            if (errors.Count == startErrors)
            {
                for (var i = 0; i < directors.Count; i++)
                {
                    ApplyDirector(directors[i], i + 1, generateTimelines, builds, rows, errors);
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
        /// keeps one row, free in each of them.
        /// </summary>
        public static Dictionary<AlpsFixture, int> AssignDmxRows(List<List<AlpsFixture>> shows, List<string> names, List<string> errors)
        {
            var showsOf = new Dictionary<AlpsFixture, List<int>>();
            var order = new List<AlpsFixture>();
            for (var s = 0; s < shows.Count; s++)
            {
                var count = 0;
                foreach (var fixture in shows[s])
                {
                    if (fixture == null)
                    {
                        continue;
                    }

                    if (!showsOf.TryGetValue(fixture, out var of))
                    {
                        of = new List<int>();
                        showsOf.Add(fixture, of);
                        order.Add(fixture);
                    }

                    if (!of.Contains(s))
                    {
                        of.Add(s);
                        count++;
                    }
                }

                if (count > AlpsShowPlayer.GpuMaxFixtures)
                {
                    errors.Add($"A show drives {AlpsShowPlayer.GpuMaxFixtures} fixtures at most, which is what VRSL's DMX grid holds. {names[s]} drives {count}.");
                }
            }

            var used = new List<HashSet<int>>();
            foreach (var _ in shows)
            {
                used.Add(new HashSet<int>());
            }

            var rows = new Dictionary<AlpsFixture, int>();
            var overflow = false;
            foreach (var fixture in order)
            {
                var row = 0;
                while (showsOf[fixture].Exists(s => used[s].Contains(row)))
                {
                    row++;
                }

                foreach (var s in showsOf[fixture])
                {
                    used[s].Add(row);
                }

                rows.Add(fixture, row);
                overflow |= row >= AlpsShowPlayer.GpuMaxFixtures;
            }

            // Every show fits on its own, but the fixtures they share pushed a row past the grid.
            if (overflow && used.TrueForAll(u => u.Count <= AlpsShowPlayer.GpuMaxFixtures))
            {
                errors.Add($"The fixtures the shows share leave no room for the others in VRSL's DMX grid, which holds {AlpsShowPlayer.GpuMaxFixtures}.");
            }

            return rows;
        }

        private static void ApplyDirector(PlayableDirector director, int showToken, bool generateTimelines,
            Dictionary<TimelineAsset, TimelineAsset> builds, Dictionary<AlpsFixture, int> rows, List<string> errors)
        {
            var source = (TimelineAsset)director.playableAsset;
            var show = AlpsShowCompiler.Compile(director);
            if (show.errors.Count > 0)
            {
                errors.AddRange(show.errors);
                return;
            }

            var shared = AlpsGpuAssets.GetShared(generateTimelines);
            if (!shared.IsComplete)
            {
                errors.Add("The show player's materials and targets are missing.");
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
            player.vrslLegacy = AlpsVrslVersion.IsLegacy;
            player.showToken = showToken;
            for (var i = 0; i < show.fixtures.Count; i++)
            {
                var fixture = show.fixtures[i];
                player.dmxRows[i] = fixture != null && rows.TryGetValue(fixture, out var row) ? row : i;
                if (player.vrslFixtures[i] != null)
                {
                    AlpsGpuAssets.UseConeLengthMaterials(player.vrslFixtures[i], generateTimelines, errors);
                }
            }

            player.framesMaterial = shared.framesMaterial;
            player.gridMaterial = shared.gridMaterial;
            player.frames = shared.frames;
            player.framesPrevious = shared.framesPrevious;
            player.grid = shared.grid;
            player.spin = shared.spin;
            player.gameObject.SetActive(true);
            UdonSharpEditorUtility.CopyProxyToUdon(player);
            AlpsBuildTimeline.SwapAndRebind(director, source, build);
            AlpsProjectionTracks.Bind(director, build, show, errors);
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

            var count = show.fixtures.Count;
            player.fixtureAdapter = new int[count];
            player.vrslFixtures = new VRStageLighting_DMX_Static[count];
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
            }
        }

        /// <summary>
        /// Containers and fixture components only describe the show and place objects while
        /// editing. VRChat runs none of them, and the transforms they left stay.
        /// </summary>
        private static void StripAuthoringComponents(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var target in root.GetComponentsInChildren<AlpsTarget>(true))
                {
                    Object.DestroyImmediate(target);
                }
            }
        }
    }
}
