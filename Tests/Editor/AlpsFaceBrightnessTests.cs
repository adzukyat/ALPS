using System.Collections.Generic;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// How bright the lit face of each fixture comes out at 100%, previewed and rendered
    /// without tonemapping. VRSL's own materials light a face hundreds of times past white,
    /// and each fixture by another amount, so <c>scripts/vendor-vrsl.py</c> sets the
    /// intensities this holds, and the grid dims every fixture by its colour: the same level
    /// looks about the same on every fixture.
    ///
    /// The spotlight's lens is the end of its cone, which its cone material lights. The laser
    /// has no lit face: its light is its beams.
    /// </summary>
    public class AlpsFaceBrightnessTests
    {
        /// <summary>What a white face shines at 100%, in units of white.</summary>
        private const float Face = 4f;
        private const float Distance = 4f;
        private const float Spacing = 10f;
        private const int Size = 256;

        private static readonly AlpsVrslPrefab[] Fixtures =
        {
            AlpsCreateMenu.MoverSpotlight, AlpsCreateMenu.MoverWashLight, AlpsCreateMenu.ParLight, AlpsCreateMenu.Blinder, AlpsCreateMenu.LightBar,
            AlpsCreateMenu.MultiLightBar,
        };

        private readonly List<Object> _created = new List<Object>();
        private bool _asyncCompilation;

        [SetUp]
        public void RequireGraphics()
        {
            AlpsGpuShow.RequireGraphics();

            // An edit mode render skips what is still compiling.
            _asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
        }

        [TearDown]
        public void CloseScene()
        {
            ShaderUtil.allowAsyncCompilation = _asyncCompilation;
            RenderTexture.active = null;
            foreach (var created in _created)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            _created.Clear();
            AlpsPreviewDriver.MarkDirty();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void AFace_ShinesTheSameOnEveryFixtureAtFull()
        {
            var faces = Measure(100f);
            var report = Describe(faces);
            Debug.Log("[ALPS Face Brightness] at 100%: " + report);
            for (var i = 0; i < Fixtures.Length; i++)
            {
                Assert.AreEqual(Face, faces[i], Face * 0.1f, $"{Fixtures[i].Name} at 100%. {report}");
            }
        }

        [Test]
        public void AFace_DimsByTheSameCurveOnEveryFixture()
        {
            var half = Measure(50f);
            var report = Describe(half);
            Debug.Log("[ALPS Face Brightness] at 50%: " + report);
            for (var i = 0; i < Fixtures.Length; i++)
            {
                // Half the level looks half as bright, which is the level to the power of 2.2 in light.
                Assert.AreEqual(Face * Mathf.Pow(0.5f, 2.2f), half[i], Face * 0.02f, $"{Fixtures[i].Name} at 50%. {report}");
            }
        }

        [Test]
        public void AStaticLight_ThrowsNoLightOnItsOwnFace()
        {
            // A par's light starts 5 cm behind its face and grows toward where it starts, which
            // lit the middle of the face over a hundred times past white. Seen from 1 m, where
            // that middle is several pixels.
            var faces = Measure(100f, true, 1f);
            var report = Describe(faces);
            Debug.Log("[ALPS Face Brightness] with its own light on it: " + report);
            for (var i = 0; i < Fixtures.Length; i++)
            {
                if (Fixtures[i] == AlpsCreateMenu.ParLight || Fixtures[i] == AlpsCreateMenu.Blinder)
                {
                    Assert.AreEqual(Face, faces[i], Face * 0.1f, $"{Fixtures[i].Name}. {report}");
                }
            }
        }

        private static string Describe(float[] faces)
        {
            var parts = new List<string>();
            for (var i = 0; i < Fixtures.Length; i++)
            {
                parts.Add($"{Fixtures[i].Name} {faces[i]:0.00}");
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// The brightest point of each fixture's face at a level, white, seen from in front.
        /// With <paramref name="projections"/> the light the fixture throws is drawn too,
        /// which lands on the face itself where the face is in its way.
        /// </summary>
        private float[] Measure(float level, bool projections = false, float from = Distance)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0f;

            var container = Keep(AlpsCreateMenu.CreateContainer(null));
            var fixtures = new GameObject[Fixtures.Length];
            for (var i = 0; i < Fixtures.Length; i++)
            {
                var fixture = AlpsCreateMenu.CreateFixture(Fixtures[i], container);
                Assert.NotNull(fixture, Fixtures[i].Path);
                fixture.transform.position = new Vector3(i * Spacing, Distance, 0f);
                // Every face looks straight down: a hung mover's, a static light's along its
                // own -Y, and the multi light bar's along its own +Z.
                var mover = Fixtures[i] == AlpsCreateMenu.MoverSpotlight || Fixtures[i] == AlpsCreateMenu.MoverWashLight;
                fixture.transform.rotation = mover ? Quaternion.Euler(90f, 180f, 0f)
                    : Fixtures[i] == AlpsCreateMenu.MultiLightBar ? Quaternion.Euler(90f, 0f, 0f)
                    : Quaternion.identity;

                // The face alone, without the light it throws and the blinder's flare, which are
                // drawn over it. A mover keeps its cone, whose end is the spotlight's lens.
                foreach (var renderer in fixture.GetComponentsInChildren<Renderer>(true))
                {
                    var shader = renderer.sharedMaterial != null ? renderer.sharedMaterial.shader.name : string.Empty;
                    if ((shader.EndsWith("/Projection") && !projections) || shader.EndsWith("/Lens Flare"))
                    {
                        renderer.enabled = false;
                    }
                }

                fixtures[i] = fixture;
            }

            var timeline = Keep(ScriptableObject.CreateInstance<TimelineAsset>());
            var director = Keep(new GameObject("Show")).AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            var track = timeline.CreateTrack<AlpsLightingTrack>(null, "Rig");
            director.SetGenericBinding(track, container.GetComponent<AlpsContainer>());
            var clip = track.CreateClip<AlpsLightingClip>();
            clip.start = 0.0;
            clip.duration = 10.0;
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.white));
            set.Add(AlpsEffectKind.Brightness).brightness.value = level;
            ((AlpsLightingClip)clip.asset).data = set;

            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.RebuildGraph();
            director.time = 1.0;
            director.Evaluate();

            var camera = Keep(new GameObject("Camera")).AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = true;
            camera.fieldOfView = 20f;
            camera.nearClipPlane = 0.05f;
            // A projection finds what it lands on in the depth texture.
            camera.depthTextureMode = projections ? DepthTextureMode.Depth : DepthTextureMode.None;
            var target = Keep(new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var image = Keep(new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true));
            camera.targetTexture = target;

            var faces = new float[Fixtures.Length];
            for (var i = 0; i < Fixtures.Length; i++)
            {
                camera.transform.position = new Vector3(i * Spacing, Distance - from, 0f);
                camera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                image.Apply();
                RenderTexture.active = null;
                foreach (var pixel in image.GetPixels())
                {
                    faces[i] = Mathf.Max(faces[i], pixel.r * 0.2126f + pixel.g * 0.7152f + pixel.b * 0.0722f);
                }
            }

            camera.targetTexture = null;
            return faces;
        }

        private T Keep<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }
    }
}
