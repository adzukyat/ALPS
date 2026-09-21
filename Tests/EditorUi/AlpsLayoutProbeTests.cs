using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using AdzukiSoft.ALPS.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Diagnostic: writes the inspector's computed geometry (boxes, resolved colours,
    /// text, and the painter2D paths) to a TSV for inspection outside Unity. Always
    /// passes, since it is a dump rather than a gate.
    /// The gate is <see cref="AlpsLayoutAuditTests"/>.
    /// </summary>
    public class AlpsLayoutProbeTests
    {
        private const string DumpRelativePath = "../layout-dump.tsv";
        private const string AssetRelativePath = "../layout-assets";

        [UnityTest]
        public IEnumerator Dump_ComputedLayout()
        {
            var set = AlpsInspectorFixture.Build();

            var window = ScriptableObject.CreateInstance<ProbeWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
            window.minSize = new Vector2(476f, 3200f);
            window.maxSize = new Vector2(476f, 3200f);
            window.ShowUtility();
            window.position = new Rect(0f, 0f, 476f, 3200f);

            var view = new AlpsClipInspectorView(set);
            window.rootVisualElement.Add(view);

            for (var i = 0; i < 16; i++)
            {
                window.Repaint();
                yield return null;
            }

            var builder = new StringBuilder();
            builder.Append("type\tclasses\tx\ty\tw\th\tbg\tborder\tbw\tradius\tcolor\tfs\ttext\tbgimg\tpaths\n");

            _assetDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, AssetRelativePath));
            Directory.CreateDirectory(_assetDirectory);
            Write(builder, view, view.worldBound.position);

            var path = Path.GetFullPath(Path.Combine(Application.dataPath, DumpRelativePath));
            File.WriteAllText(path, builder.ToString());
            Debug.Log($"[layout-dump] {path}");

            window.Close();
            Object.DestroyImmediate(window);
        }

        private static void Write(StringBuilder builder, VisualElement element, Vector2 origin)
        {
            var style = element.resolvedStyle;
            if (style.display == DisplayStyle.None)
            {
                return;
            }

            var world = element.worldBound;
            var culture = CultureInfo.InvariantCulture;

            builder.Append(element.GetType().Name).Append('\t');
            builder.Append(string.Join(".", element.GetClasses())).Append('\t');
            builder.Append((world.x - origin.x).ToString("0.##", culture)).Append('\t');
            builder.Append((world.y - origin.y).ToString("0.##", culture)).Append('\t');
            builder.Append(world.width.ToString("0.##", culture)).Append('\t');
            builder.Append(world.height.ToString("0.##", culture)).Append('\t');
            builder.Append(ColorUtility.ToHtmlStringRGBA(style.backgroundColor)).Append('\t');
            builder.Append(ColorUtility.ToHtmlStringRGBA(style.borderTopColor)).Append('\t');
            builder.Append(style.borderTopWidth.ToString("0.##", culture)).Append('\t');
            builder.Append(style.borderTopLeftRadius.ToString("0.##", culture)).Append('\t');
            builder.Append(ColorUtility.ToHtmlStringRGBA(style.color)).Append('\t');
            builder.Append(style.fontSize.ToString("0.##", culture)).Append('\t');

            var text = element is TextElement textElement ? textElement.text : string.Empty;
            builder.Append((text ?? string.Empty).Replace("\t", " ").Replace("\n", " ")).Append('\t');
            builder.Append(DescribeImage(element, style)).Append('\t');
            builder.Append(DescribePaths(element, world.position - origin)).Append('\n');

            foreach (var child in element.Children())
            {
                Write(builder, child, origin);
            }
        }

        private static string _assetDirectory;

        /// <summary>
        /// Background images and Image contents are exported as PNGs beside the dump,
        /// so the gobo textures, the gradient palette swatch and the brightness ramp are
        /// available alongside the geometry.
        /// </summary>
        private static string DescribeImage(VisualElement element, IResolvedStyle style)
        {
            if (element is Image image && image.image != null)
            {
                return ExportTexture(image.image);
            }

            var background = style.backgroundImage;
            if (background.texture != null)
            {
                return ExportTexture(background.texture);
            }

            if (background.sprite != null && background.sprite.texture != null)
            {
                return ExportTexture(background.sprite.texture);
            }

            return string.Empty;
        }

        private static string ExportTexture(Texture texture)
        {
            var name = $"{texture.name}_{texture.GetInstanceID()}";
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            var file = Path.Combine(_assetDirectory, name + ".png");
            if (File.Exists(file))
            {
                return name + ".png";
            }

            // The source may be non-readable (the packaged gobos are), so round-trip it
            // through a RenderTexture rather than calling GetPixels on it directly.
            var target = RenderTexture.GetTemporary(
                texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            Graphics.Blit(texture, target);
            RenderTexture.active = target;

            var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
            copy.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);

            File.WriteAllBytes(file, copy.EncodeToPNG());
            Object.DestroyImmediate(copy);
            return name + ".png";
        }

        /// <summary>
        /// Serialises painter2D geometry as "w,RRGGBBAA,fill:x,y x,y|w,RRGGBBAA,fill:…",
        /// one entry per stroked or filled contour with the colour actually painted.
        /// </summary>
        private static string DescribePaths(VisualElement element, Vector2 offset)
        {
            if (element is AlpsVectorIcon icon &&
                icon.TryGetLocalGeometry(out var contours, out var strokeWidth))
            {
                var builder = new StringBuilder();
                for (var i = 0; i < contours.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append('|');
                    }

                    AppendContour(builder, contours[i], icon.IconColor, strokeWidth, icon.Filled, offset);
                }

                return builder.ToString();
            }

            if (element is AlpsPhaseGraph graph)
            {
                var strokes = graph.GetLocalStrokes();
                var builder = new StringBuilder();
                for (var i = 0; i < strokes.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append('|');
                    }

                    AppendContour(builder, strokes[i].Points, strokes[i].Color, strokes[i].Width, false, offset);
                }

                return builder.ToString();
            }

            return string.Empty;
        }

        private static void AppendContour(
            StringBuilder builder,
            System.Collections.Generic.IReadOnlyList<Vector2> points,
            Color color,
            float width,
            bool filled,
            Vector2 offset)
        {
            var culture = CultureInfo.InvariantCulture;
            builder.Append(width.ToString("0.##", culture)).Append(',');
            builder.Append(ColorUtility.ToHtmlStringRGBA(color)).Append(',');
            builder.Append(filled ? '1' : '0').Append(':');

            foreach (var point in points)
            {
                builder.Append((point.x + offset.x).ToString("0.#", culture)).Append(',')
                    .Append((point.y + offset.y).ToString("0.#", culture)).Append(' ');
            }
        }

        private sealed class ProbeWindow : EditorWindow
        {
        }
    }
}
