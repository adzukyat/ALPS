using System.Collections.Generic;
using System.IO;
using ManeuverForVRC.Editor;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace ManeuverForVRC.Tests
{
    public class MfvPackageMetadataTests
    {
        [Test]
        public void PackageImportableFiles_AllHaveMetaFiles()
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(MfvShowSetup).Assembly);
            Assert.NotNull(packageInfo, "Could not resolve the Maneuver For VRC package from its editor assembly.");

            var missing = new List<string>();
            CollectMissingMetaFiles(packageInfo.resolvedPath, packageInfo.resolvedPath, missing);

            Assert.That(missing, Is.Empty, "Missing .meta files:\n" + string.Join("\n", missing));
        }

        private static void CollectMissingMetaFiles(string root, string path, List<string> missing)
        {
            foreach (var directory in Directory.GetDirectories(path))
            {
                if (ShouldIgnore(directory))
                {
                    continue;
                }

                RequireMeta(root, directory, missing);
                CollectMissingMetaFiles(root, directory, missing);
            }

            foreach (var file in Directory.GetFiles(path))
            {
                if (ShouldIgnore(file) || file.EndsWith(".meta"))
                {
                    continue;
                }

                RequireMeta(root, file, missing);
            }
        }

        private static bool ShouldIgnore(string path)
        {
            var name = Path.GetFileName(path);
            return name.StartsWith(".") || name.EndsWith("~");
        }

        private static void RequireMeta(string root, string path, List<string> missing)
        {
            if (File.Exists(path + ".meta"))
            {
                return;
            }

            missing.Add(path.Substring(root.Length + 1));
        }
    }
}
