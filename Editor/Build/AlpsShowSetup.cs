using System.Reflection;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Puts the Udon player under a director. It is created only in the scene VRChat builds
    /// or plays, so the authoring scene never holds one.
    /// </summary>
    public static class AlpsShowSetup
    {
        public const string PlayerName = "ALPS Show Player";
        public const string GeneratedFolder = "Assets/ALPS";
        public const string ProgramAssetFolder = GeneratedFolder + "/UdonSharpPrograms";
        public const string ProgramAssetPath = ProgramAssetFolder + "/AlpsShowPlayer.asset";

        /// <summary>The player that belongs to <paramref name="director"/>, or null.</summary>
        public static AlpsShowPlayer FindPlayer(PlayableDirector director)
        {
            if (director == null)
            {
                return null;
            }

            foreach (var player in director.GetComponentsInChildren<AlpsShowPlayer>(true))
            {
                if (player.director == director)
                {
                    return player;
                }
            }

            return null;
        }

        /// <summary>
        /// The player of <paramref name="director"/>, created when it has none. A player an
        /// older version left in the authoring scene is reused. Returns null when the Udon
        /// program for the player cannot be made.
        /// </summary>
        public static AlpsShowPlayer GetOrCreatePlayer(PlayableDirector director)
        {
            var player = FindPlayer(director);
            if (player != null)
            {
                return player;
            }

            if (EnsureProgramAsset() == null)
            {
                return null;
            }

            // Created inactive, so nothing runs before the show is copied in.
            var playerObject = new GameObject(PlayerName);
            playerObject.SetActive(false);
            playerObject.transform.SetParent(director.transform, false);

            player = UdonSharpUndo.AddComponent<AlpsShowPlayer>(playerObject);
            player.director = director;
            UdonSharpEditorUtility.CopyProxyToUdon(player);

            // Only the scene copy is touched, so the add needs no undo step.
            Undo.ClearUndo(playerObject);
            return player;
        }

        public static UdonSharpProgramAsset EnsureProgramAsset()
        {
            var programAsset = UdonSharpProgramAsset.GetProgramAssetForClass(typeof(AlpsShowPlayer));
            if (programAsset == null)
            {
                programAsset = CreateProgramAsset();
            }

            if (programAsset == null)
            {
                Debug.LogError("[ALPS] Failed to create the UdonSharp program asset for AlpsShowPlayer.");
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
            foreach (var guid in AssetDatabase.FindAssets(nameof(AlpsShowPlayer) + " t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.GetClass() == typeof(AlpsShowPlayer))
                {
                    sourceScript = script;
                    break;
                }
            }

            if (sourceScript == null)
            {
                Debug.LogError("[ALPS] Could not locate the AlpsShowPlayer script.");
                return null;
            }

            EnsureFolder(ProgramAssetFolder);
            var programAsset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramAssetPath);
            if (programAsset == null)
            {
                programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                programAsset.name = nameof(AlpsShowPlayer);
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

            return UdonSharpProgramAsset.GetProgramAssetForClass(typeof(AlpsShowPlayer))
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
    }
}
