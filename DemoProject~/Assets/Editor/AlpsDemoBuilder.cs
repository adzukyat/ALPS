using System.Linq;
using AdzukiSoft.ALPS;
using AdzukiSoft.ALPS.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// Rebuilds the demo from code: the venue (<see cref="AlpsDemoVenue"/>), the lighting rig
/// hung on it (<see cref="AlpsDemoRig"/>) and a three and a half minute show
/// (<see cref="AlpsDemoShow"/>). Everything below the scene's ALPS Demo object and under
/// Assets/Demo is written again on every run, so it is safe to run at any time, and what is
/// changed by hand there does not last.
/// </summary>
public static class AlpsDemoBuilder
{
    public const string ScenePath = "Assets/Scenes/VRCDefaultWorldScene.unity";
    public const string RootName = "ALPS Demo";
    public const string DirectorName = "Show";
    private const string WorldName = "VRCWorld";
    private const string PostProcessPath = AlpsDemoVenue.Folder + "/DemoPostProcess.asset";

    /// <summary>Where a visitor arrives and where the scene's camera stands: the mix position.</summary>
    private static readonly Vector3 Spawn = new Vector3(0f, 0.42f, AlpsDemoVenue.FohZ - 0.7f);
    private static readonly Vector3 CameraPosition = new Vector3(0f, 3.1f, AlpsDemoVenue.FohZ - 0.5f);
    private static readonly Vector3 CameraTarget = new Vector3(0f, 5.2f, 4f);

    [MenuItem(AlpsMainMenu.DebugRoot + "Demo/Rebuild Example", false, AlpsMainMenu.DebugPriority)]
    public static void Rebuild()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        RemoveLegacyAssets();

        // Everything but VRChat's world object is the demo's and is made again.
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != WorldName)
            {
                Object.DestroyImmediate(root);
            }
        }

        SetEnvironment();
        var demo = new GameObject(RootName);
        AlpsDemoVenue.Build(demo.transform);
        var targets = AlpsDemoRig.Build(demo.transform);
        var camera = AddCamera(demo.transform);
        AddDepthLight(demo.transform);

        var director = new GameObject(DirectorName).AddComponent<PlayableDirector>();
        director.transform.SetParent(demo.transform, false);
        director.playOnAwake = true;
        // Loop, since a director that ends with wrap mode None stops and a later seek freezes it.
        director.extrapolationMode = DirectorWrapMode.Loop;
        AlpsDemoShow.Build(director, targets);

        PlaceWorld(camera);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        // What a VRChat build would check before it converts the show.
        var show = AlpsShowCompiler.Compile(director);
        var errors = new System.Collections.Generic.List<string>(show.errors);
        var warnings = new System.Collections.Generic.List<string>(show.warnings);
        AlpsShowApplier.Validate(scene, errors, warnings);
        foreach (var error in errors.Distinct())
        {
            Debug.LogError("[ALPS Demo] " + error);
        }

        foreach (var warning in warnings.Distinct())
        {
            Debug.LogWarning("[ALPS Demo] " + warning);
        }

        var dmx = show.fixtures.Count(fixture => fixture != null && fixture.UsesDmxRow);
        Debug.Log($"[ALPS Demo] Rebuilt the example show: {show.fixtures.Count} fixtures ({dmx} on DMX rows), "
                  + $"{show.clips.Length / AlpsShowLayout.ClipStride} clips, {AlpsDemoShow.Duration:0} seconds.");
    }

    /// <summary>Batch mode entry point.</summary>
    public static void RebuildAndExit()
    {
        var failed = false;
        void OnLog(string condition, string stackTrace, LogType type)
        {
            failed |= type == LogType.Error || type == LogType.Exception;
        }

        Application.logMessageReceived += OnLog;
        try
        {
            Rebuild();
        }
        catch (System.Exception exception)
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

    /// <summary>The demo's director in the open scene, or null.</summary>
    public static PlayableDirector FindDirector()
    {
        var demo = GameObject.Find(RootName);
        return demo != null ? demo.GetComponentInChildren<PlayableDirector>(true) : null;
    }

    private static void RemoveLegacyAssets()
    {
        const string legacySettingsPath = "Assets/StageLightManeuverSettings.asset";
        if (AssetDatabase.LoadAssetAtPath<Object>(legacySettingsPath) != null)
        {
            AssetDatabase.DeleteAsset(legacySettingsPath);
        }
    }

    /// <summary>A dark room: no sky, next to no ambient light and nothing to reflect.</summary>
    private static void SetEnvironment()
    {
        RenderSettings.skybox = null;
        RenderSettings.sun = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.012f, 0.013f, 0.02f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflection = null;
        RenderSettings.reflectionIntensity = 0f;
        RenderSettings.fog = false;
        Lightmapping.realtimeGI = false;
        Lightmapping.bakedGI = false;
    }

    private static Camera AddCamera(Transform parent)
    {
        var camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.gameObject.tag = "MainCamera";
        camera.transform.SetParent(parent, false);
        camera.transform.position = CameraPosition;
        camera.transform.LookAt(CameraTarget);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.fieldOfView = 50f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 200f;
        camera.allowHDR = true;
        camera.gameObject.AddComponent<AudioListener>();

        // Bloom is what makes a lit lens and a beam read as light. VRChat takes these settings
        // from the scene descriptor's reference camera.
        var profile = AlpsDemoVenue.Asset<PostProcessProfile>(PostProcessPath, ScriptableObject.CreateInstance<PostProcessProfile>);
        var bloom = Setting<Bloom>(profile);
        bloom.enabled.Override(true);
        bloom.intensity.Override(2.2f);
        bloom.threshold.Override(1.05f);
        bloom.softKnee.Override(0.6f);
        bloom.diffusion.Override(6.5f);
        var grading = Setting<ColorGrading>(profile);
        grading.enabled.Override(true);
        grading.tonemapper.Override(Tonemapper.Neutral);
        EditorUtility.SetDirty(profile);

        var volume = camera.gameObject.AddComponent<PostProcessVolume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;

        var resources = AssetDatabase.FindAssets("t:PostProcessResources")
            .Select(guid => AssetDatabase.LoadAssetAtPath<PostProcessResources>(AssetDatabase.GUIDToAssetPath(guid)))
            .FirstOrDefault(found => found != null);
        if (resources != null)
        {
            var layer = camera.gameObject.AddComponent<PostProcessLayer>();
            layer.Init(resources);
            layer.volumeTrigger = camera.transform;
            layer.volumeLayer = 1 << camera.gameObject.layer;
        }

        return camera;
    }

    /// <summary>The profile's settings of one effect, added to its asset the first time.</summary>
    private static T Setting<T>(PostProcessProfile profile) where T : PostProcessEffectSettings
    {
        var setting = profile.GetSetting<T>();
        if (setting == null)
        {
            setting = profile.AddSettings<T>();
            setting.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(setting, profile);
        }

        return setting;
    }

    /// <summary>
    /// A directional light that casts shadows from nothing. VRSL's cones fade out where they
    /// meet the scene, which they read from the camera's depth texture, and in the built-in
    /// pipeline it is a shadow casting directional light that makes Unity draw one.
    /// </summary>
    private static void AddDepthLight(Transform parent)
    {
        var light = new GameObject("Depth Light").AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        light.type = LightType.Directional;
        light.intensity = 0.01f;
        light.color = Color.black;
        light.shadows = LightShadows.Hard;
        light.cullingMask = 1 << LayerMask.NameToLayer("Water");
    }

    /// <summary>Moves VRChat's world object, which is the spawn, and hands it the camera to copy.</summary>
    private static void PlaceWorld(Camera camera)
    {
        var world = GameObject.Find(WorldName);
        if (world == null)
        {
            Debug.LogWarning("[ALPS Demo] The scene has no VRCWorld object, so it has no spawn.");
            return;
        }

        world.transform.position = Spawn;
        world.transform.rotation = Quaternion.identity;
        foreach (var behaviour in world.GetComponents<MonoBehaviour>())
        {
            if (behaviour == null)
            {
                continue;
            }

            var serialized = new SerializedObject(behaviour);
            var reference = serialized.FindProperty("ReferenceCamera");
            if (reference != null && reference.propertyType == SerializedPropertyType.ObjectReference)
            {
                reference.objectReferenceValue = camera.gameObject;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
