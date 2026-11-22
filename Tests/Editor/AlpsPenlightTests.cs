using System.Collections.Generic;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Penlight areas: the slots a show drives come out as the penlight texture, and the
    /// penlight shader finds its colour in it from where the penlight is.
    /// </summary>
    public class AlpsPenlightTests
    {
        private const float Bpm = 120f;
        private const int Slots = AlpsShowPlayer.GpuMaxPenlightSlots;

        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void OpenScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void CloseScene()
        {
            Shader.SetGlobalTexture(AlpsShowPlayer.PenlightTextureProperty, null);
            foreach (var created in _created)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            _created.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // --- Slots -------------------------------------------------------------------------

        [Test]
        public void Slots_FollowTheCount()
        {
            var area = Area(5);
            Assert.AreEqual(5, area.Slots().Count);
            Assert.AreEqual(5, area.Fixtures().Count, "A track bound to the area drives its slots.");

            area.slots = 2;
            Assert.IsTrue(AlpsPenlightSlots.Sync(area, recordUndo: false));
            Assert.AreEqual(2, area.Slots().Count);
            Assert.IsFalse(AlpsPenlightSlots.Sync(area, recordUndo: false), "Nothing changes at the right count.");
        }

        [Test]
        public void Slots_BelongToTheClosestArea()
        {
            var outer = Area(2);
            var inner = Area(3);
            inner.transform.SetParent(outer.transform, false);

            Assert.AreEqual(2, outer.Slots().Count);
            Assert.AreEqual(3, inner.Slots().Count);
        }

        [Test]
        public void CreateMenu_MakesAnAreaWithItsSlots()
        {
            var area = AlpsCreateMenu.CreatePenlightArea(null).GetComponent<AlpsPenlightArea>();

            Assert.AreEqual(area.slots, area.Slots().Count);
            Assert.AreEqual(1, area.gameObject.scene.rootCount, "The slots sit below the area.");
        }

        [Test]
        public void Rows_SlotsTakeNoDmxRow()
        {
            var fixtures = new List<AlpsFixture>();
            for (var i = 0; i < AlpsShowPlayer.GpuMaxFixtures; i++)
            {
                var go = new GameObject("Fixture " + i);
                fixtures.Add(go.AddComponent<AlpsVRSLFixture>());
            }

            var slots = Area(8).Fixtures();
            fixtures.InsertRange(0, slots);

            var errors = new List<string>();
            var rows = AlpsShowApplier.AssignDmxRows(
                new List<List<AlpsFixture>> { fixtures }, new List<string> { "Show" }, errors);

            Assert.IsEmpty(errors, "A full grid still leaves room for the slots.");
            Assert.AreEqual(AlpsShowPlayer.GpuMaxFixtures, rows.Count);
            Assert.IsFalse(rows.ContainsKey(slots[0]));
            Assert.AreEqual(0, rows[fixtures[slots.Count]], "The first fixture on the grid takes the first row.");
        }

        // --- Show data ---------------------------------------------------------------------

        [Test]
        public void Compile_ListsTheShowsFixtureOnEachSlot()
        {
            var first = Area(3);
            var second = Area(2);
            second.shape = AlpsPenlightShape.Radial;
            second.size = 4f;

            var show = new AlpsCompiledShow();
            show.fixtures.Add(new GameObject("Fixture").AddComponent<AlpsVRSLFixture>());
            show.fixtures.AddRange(second.Fixtures());
            show.fixtures.AddRange(first.Fixtures());
            AlpsPenlight.Compile(show);

            Assert.IsEmpty(show.errors);
            CollectionAssert.AreEqual(new[] { second, first }, show.penlightAreas, "Areas come in the order the show meets them.");
            var stride = AlpsShowPlayer.GpuPenlightAreaStride;
            Assert.AreEqual(2 * stride, show.penlight.Length);
            Assert.AreEqual(2f, show.penlight[AlpsShowPlayer.PenlightSlotCount]);
            Assert.AreEqual((float)AlpsPenlightShape.Radial, show.penlight[AlpsShowPlayer.PenlightShape]);
            Assert.AreEqual(4f, show.penlight[AlpsShowPlayer.PenlightSize]);
            Assert.AreEqual(1f, show.penlight[AlpsShowPlayer.PenlightSlots]);
            Assert.AreEqual(2f, show.penlight[AlpsShowPlayer.PenlightSlots + 1]);
            Assert.AreEqual(-1f, show.penlight[AlpsShowPlayer.PenlightSlots + 2], "Slots past the count hold no fixture.");
            Assert.AreEqual(3f, show.penlight[stride + AlpsShowPlayer.PenlightSlots]);
        }

        [Test]
        public void Compile_HoldsAsManyAreasAsTheTextureHasRows()
        {
            var show = new AlpsCompiledShow();
            for (var i = 0; i <= AlpsShowPlayer.GpuMaxPenlightAreas; i++)
            {
                show.fixtures.AddRange(Area(1).Fixtures());
            }

            AlpsPenlight.Compile(show);

            Assert.AreEqual(1, show.errors.Count);
            Assert.AreEqual(AlpsShowPlayer.GpuMaxPenlightAreas, show.penlightAreas.Count);
        }

        [Test]
        public void Moved_NoticesAnAreaThatWasMovedOrReshaped()
        {
            var area = Area(2);
            var show = new AlpsCompiledShow();
            show.fixtures.AddRange(area.Fixtures());
            AlpsPenlight.Compile(show);
            Assert.IsFalse(AlpsPenlight.Moved(show));

            area.transform.position = new Vector3(1f, 0f, 0f);
            Assert.IsTrue(AlpsPenlight.Moved(show));

            AlpsPenlight.Compile(show);
            area.size *= 2f;
            Assert.IsTrue(AlpsPenlight.Moved(show));
        }

        // --- Penlight texture --------------------------------------------------------------

        [Test]
        public void Grid_HoldsTheLightOfEachSlotsColourAtItsBrightness()
        {
            AlpsGpuShow.RequireGraphics();
            var set = new AlpsClipEffectSet();
            var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
            brightness.hasSpread = true;
            brightness.spreadRange = new Vector2(20f, 100f);
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.red));
            var show = AlpsShowCompiler.CompileStandalone(4, Bpm, new AlpsStandaloneClip { set = set, end = 4f });

            // The slots run against the fixtures, and the second row holds no area.
            var matrix = Matrix4x4.Translate(new Vector3(1f, 2f, 3f));
            var penlight = new float[AlpsShowPlayer.GpuPenlightAreaStride];
            AlpsPenlight.WriteArea(penlight, 0, new[] { 3, 2, 1, 0 }, AlpsPenlightShape.Radial, 5f, matrix);

            using (var gpu = new AlpsGpuShow(show, penlight: penlight))
            {
                var frames = gpu.FramesAt(1f);
                var texture = gpu.ReadBack(gpu.Penlight);
                for (var slot = 0; slot < 4; slot++)
                {
                    var fixture = 3 - slot;
                    var level = frames[fixture * AlpsShowLayout.FrameStride + AlpsShowLayout.FrameBrightness] / 100f;
                    var texel = texture.GetPixel(slot, 0);
                    Assert.AreEqual(AlpsGpuShow.Light(1f, level), texel.r, 1e-4f, $"Slot {slot} is fixture {fixture}'s red at its brightness.");
                    Assert.AreEqual(0f, texel.g, 1e-4f);
                    Assert.AreEqual(level, texel.a, 1e-4f, "Alpha holds the brightness.");
                }

                Assert.AreEqual(AlpsGpuShow.Light(1f, 0.2f), texture.GetPixel(3, 0).r, 1e-4f, "The first fixture is the dimmest and sits on the last slot.");
                Assert.AreEqual(new Color(0f, 0f, 0f, 0f), texture.GetPixel(4, 0), "Slots past the count stay empty.");

                var info = texture.GetPixel(Slots + 3, 0);
                Assert.AreEqual(4f, info.r);
                Assert.AreEqual((float)AlpsPenlightShape.Radial, info.g);
                Assert.AreEqual(5f, info.b);
                Assert.AreEqual(1f, info.a, "An area that is there says so.");
                Assert.AreEqual(new Color(1f, 0f, 0f, 1f), texture.GetPixel(Slots, 0), "The matrix rows follow the slots.");
                Assert.AreEqual(new Color(0f, 0f, 1f, 3f), texture.GetPixel(Slots + 2, 0));
                Assert.AreEqual(0f, texture.GetPixel(Slots + 3, 1).a, "A row without an area is empty.");
            }
        }

        // --- Penlight shader ---------------------------------------------------------------

        [Test]
        public void Shader_BlendsTheSlotsAroundThePenlight()
        {
            AlpsGpuShow.RequireGraphics();
            var colours = new[] { Color.red, Color.green, Color.blue, Color.white };
            var area = Matrix4x4.Translate(new Vector3(0f, 0f, 5f)).inverse;
            Bind(Texture(Row(colours, AlpsPenlightShape.Line, 8f, area)));

            // Four slots over 8 m sit at -3, -1, 1 and 3 along X.
            AssertGlow(Color.green, new Vector3(-1f, 0f, 5f), "On a slot the penlight takes its colour.");
            AssertGlow(new Color(0f, 0.5f, 0.5f), new Vector3(0f, 0f, 5f), "Between two slots it blends them.");
            AssertGlow(new Color(0f, 0.25f, 0.75f), new Vector3(0.5f, 1.5f, 5f), "The blend follows the distance, whatever the height.");
            AssertGlow(Color.red, new Vector3(-20f, 0f, 5f), "Past the start it takes the first slot.");
            AssertGlow(Color.white, new Vector3(20f, 0f, 5f), "Past the end it takes the last slot.");
        }

        [Test]
        public void Shader_ReadsEveryShapeTheWayTheAreaDoes()
        {
            AlpsGpuShow.RequireGraphics();
            var colours = new[] { Color.red, Color.green, Color.blue, Color.white };
            var areaObject = Area(4);
            areaObject.transform.position = new Vector3(2f, 0f, 3f);
            areaObject.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            areaObject.size = 6f;
            var points = new[] { new Vector3(2.5f, 0f, 3.2f), new Vector3(4f, 1f, 6f), new Vector3(-1f, 0f, 1f), new Vector3(2f, 0f, 0f) };

            foreach (AlpsPenlightShape shape in System.Enum.GetValues(typeof(AlpsPenlightShape)))
            {
                areaObject.shape = shape;
                Bind(Texture(Row(colours, shape, areaObject.size, areaObject.transform.worldToLocalMatrix)));
                foreach (var point in points)
                {
                    var expected = Blend(colours, areaObject.PositionOf(point), shape == AlpsPenlightShape.Angle);
                    AssertGlow(expected, point, $"{shape} at {point}");
                }
            }
        }

        [Test]
        public void Shader_TakesTheBrightestArea()
        {
            AlpsGpuShow.RequireGraphics();
            var first = Row(new[] { new Color(0.2f, 0.8f, 0f) }, AlpsPenlightShape.Line, 1f, Matrix4x4.identity);
            var second = Row(new[] { new Color(0.6f, 0.1f, 0.3f) }, AlpsPenlightShape.Radial, 1f, Matrix4x4.identity);
            Bind(Texture(first, second));

            AssertGlow(new Color(0.6f, 0.8f, 0.3f), Vector3.zero, "Each channel takes the brighter area.");
        }

        [Test]
        public void Shader_GlowsInItsOwnColourWithoutAShow()
        {
            AlpsGpuShow.RequireGraphics();
            var own = new Color(0.3f, 0.6f, 0.9f);

            Shader.SetGlobalTexture(AlpsShowPlayer.PenlightTextureProperty, null);
            AssertGlow(own, Vector3.zero, "No world handed it a texture.", own);

            Bind(Texture());
            AssertGlow(own, Vector3.zero, "The show holds no penlight area.", own);

            Bind(Texture(Row(new[] { Color.black }, AlpsPenlightShape.Line, 1f, Matrix4x4.identity)));
            AssertGlow(Color.black, Vector3.zero, "A show that keeps the area dark keeps the penlight dark.", own);
        }

        // --- Helpers -----------------------------------------------------------------------

        private AlpsPenlightArea Area(int slots)
        {
            var area = new GameObject("Area").AddComponent<AlpsPenlightArea>();
            area.slots = slots;
            AlpsPenlightSlots.Sync(area, recordUndo: false);
            return area;
        }

        /// <summary>What a penlight at <paramref name="place"/> along <paramref name="colours"/> shows.</summary>
        private static Color Blend(Color[] colours, float place, bool wraps)
        {
            var count = colours.Length;
            var x = place * count - 0.5f;
            var first = Mathf.FloorToInt(x);
            var second = first + 1;
            if (wraps)
            {
                first = ((first % count) + count) % count;
                second = ((second % count) + count) % count;
            }
            else
            {
                first = Mathf.Clamp(first, 0, count - 1);
                second = Mathf.Clamp(second, 0, count - 1);
            }

            return Color.Lerp(colours[first], colours[second], x - Mathf.Floor(x));
        }

        /// <summary>One row of the penlight texture, the way the penlight grid pass lays an area out.</summary>
        private static Color[] Row(Color[] colours, AlpsPenlightShape shape, float size, Matrix4x4 worldToArea)
        {
            var row = new Color[AlpsShowPlayer.GpuPenlightWidth];
            for (var i = 0; i < colours.Length; i++)
            {
                row[i] = colours[i];
            }

            for (var r = 0; r < 3; r++)
            {
                var values = worldToArea.GetRow(r);
                row[Slots + r] = new Color(values.x, values.y, values.z, values.w);
            }

            row[Slots + 3] = new Color(colours.Length, (float)shape, size, 1f);
            return row;
        }

        private Texture2D Texture(params Color[][] rows)
        {
            var texture = new Texture2D(AlpsShowPlayer.GpuPenlightWidth, AlpsShowPlayer.GpuMaxPenlightAreas, TextureFormat.RGBAFloat, false, true);
            var pixels = new Color[texture.width * texture.height];
            for (var r = 0; r < rows.Length; r++)
            {
                rows[r].CopyTo(pixels, r * texture.width);
            }

            texture.SetPixels(pixels);
            texture.Apply(false);
            _created.Add(texture);
            return texture;
        }

        private static void Bind(Texture texture)
        {
            Shader.SetGlobalTexture(AlpsShowPlayer.PenlightTextureProperty, texture);
        }

        /// <summary>Renders a black penlight at <paramref name="position"/> and checks what it glows.</summary>
        private void AssertGlow(Color expected, Vector3 position, string message, Color? own = null)
        {
            var shader = Shader.Find("ALPS/Penlight");
            Assert.NotNull(shader, "The penlight shader did not compile.");
            var material = new Material(shader);
            material.SetColor("_Color", Color.black);
            material.SetColor("_EmissionColor", own ?? Color.magenta);
            _created.Add(material);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.position = position;
            quad.GetComponent<Renderer>().sharedMaterial = material;

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = position + new Vector3(0f, 0f, -2f);
            camera.orthographic = true;
            camera.orthographicSize = 0.25f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = true;
            var target = new RenderTexture(8, 8, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;

            var image = AlpsGpuShow.ReadBackTexture(target);
            var pixel = image.GetPixel(4, 4);
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(camera.gameObject);
            Object.DestroyImmediate(quad);

            Assert.AreEqual(expected.r, pixel.r, 2e-3f, message + " (red)");
            Assert.AreEqual(expected.g, pixel.g, 2e-3f, message + " (green)");
            Assert.AreEqual(expected.b, pixel.b, 2e-3f, message + " (blue)");
        }
    }
}
