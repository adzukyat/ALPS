using System.Collections;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using VRC.Udon;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Enters play mode on the preview smoke scene with its director set to play on awake.
    /// Play mode processes a scene after its objects woke up, so the director is already
    /// playing when the show is applied and its timeline swapped. Explicit, since it enters
    /// play mode.
    /// </summary>
    [Explicit]
    public class AlpsPlayOnAwakeTests
    {
        [UnityTest]
        public IEnumerator ADirectorThatPlaysOnAwake_PlaysInPlayMode()
        {
            if (!Application.isPlaying)
            {
                // Changed in memory only: play mode starts from the open scene as it is.
                var context = AlpsPreviewSmokeFixtureBuilder.OpenFreshScene();
                context.Director.playOnAwake = true;
                context.Director.timeUpdateMode = DirectorUpdateMode.GameTime;
                yield return new EnterPlayMode();
            }

            LogAssert.ignoreFailingMessages = true;
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            var director = Object.FindObjectOfType<PlayableDirector>();
            var state = director.state;
            var time = director.time;
            var timeline = director.playableAsset != null ? director.playableAsset.name : null;
            var drawn = DrawnTime();
            Debug.Log($"[ALPS Play On Awake] state {state}, time {time}, timeline {timeline}, drawn {drawn}");
            yield return new ExitPlayMode();

            // The player's program, which the preflight made for play mode.
            AssetDatabase.DeleteAsset(AlpsShowSetup.GeneratedFolder);
            Assert.AreEqual(PlayState.Playing, state, "The director plays the build timeline.");
            Assert.Greater(time, 0.0, "Its time moves.");
            Assert.Greater(drawn, 0f, "The player draws the show on its own update.");
        }

        /// <summary>The time the player last drew, which stays at -1 while Udon does not run it.</summary>
        private static float DrawnTime()
        {
            foreach (var behaviour in Object.FindObjectsOfType<UdonBehaviour>())
            {
                if (behaviour.gameObject.name == AlpsShowSetup.PlayerName)
                {
                    return (float)behaviour.GetProgramVariable("_lastTime");
                }
            }

            return -1f;
        }
    }
}
