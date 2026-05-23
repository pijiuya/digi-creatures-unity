using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace DigiCreaturesInstaller
{
    [InitializeOnLoad]
    public static class DigiCreaturesDependencyInstaller
    {
        private const string AutoInstallSessionKey = "DigiCreatures.DependencyInstaller.AutoInstallAttempted";

        private static readonly Dependency[] RequiredDependencies =
        {
            new Dependency("com.unity.ai.navigation", string.Empty),
            new Dependency("com.unity.cloud.gltfast", string.Empty),
            new Dependency("com.unity.inputsystem", string.Empty),
            new Dependency("com.unity.render-pipelines.universal", string.Empty),
            new Dependency("com.unity.ugui", string.Empty)
        };

        private static readonly Queue<Dependency> Pending = new Queue<Dependency>();
        private static AddRequest currentRequest;
        private static Dependency currentDependency;

        static DigiCreaturesDependencyInstaller()
        {
            EditorApplication.delayCall += AutoInstallOnce;
        }

        [MenuItem("DigiCreatures/Install Dependencies")]
        [MenuItem("数字生物/高级设置/安装依赖")]
        public static void InstallDependenciesMenu()
        {
            InstallMissingDependencies(true);
        }

        private static void AutoInstallOnce()
        {
            if (SessionState.GetBool(AutoInstallSessionKey, false))
            {
                return;
            }

            SessionState.SetBool(AutoInstallSessionKey, true);
            InstallMissingDependencies(false);
        }

        private static void InstallMissingDependencies(bool showDialogWhenComplete)
        {
            if (currentRequest != null)
            {
                UnityEngine.Debug.Log("DigiCreatures dependency installation is already running.");
                return;
            }

            Pending.Clear();
            foreach (Dependency dependency in RequiredDependencies)
            {
                if (!IsInstalled(dependency.Name))
                {
                    Pending.Enqueue(dependency);
                }
            }

            if (Pending.Count == 0)
            {
                if (showDialogWhenComplete)
                {
                    EditorUtility.DisplayDialog("DigiCreatures", "依赖已经安装完成。", "确定");
                }

                return;
            }

            UnityEngine.Debug.Log($"DigiCreatures installing {Pending.Count} missing package dependencies...");
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            StartNextRequest();
        }

        private static bool IsInstalled(string packageName)
        {
            try
            {
                return UnityEditor.PackageManager.PackageInfo.FindForPackageName(packageName) != null;
            }
            catch
            {
                return false;
            }
        }

        private static void StartNextRequest()
        {
            if (Pending.Count == 0)
            {
                currentRequest = null;
                EditorApplication.update -= Tick;
                AssetDatabase.Refresh();
                UnityEngine.Debug.Log("DigiCreatures dependency installation finished.");
                return;
            }

            currentDependency = Pending.Dequeue();
            string identifier = string.IsNullOrWhiteSpace(currentDependency.Version)
                ? currentDependency.Name
                : currentDependency.Name + "@" + currentDependency.Version;
            UnityEngine.Debug.Log("DigiCreatures installing dependency: " + identifier);
            currentRequest = Client.Add(identifier);
        }

        private static void Tick()
        {
            if (currentRequest == null || !currentRequest.IsCompleted)
            {
                return;
            }

            if (currentRequest.Status == StatusCode.Success)
            {
                UnityEngine.Debug.Log("DigiCreatures installed dependency: " + currentDependency.Name);
            }
            else
            {
                UnityEngine.Debug.LogError("DigiCreatures failed to install dependency " + currentDependency.Name + ": " + currentRequest.Error.message);
            }

            currentRequest = null;
            StartNextRequest();
        }

        [Serializable]
        private readonly struct Dependency
        {
            public Dependency(string name, string version)
            {
                Name = name;
                Version = version;
            }

            public string Name { get; }
            public string Version { get; }
        }
    }
}
