using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DigiCreaturesEditor
{
    public static class DigiCreaturesPackageExporter
    {
        private const string PackageRoot = "Packages/com.digicreatures.agent";
        private const string ExportFolder = "Builds";
        private const string UnityPackageRoot = "Assets/DigiCreaturesAgent";

        [MenuItem("DigiCreatures/Export UnityPackage")]
        [MenuItem("数字生物/高级设置/导出 UnityPackage")]
        public static void ExportUnityPackage()
        {
            if (!Directory.Exists(PackageRoot))
            {
                EditorUtility.DisplayDialog("DigiCreatures Export", "没有找到 Packages/com.digicreatures.agent。", "确定");
                return;
            }

            Directory.CreateDirectory(ExportFolder);
            string exportPath = Path.GetFullPath(Path.Combine(ExportFolder, $"DigiCreaturesAgent-{GetPackageVersion()}.unitypackage")).Replace("\\", "/");
            List<PackageAsset> assets = CollectPackageAssets().ToList();
            if (assets.Count == 0)
            {
                EditorUtility.DisplayDialog("DigiCreatures Export", "没有找到可导出的包文件。", "确定");
                return;
            }

            WriteUnityPackage(exportPath, assets);
            Debug.Log($"DigiCreatures stable unitypackage exported to {exportPath}. Assets={assets.Count}; root={UnityPackageRoot}");
            EditorUtility.DisplayDialog("DigiCreatures Export", $"已导出：\n{exportPath}\n\n此版本使用稳定 GUID 打包，不再依赖临时 Assets 目录。", "确定");
        }

        private static IEnumerable<PackageAsset> CollectPackageAssets()
        {
            string packageRootFullPath = Path.GetFullPath(PackageRoot);
            foreach (string assetPath in Directory.GetFiles(packageRootFullPath, "*", SearchOption.AllDirectories))
            {
                if (!ShouldIncludeAsset(assetPath))
                {
                    continue;
                }

                string metaPath = assetPath + ".meta";
                if (!File.Exists(metaPath))
                {
                    Debug.LogWarning("DigiCreatures export skipped file without meta: " + ToProjectRelativePath(assetPath));
                    continue;
                }

                string relativePath = Path.GetRelativePath(packageRootFullPath, assetPath).Replace("\\", "/");
                string packagePath = MapToUnityPackagePath(relativePath);
                string guid = ReadGuid(metaPath);
                if (string.IsNullOrWhiteSpace(guid))
                {
                    guid = StableHash(relativePath);
                }

                yield return new PackageAsset(assetPath, metaPath, packagePath, guid);
            }
        }

        private static bool ShouldIncludeAsset(string assetPath)
        {
            string fileName = Path.GetFileName(assetPath);
            if (fileName.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string normalized = assetPath.Replace("\\", "/");
            return !normalized.Contains("/memory.jsonl", StringComparison.OrdinalIgnoreCase) &&
                   !normalized.Contains("/test-memory.jsonl", StringComparison.OrdinalIgnoreCase) &&
                   !normalized.Contains("/llm-long-test-report", StringComparison.OrdinalIgnoreCase);
        }

        private static string MapToUnityPackagePath(string relativePath)
        {
            string mapped = relativePath;
            if (mapped.StartsWith("Samples~/", StringComparison.Ordinal))
            {
                mapped = "Samples/" + mapped.Substring("Samples~/".Length);
            }
            else if (mapped.StartsWith("Documentation~/", StringComparison.Ordinal))
            {
                mapped = "Documentation/" + mapped.Substring("Documentation~/".Length);
            }

            return UnityPackageRoot + "/" + mapped;
        }

        private static void WriteUnityPackage(string exportPath, List<PackageAsset> assets)
        {
            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }

            using FileStream fileStream = File.Create(exportPath);
            using GZipStream gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
            foreach (PackageAsset asset in assets.OrderBy(asset => asset.PackagePath, StringComparer.OrdinalIgnoreCase))
            {
                string entryRoot = asset.Guid;
                WriteTarFile(gzipStream, entryRoot + "/asset", asset.AssetPath);
                WriteTarFile(gzipStream, entryRoot + "/asset.meta", asset.MetaPath);
                WriteTarBytes(gzipStream, entryRoot + "/pathname", Encoding.UTF8.GetBytes(asset.PackagePath));
            }

            gzipStream.Write(new byte[1024], 0, 1024);
        }

        private static void WriteTarFile(Stream stream, string entryName, string sourcePath)
        {
            FileInfo info = new FileInfo(sourcePath);
            WriteTarHeader(stream, entryName, info.Length, info.LastWriteTimeUtc);
            using FileStream input = File.OpenRead(sourcePath);
            input.CopyTo(stream);
            WritePadding(stream, info.Length);
        }

        private static void WriteTarBytes(Stream stream, string entryName, byte[] content)
        {
            WriteTarHeader(stream, entryName, content.Length, DateTime.UtcNow);
            stream.Write(content, 0, content.Length);
            WritePadding(stream, content.Length);
        }

        private static void WriteTarHeader(Stream stream, string entryName, long size, DateTime modifiedUtc)
        {
            byte[] header = new byte[512];
            WriteAscii(header, 0, 100, entryName);
            WriteOctal(header, 100, 8, 420);
            WriteOctal(header, 108, 8, 0);
            WriteOctal(header, 116, 8, 0);
            WriteOctal(header, 124, 12, size);
            long unixTime = (long)(modifiedUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            WriteOctal(header, 136, 12, Math.Max(0, unixTime));
            for (int i = 148; i < 156; i++)
            {
                header[i] = 0x20;
            }

            header[156] = (byte)'0';
            WriteAscii(header, 257, 6, "ustar");
            WriteAscii(header, 263, 2, "00");
            WriteAscii(header, 265, 32, "DigiCreatures");
            WriteAscii(header, 297, 32, "DigiCreatures");

            int checksum = header.Sum(value => (int)value);
            WriteChecksum(header, checksum);
            stream.Write(header, 0, header.Length);
        }

        private static void WriteAscii(byte[] buffer, int offset, int length, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            int count = Math.Min(bytes.Length, length);
            Array.Copy(bytes, 0, buffer, offset, count);
        }

        private static void WriteOctal(byte[] buffer, int offset, int length, long value)
        {
            string text = Convert.ToString(value, 8);
            if (text.Length > length - 1)
            {
                text = text.Substring(text.Length - (length - 1));
            }

            text = text.PadLeft(length - 1, '0');
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            Array.Copy(bytes, 0, buffer, offset, bytes.Length);
            buffer[offset + length - 1] = 0;
        }

        private static void WriteChecksum(byte[] buffer, int checksum)
        {
            string text = Convert.ToString(checksum, 8).PadLeft(6, '0');
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            Array.Copy(bytes, 0, buffer, 148, bytes.Length);
            buffer[154] = 0;
            buffer[155] = 0x20;
        }

        private static void WritePadding(Stream stream, long size)
        {
            int padding = (int)((512 - (size % 512)) % 512);
            if (padding > 0)
            {
                stream.Write(new byte[padding], 0, padding);
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

        private static string ReadGuid(string metaPath)
        {
            foreach (string line in File.ReadLines(metaPath))
            {
                Match match = Regex.Match(line, "^guid:\\s*([a-fA-F0-9]+)\\s*$");
                if (match.Success)
                {
                    return match.Groups[1].Value.ToLowerInvariant();
                }
            }

            return string.Empty;
        }

        private static string StableHash(string value)
        {
            using MD5 md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(value));
            return string.Concat(hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static string ToProjectRelativePath(string path)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace("\\", "/");
            string fullPath = Path.GetFullPath(path).Replace("\\", "/");
            return fullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(projectRoot.Length + 1)
                : path;
        }

        private readonly struct PackageAsset
        {
            public PackageAsset(string assetPath, string metaPath, string packagePath, string guid)
            {
                AssetPath = assetPath;
                MetaPath = metaPath;
                PackagePath = packagePath;
                Guid = guid;
            }

            public string AssetPath { get; }
            public string MetaPath { get; }
            public string PackagePath { get; }
            public string Guid { get; }
        }
    }
}
