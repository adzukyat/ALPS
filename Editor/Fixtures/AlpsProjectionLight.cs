using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Draws VRSL's projections as light added to what is drawn, so overlapping ones add up
    /// instead of multiplying. VRSL's projection shaders blend with
    /// <c>Blend DstColor [_BlendDst]</c>, which scales the colour already drawn by one plus the
    /// projection, so each projection on a pixel multiplies the ones before it. ALPS writes a
    /// copy of a projection shader that blends with <c>Blend One One</c> instead, made from the
    /// installed VRSL's own source so it follows VRSL's version, and the projection materials
    /// of the fixtures ALPS adds draw with it (<see cref="AlpsFixtureMaterials"/>).
    /// </summary>
    public static class AlpsProjectionLight
    {
        public const string ShaderFolder = AlpsShowSetup.GeneratedFolder + "/Shaders";

        /// <summary>
        /// What the projections' intensity is scaled by. VRSL's value scales the colour already
        /// drawn, so added as light it is too high by that colour's brightness: on VRChat's
        /// default floor one projection adds about what it did before. A darker floor gets more
        /// light than before, a brighter one less.
        /// </summary>
        public const float IntensityScale = 0.1f;

        /// <summary>The intensity of the movers' projections and of the static lights'.</summary>
        private static readonly string[] IntensityProperties = { "_ProjectionIntensity", "_ProjectionMaxIntensity" };

        private const string VariantPrefix = "Hidden/ALPS/";
        private const string Header = "// Made by ALPS from ";
        private static readonly Regex Blend = new Regex(@"Blend\s+DstColor\s+\[_BlendDst\]");
        private static readonly Regex ShaderName = new Regex("Shader\\s+\"[^\"]+\"");
        private static readonly Regex Include = new Regex("#include\\s+\"([^\"]+)\"");

        /// <summary>A VRSL projection shader, or the added light copy of one.</summary>
        public static bool IsProjection(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            var name = shader.name.StartsWith(VariantPrefix) ? shader.name.Substring(VariantPrefix.Length) : shader.name;
            return name.StartsWith("VRSL/") && name.EndsWith("/Projection");
        }

        /// <summary>
        /// A copy of VRSL's projection material <paramref name="source"/> drawn as added light,
        /// keeping its render queue, with its intensity scaled by <see cref="IntensityScale"/>.
        /// Null for any other material, for a projection VRSL draws dithered
        /// (<c>_ALPHATEST_ON</c>), which replaces the colour drawn instead, and for one whose
        /// shader does not blend the VRSL way.
        /// </summary>
        public static Material CreateLight(Material source)
        {
            if (source == null || !IsVrslProjection(source.shader) || source.IsKeywordEnabled("_ALPHATEST_ON"))
            {
                return null;
            }

            var variant = Variant(source.shader, true);
            if (variant == null)
            {
                return null;
            }

            var light = new Material(source) { name = source.name };
            light.shader = variant;
            light.renderQueue = source.renderQueue;
            foreach (var property in IntensityProperties)
            {
                if (light.HasProperty(property))
                {
                    light.SetFloat(property, light.GetFloat(property) * IntensityScale);
                }
            }

            return light;
        }

        /// <summary>
        /// The added light copy of a VRSL projection shader, or null when it cannot blend as
        /// light or, unless <paramref name="create"/> allows writing it, is missing. Written
        /// again whenever VRSL's source changes.
        /// </summary>
        public static Shader Variant(Shader source, bool create)
        {
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
            {
                return null;
            }

            var path = VariantPath(source);
            if (create && !Write(sourcePath, source.name, path))
            {
                return null;
            }

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader != null && ShaderUtil.ShaderHasError(shader))
            {
                if (create)
                {
                    Debug.LogError($"[ALPS] The added light copy of '{source.name}' does not compile, so it is not used.");
                }

                return null;
            }

            return shader;
        }

        /// <summary>
        /// Writes the copies again whose VRSL source is among <paramref name="imported"/>, so
        /// they follow a VRSL update. A source that no longer blends the VRSL way keeps the
        /// copy it had, which still blends as light.
        /// </summary>
        public static void Refresh(IEnumerable<string> imported)
        {
            var sources = new HashSet<string>();
            foreach (var path in imported)
            {
                if (path.EndsWith(".shader") && !AlpsShowSetup.IsInFolder(path, ShaderFolder))
                {
                    sources.Add(path);
                }
            }

            if (sources.Count == 0 || !AssetDatabase.IsValidFolder(ShaderFolder))
            {
                return;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { ShaderFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var sourcePath = SourceOf(path);
                var source = sourcePath != null && sources.Contains(sourcePath) ? AssetDatabase.LoadAssetAtPath<Shader>(sourcePath) : null;
                if (source != null && IsVrslProjection(source))
                {
                    Write(sourcePath, source.name, path);
                }
            }
        }

        /// <summary>One of VRSL's own projection shaders, not a copy.</summary>
        public static bool IsVrslProjection(Shader shader)
        {
            return IsProjection(shader) && !shader.name.StartsWith(VariantPrefix);
        }

        private static string VariantPath(Shader source)
        {
            return $"{ShaderFolder}/{source.name.Replace('/', ' ')}.shader";
        }

        /// <summary>
        /// Writes the copy of the shader at <paramref name="sourcePath"/> to
        /// <paramref name="path"/> when it changed. False when the source does not blend the
        /// VRSL way and no copy is there.
        /// </summary>
        private static bool Write(string sourcePath, string name, string path)
        {
            var text = LightSource(File.ReadAllText(FileUtil.GetPhysicalPath(sourcePath)), sourcePath, name);
            var file = FileUtil.GetPhysicalPath(path);
            if (text == null)
            {
                return File.Exists(file);
            }

            if (!File.Exists(file) || File.ReadAllText(file) != text)
            {
                AlpsShowSetup.EnsureFolder(ShaderFolder);
                File.WriteAllText(file, text);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }

            return true;
        }

        /// <summary>The VRSL shader the copy at <paramref name="path"/> was made from, read from its first line.</summary>
        private static string SourceOf(string path)
        {
            var file = FileUtil.GetPhysicalPath(path);
            if (!File.Exists(file))
            {
                return null;
            }

            using (var reader = new StreamReader(file))
            {
                var line = reader.ReadLine();
                if (line == null || !line.StartsWith(Header))
                {
                    return null;
                }

                var end = line.IndexOf(", ", Header.Length, System.StringComparison.Ordinal);
                return end < 0 ? null : line.Substring(Header.Length, end - Header.Length);
            }
        }

        /// <summary>
        /// VRSL's shader source blending as light, named under <see cref="VariantPrefix"/> and
        /// with its includes reaching VRSL's folder, or null when nothing blends the VRSL way.
        /// </summary>
        private static string LightSource(string text, string sourcePath, string name)
        {
            if (!Blend.IsMatch(text))
            {
                return null;
            }

            var folder = Path.GetDirectoryName(sourcePath).Replace('\\', '/');
            text = Include.Replace(text, match =>
            {
                var resolved = Normalize(folder + "/" + match.Groups[1].Value);
                return File.Exists(FileUtil.GetPhysicalPath(resolved)) ? $"#include \"{resolved}\"" : match.Value;
            });
            text = ShaderName.Replace(text, $"Shader \"{VariantPrefix}{name}\"", 1);
            text = Blend.Replace(text, "Blend One One");
            // The first line ends the way VRSL's do, or Unity warns about mixed line endings.
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            return $"{Header}{sourcePath}, with its projection added as light.{newline}{text}";
        }

        private static string Normalize(string path)
        {
            var parts = new List<string>();
            foreach (var part in path.Split('/'))
            {
                if (part == ".." && parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
                else if (part != ".")
                {
                    parts.Add(part);
                }
            }

            return string.Join("/", parts);
        }
    }

    /// <summary>Writes the added light copies again when VRSL's shaders are imported, such as after a VRSL update.</summary>
    internal class AlpsProjectionShaderWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Length == 0)
            {
                return;
            }

            // Importing from inside the postprocessor would nest imports.
            EditorApplication.delayCall += () => AlpsProjectionLight.Refresh(imported);
        }
    }
}
