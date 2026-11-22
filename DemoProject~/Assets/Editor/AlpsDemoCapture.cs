using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AdzukiSoft.ALPS.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Renders moments of the demo show the way the Timeline preview draws them, as stills and
/// as contact sheets under TestResults~/demo, to look through a whole show without playing it.
///
/// In batch mode the environment picks what is drawn: ALPS_DEMO_TIMES is a list of seconds
/// ("12.5,40,41"), or of bars with a b in front ("b17,b17.5"), and without it every section
/// is drawn a few times. ALPS_DEMO_VIEW is one of the views below, and ALPS_DEMO_SIZE the
/// width of a still in pixels.
/// </summary>
public static class AlpsDemoCapture
{
    private const int Columns = 3;
    private const int Rows = 3;

    private static readonly Dictionary<string, (Vector3 position, Vector3 target, float fov)> Views =
        new Dictionary<string, (Vector3, Vector3, float)>
        {
            { "house", (new Vector3(0f, 3.1f, AlpsDemoVenue.FohZ - 0.5f), new Vector3(0f, 5.2f, 4f), 50f) },
            { "crowd", (new Vector3(-4.5f, 2f, -11.5f), new Vector3(0.5f, 5f, 4f), 62f) },
            { "side", (new Vector3(-17f, 5f, -7f), new Vector3(0f, 4.5f, 4f), 55f) },
            { "top", (new Vector3(0f, 12.5f, -3f), new Vector3(0f, 1.4f, 3.5f), 75f) },
            { "stage", (new Vector3(3f, 3.2f, 1.5f), new Vector3(-1f, 4.5f, 8f), 70f) },
            { "back", (new Vector3(0f, 5.5f, 9.5f), new Vector3(0f, 1.5f, -10f), 70f) },
            { "riser", (new Vector3(0f, 2.3f, 1.6f), new Vector3(0f, 1.75f, 5.4f), 62f) },
        };

    [MenuItem(AlpsMainMenu.DebugRoot + "Demo/Capture Stills", false, AlpsMainMenu.DebugPriority + 1)]
    public static void Capture()
    {
        var times = Times(Environment.GetEnvironmentVariable("ALPS_DEMO_TIMES"));
        var view = Environment.GetEnvironmentVariable("ALPS_DEMO_VIEW");
        if (string.IsNullOrEmpty(view) || !Views.ContainsKey(view))
        {
            view = "house";
        }

        var width = 640;
        if (int.TryParse(Environment.GetEnvironmentVariable("ALPS_DEMO_SIZE"), out var asked) && asked >= 160)
        {
            width = asked;
        }

        Capture(times, view, width);
    }

    /// <summary>Batch mode entry point.</summary>
    public static void CaptureAndExit()
    {
        try
        {
            Capture();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>Batch mode entry point: rebuilds the demo, then draws it.</summary>
    public static void RebuildAndCaptureAndExit()
    {
        var failed = false;
        void OnLog(string condition, string stackTrace, LogType type)
        {
            failed |= type == LogType.Error || type == LogType.Exception;
        }

        Application.logMessageReceived += OnLog;
        try
        {
            AlpsDemoBuilder.Rebuild();
            Capture();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            failed = true;
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
        }

        EditorApplication.Exit(failed ? 1 : 0);
    }

    public static void Capture(List<double> times, string view, int width)
    {
        var asyncCompilation = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        if (EditorSceneManager.GetActiveScene().path != AlpsDemoBuilder.ScenePath)
        {
            EditorSceneManager.OpenScene(AlpsDemoBuilder.ScenePath, OpenSceneMode.Single);
        }

        var director = AlpsDemoBuilder.FindDirector();
        if (director == null)
        {
            throw new InvalidOperationException("The demo scene has no show. Run Rebuild Example first.");
        }

        var main = Camera.main;
        var camera = UnityEngine.Object.Instantiate(main.gameObject).GetComponent<Camera>();
        camera.gameObject.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DestroyImmediate(camera.GetComponent<AudioListener>());
        var (position, target, fov) = Views[view];
        camera.transform.position = position;
        camera.transform.LookAt(target);
        camera.fieldOfView = fov;
        camera.depthTextureMode = DepthTextureMode.Depth;

        var height = width * 9 / 16;
        var folder = Path.Combine(Application.dataPath, "..", "TestResults~", "demo");
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, view + "-*.jpg"))
        {
            File.Delete(old);
        }

        var wrap = director.extrapolationMode;
        var update = director.timeUpdateMode;
        director.timeUpdateMode = DirectorUpdateMode.Manual;
        director.RebuildGraph();

        var sheet = new Texture2D(width * Columns, height * Rows, TextureFormat.RGB24, false);
        // Post processing works in the format of the camera's target, so the camera draws into
        // an HDR one: in 8 bits nothing is past white and the bloom finds nothing to spread.
        var drawn = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR);
        var target2 = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        var still = new Texture2D(width, height, TextureFormat.RGB24, false);
        var log = new List<string>();
        try
        {
            var onSheet = 0;
            var sheets = 0;
            for (var i = 0; i < times.Count; i++)
            {
                director.time = times[i];
                director.Evaluate();

                camera.targetTexture = drawn;
                camera.Render();
                Graphics.Blit(drawn, target2);
                var previous = RenderTexture.active;
                RenderTexture.active = target2;
                still.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                still.Apply();
                RenderTexture.active = previous;
                camera.targetTexture = null;

                if (times.Count <= 2)
                {
                    File.WriteAllBytes(Path.Combine(folder, $"{view}-{times[i]:000.00}s.jpg"), still.EncodeToJPG(88));
                }

                // Sheets read left to right, top to bottom, and a texture's rows count from the bottom.
                var column = onSheet % Columns;
                var row = Rows - 1 - onSheet / Columns;
                sheet.SetPixels(column * width, row * height, width, height, still.GetPixels());
                log.Add($"sheet {sheets + 1} tile {onSheet + 1}: {times[i]:0.00}s  bar {times[i] / AlpsDemoShow.Seconds(1) + 1:0.00}  {Section(times[i])}");
                onSheet++;
                if (onSheet == Columns * Rows || i == times.Count - 1)
                {
                    sheet.Apply();
                    sheets++;
                    File.WriteAllBytes(Path.Combine(folder, $"{view}-sheet-{sheets:00}.jpg"), sheet.EncodeToJPG(85));
                    sheet.SetPixels(new Color[sheet.width * sheet.height]);
                    onSheet = 0;
                }
            }
        }
        finally
        {
            director.time = 0.0;
            director.Evaluate();
            director.timeUpdateMode = update;
            director.extrapolationMode = wrap;
            ShaderUtil.allowAsyncCompilation = asyncCompilation;
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            UnityEngine.Object.DestroyImmediate(sheet);
            UnityEngine.Object.DestroyImmediate(still);
            UnityEngine.Object.DestroyImmediate(target2);
            UnityEngine.Object.DestroyImmediate(drawn);
        }

        File.WriteAllLines(Path.Combine(folder, view + "-index.txt"), log);
        Debug.Log($"[ALPS Demo] Wrote {times.Count} stills of the '{view}' view to {folder}.");
    }

    private static string Section(double seconds)
    {
        var bar = seconds / AlpsDemoShow.Seconds(1) + 1.0;
        var name = "after the show";
        foreach (var section in AlpsDemoShow.Sections)
        {
            if (bar >= section.bar && bar < section.bar + section.bars)
            {
                name = section.name;
            }
        }

        return name;
    }

    /// <summary>The asked moments, or two or three per section, set a little off the bar lines.</summary>
    private static List<double> Times(string asked)
    {
        var times = new List<double>();
        if (!string.IsNullOrEmpty(asked))
        {
            foreach (var part in asked.Split(','))
            {
                var text = part.Trim();
                var isBar = text.StartsWith("b");
                if (double.TryParse(isBar ? text.Substring(1) : text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    times.Add(isBar ? AlpsDemoShow.Seconds(value - 1.0) : value);
                }
            }

            return times;
        }

        foreach (var section in AlpsDemoShow.Sections)
        {
            var count = section.bars >= 16 ? 3 : 2;
            for (var i = 0; i < count; i++)
            {
                times.Add(AlpsDemoShow.Seconds(section.bar - 1 + section.bars * (i + 0.6) / count));
            }
        }

        return times;
    }
}
