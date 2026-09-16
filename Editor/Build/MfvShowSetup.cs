using System.Reflection;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;

namespace ManeuverForVRC.Editor
{
    /// <summary>
    /// Puts the Udon player next to a director. The player stays inactive in the authoring
    /// scene and is filled and switched on only in the scene VRChat builds or plays.
    /// </summary>
    public static class MfvShowSetup
    {
        public const string PlayerName = "MFV Show Player";
        public const string GeneratedFolder = "Assets/ManeuverForVRC";
        public const string ProgramAssetFolder = GeneratedFolder + "/UdonSharpPrograms";
        public const string ProgramAssetPath = ProgramAssetFolder + "/MfvShowPlayer.asset";

        [MenuItem("ManeuverForVRC/Set Up Show Player")]
        public static void SetUpSelectedDirector()
        {
            var director = GetSelectedDirector();
            if (director == null)
            {
                Debug.LogError("[ManeuverForVRC] Select a GameObject with a PlayableDirector first.");
                return;
            }

            var player = GetOrCreatePlayer(director);
            EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            EditorGUIUtility.PingObject(player);
        }

        [MenuItem("ManeuverForVRC/Set Up Show Player", true)]
        public static bool ValidateSetUpSelectedDirector()
        {
            return GetSelectedDirector() != null;
        }

        /// <summary>The player that belongs to <paramref name="director"/>, or null.</summary>
        public static MfvShowPlayer FindPlayer(PlayableDirector director)
        {
            if (director == null)
            {
                return null;
            }

            foreach (var player in director.GetComponentsInChildren<MfvShowPlayer>(true))
            {
                if (player.director == director)
                {
                    return player;
                }
            }

            return null;
        }

        public static MfvShowPlayer GetOrCreatePlayer(PlayableDirector director)
        {
            var player = FindPlayer(director);
            if (player != null)
            {
                return player;
            }

            var playerObject = new GameObject(PlayerName);
            Undo.RegisterCreatedObjectUndo(playerObject, "Create MFV Show Player");
            playerObject.transform.SetParent(director.transform, false);

            EnsureProgramAsset();
            player = UdonSharpUndo.AddComponent<MfvShowPlayer>(playerObject);
            player.director = director;
            UdonSharpEditorUtility.CopyProxyToUdon(player);

            // Inactive until a build or play mode fills it, so it never fights the preview.
            playerObject.SetActive(false);
            return player;
        }

        public static UdonSharpProgramAsset EnsureProgramAsset()
        {
            var programAsset = UdonSharpProgramAsset.GetProgramAssetForClass(typeof(MfvShowPlayer));
            if (programAsset == null)
            {
                programAsset = CreateProgramAsset();
            }

            if (programAsset == null)
            {
                Debug.LogError("[ManeuverForVRC] Failed to create the UdonSharp program asset for MfvShowPlayer.");
                return null;
            }

            if (programAsset.ScriptVersion < UdonSharpProgramVersion.CurrentVersion)
            {
                programAsset.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
            }

            if (programAsset.CompiledVersion < UdonSharpProgramVersion.CurrentVersion)
            {
                UdonSharpCompilerV1.CompileSync();
            }

            return programAsset;
        }

        private static UdonSharpProgramAsset CreateProgramAsset()
        {
            MonoScript sourceScript = null;
            foreach (var guid in AssetDatabase.FindAssets(nameof(MfvShowPlayer) + " t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.GetClass() == typeof(MfvShowPlayer))
                {
                    sourceScript = script;
                    break;
                }
            }

            if (sourceScript == null)
            {
                Debug.LogError("[ManeuverForVRC] Could not locate the MfvShowPlayer script.");
                return null;
            }

            EnsureFolder(ProgramAssetFolder);
            var programAsset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramAssetPath);
            if (programAsset == null)
            {
                programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                programAsset.name = nameof(MfvShowPlayer);
                programAsset.sourceCsScript = sourceScript;
                AssetDatabase.CreateAsset(programAsset, ProgramAssetPath);
            }
            else
            {
                programAsset.sourceCsScript = sourceScript;
            }

            programAsset.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
            EditorUtility.SetDirty(programAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(ProgramAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            // UdonSharp caches the class to program asset lookup, so a new asset needs the cache cleared.
            typeof(UdonSharpProgramAsset)
                .GetMethod("ClearProgramAssetCache", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, null);

            return UdonSharpProgramAsset.GetProgramAssetForClass(typeof(MfvShowPlayer))
                ?? AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramAssetPath);
        }

        public static void EnsureFolder(string folder)
        {
            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static PlayableDirector GetSelectedDirector()
        {
            if (Selection.activeObject is PlayableDirector selectedDirector)
            {
                return selectedDirector;
            }

            return Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<PlayableDirector>() : null;
        }
    }
}
