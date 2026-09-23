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
    /// Turns an authoring scene into the scene VRChat runs: the show is compiled into the
    /// Udon player, the director switches to the build timeline, and the authoring only
    /// components are removed. Only ever run on a scene copy that is not saved back.
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
            foreach (var director in FindShowDirectors(scene))
            {
                var show = AlpsShowCompiler.Compile(director);
                errors.AddRange(show.errors);
                warnings.AddRange(show.warnings);
                if (AlpsShowSetup.FindPlayer(director) == null)
                {
                    errors.Add($"Director '{director.name}' has ALPS tracks but no show player. Run ALPS > Set Up Show Player.");
                }
            }
        }

        /// <summary>
        /// Applies every show in <paramref name="scene"/>. With
        /// <paramref name="generateTimelines"/> off, build timelines must already exist,
        /// which is the case during a real build after the preflight. The authoring
        /// components are stripped from every scene, with a show or without one.
        /// </summary>
        public static bool Apply(Scene scene, bool generateTimelines, List<string> errors)
        {
            var startErrors = errors.Count;
            foreach (var director in FindShowDirectors(scene))
            {
                ApplyDirector(director, generateTimelines, errors);
            }

            StripAuthoringComponents(scene);
            return errors.Count == startErrors;
        }

        private static void ApplyDirector(PlayableDirector director, bool generateTimelines, List<string> errors)
        {
            var source = (TimelineAsset)director.playableAsset;
            var show = AlpsShowCompiler.Compile(director);
            if (show.errors.Count > 0)
            {
                errors.AddRange(show.errors);
                return;
            }

            var player = AlpsShowSetup.FindPlayer(director);
            if (player == null)
            {
                errors.Add($"Director '{director.name}' has ALPS tracks but no show player. Run ALPS > Set Up Show Player.");
                return;
            }

            var build = generateTimelines
                ? AlpsBuildTimeline.Generate(source, errors)
                : UnityEditor.AssetDatabase.LoadAssetAtPath<TimelineAsset>(AlpsBuildTimeline.PathFor(source));
            if (build == null)
            {
                errors.Add($"The build timeline for '{source.name}' is missing.");
                return;
            }

            CopyShowToPlayer(show, player, director);
            player.gameObject.SetActive(true);
            UdonSharpEditorUtility.CopyProxyToUdon(player);
            AlpsBuildTimeline.SwapAndRebind(director, source, build);
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
        /// Fixture components only describe the show, and arrangements only place objects
        /// while editing. VRChat runs none of them, and the transforms they left stay.
        /// </summary>
        private static void StripAuthoringComponents(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var arrangement in root.GetComponentsInChildren<AlpsArrangement>(true))
                {
                    Object.DestroyImmediate(arrangement);
                }

                foreach (var group in root.GetComponentsInChildren<AlpsFixtureGroup>(true))
                {
                    Object.DestroyImmediate(group);
                }

                foreach (var fixture in root.GetComponentsInChildren<AlpsFixture>(true))
                {
                    Object.DestroyImmediate(fixture);
                }
            }
        }
    }
}
