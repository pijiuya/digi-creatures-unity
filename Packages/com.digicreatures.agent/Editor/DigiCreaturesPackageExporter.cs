using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DigiCreaturesEditor
{
    public static class DigiCreaturesPackageExporter
    {
        private const string PackageRoot = "Packages/com.digicreatures.agent";
        private const string ExportStagingRoot = "Assets/__DigiCreaturesAgentExport";
        private const string ExportFolder = "Builds";
        private const string ExportFileName = "DigiCreaturesAgent-0.1.0.unitypackage";

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

                string exportPath = Path.GetFullPath(Path.Combine(ExportFolder, ExportFileName)).Replace("\\", "/");
                AssetDatabase.ExportPackage(
                    assetPaths,
                    exportPath,
                    ExportPackageOptions.Recurse);
                Debug.Log($"DigiCreatures unitypackage exported to {exportPath}");
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
    }
}
