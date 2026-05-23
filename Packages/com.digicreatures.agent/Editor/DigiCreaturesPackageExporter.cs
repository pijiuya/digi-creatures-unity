using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DigiCreaturesEditor
{
    public static class DigiCreaturesPackageExporter
    {
        private const string PackageRoot = "Packages/com.digicreatures.agent";
        private const string ExportStagingRoot = "Assets/__DigiCreaturesAgentExport";
        private const string ExportFolder = "Builds";

        [MenuItem("DigiCreatures/Export UnityPackage")]
        [MenuItem("数字生物/高级设置/导出 UnityPackage")]
        public static void ExportUnityPackage()
        {
            if (!Directory.Exists(PackageRoot))
            {
                EditorUtility.DisplayDialog("DigiCreatures Export", "没有找到 Packages/com.digicreatures.agent。", "确定");
                return;
            }

            try
            {
                if (AssetDatabase.IsValidFolder(ExportStagingRoot))
                {
                    AssetDatabase.DeleteAsset(ExportStagingRoot);
                }

                Directory.CreateDirectory(ExportFolder);
                FileUtil.CopyFileOrDirectory(PackageRoot, ExportStagingRoot);
                string packageSamplesPath = Path.Combine(PackageRoot, "Samples~");
                string stagingSamplesPath = Path.Combine(ExportStagingRoot, "Samples");
                if (Directory.Exists(packageSamplesPath))
                {
                    if (Directory.Exists(stagingSamplesPath))
                    {
                        FileUtil.DeleteFileOrDirectory(stagingSamplesPath);
                    }

                    FileUtil.CopyFileOrDirectory(packageSamplesPath, stagingSamplesPath);
                }

                string hiddenStagingSamplesPath = Path.Combine(ExportStagingRoot, "Samples~");
                if (Directory.Exists(hiddenStagingSamplesPath))
                {
                    FileUtil.DeleteFileOrDirectory(hiddenStagingSamplesPath);
                }

                AssetDatabase.Refresh();
                Dictionary<string, string> guidRemap = BuildGuidRemap();
                int rewrittenFiles = RewriteGuidReferences(guidRemap);
                if (rewrittenFiles > 0)
                {
                    AssetDatabase.Refresh();
                }

                string[] assetPaths = AssetDatabase
                    .FindAssets(string.Empty, new[] { ExportStagingRoot })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(path => !AssetDatabase.IsValidFolder(path))
                    .ToArray();
                if (assetPaths.Length == 0)
                {
                    EditorUtility.DisplayDialog("DigiCreatures Export", "临时导出目录中没有找到可导出的资产。", "确定");
                    return;
                }

                string exportPath = Path.GetFullPath(Path.Combine(ExportFolder, $"DigiCreaturesAgent-{GetPackageVersion()}.unitypackage")).Replace("\\", "/");
                AssetDatabase.ExportPackage(
                    assetPaths,
                    exportPath,
                    ExportPackageOptions.Recurse);
                Debug.Log($"DigiCreatures unitypackage exported to {exportPath}. Remapped {guidRemap.Count} GUIDs in {rewrittenFiles} files.");
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(ExportStagingRoot))
                {
                    AssetDatabase.DeleteAsset(ExportStagingRoot);
                    AssetDatabase.Refresh();
                }
            }
        }

        private static string GetPackageVersion()
        {
            string packageJsonPath = Path.Combine(PackageRoot, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                return "0.0.0";
            }

            Match match = Regex.Match(File.ReadAllText(packageJsonPath), "\"version\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : "0.0.0";
        }

        private static Dictionary<string, string> BuildGuidRemap()
        {
            Dictionary<string, string> remap = new Dictionary<string, string>();
            string packageRootFullPath = Path.GetFullPath(PackageRoot);
            string stagingRootFullPath = Path.GetFullPath(ExportStagingRoot);
            foreach (string sourceMetaPath in Directory.GetFiles(packageRootFullPath, "*.meta", SearchOption.AllDirectories))
            {
                string sourceAssetPath = sourceMetaPath.Substring(0, sourceMetaPath.Length - ".meta".Length);
                string relativeAssetPath = Path.GetRelativePath(packageRootFullPath, sourceAssetPath).Replace("\\", "/");
                string stagingRelativePath = relativeAssetPath.StartsWith("Samples~/", System.StringComparison.Ordinal)
                    ? "Samples/" + relativeAssetPath.Substring("Samples~/".Length)
                    : relativeAssetPath;
                string stagingMetaPath = Path.Combine(stagingRootFullPath, stagingRelativePath).Replace("\\", "/") + ".meta";
                if (!File.Exists(stagingMetaPath))
                {
                    continue;
                }

                string sourceGuid = ReadGuid(sourceMetaPath);
                string stagingGuid = ReadGuid(stagingMetaPath);
                if (string.IsNullOrWhiteSpace(sourceGuid) ||
                    string.IsNullOrWhiteSpace(stagingGuid) ||
                    string.Equals(sourceGuid, stagingGuid, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                remap[sourceGuid] = stagingGuid;
            }

            return remap;
        }

        private static int RewriteGuidReferences(Dictionary<string, string> guidRemap)
        {
            if (guidRemap.Count == 0)
            {
                return 0;
            }

            int rewrittenFiles = 0;
            string stagingRootFullPath = Path.GetFullPath(ExportStagingRoot);
            foreach (string filePath in Directory.GetFiles(stagingRootFullPath, "*", SearchOption.AllDirectories))
            {
                if (!ShouldRewriteGuidReferences(filePath))
                {
                    continue;
                }

                string text;
                try
                {
                    text = File.ReadAllText(filePath);
                }
                catch
                {
                    continue;
                }

                string rewritten = text;
                foreach (KeyValuePair<string, string> pair in guidRemap)
                {
                    rewritten = rewritten.Replace(pair.Key, pair.Value);
                }

                if (!string.Equals(text, rewritten, System.StringComparison.Ordinal))
                {
                    File.WriteAllText(filePath, rewritten, new UTF8Encoding(false));
                    rewrittenFiles++;
                }
            }

            return rewrittenFiles;
        }

        private static bool ShouldRewriteGuidReferences(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            switch (extension)
            {
                case ".unity":
                case ".prefab":
                case ".asset":
                case ".mat":
                case ".controller":
                case ".anim":
                case ".inputactions":
                case ".json":
                case ".md":
                case ".txt":
                    return true;
                default:
                    return false;
            }
        }

        private static string ReadGuid(string metaPath)
        {
            foreach (string line in File.ReadLines(metaPath))
            {
                Match match = Regex.Match(line, "^guid:\\s*([a-fA-F0-9]+)\\s*$");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return string.Empty;
        }
    }
}
