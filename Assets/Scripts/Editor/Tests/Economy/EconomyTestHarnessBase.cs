using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Shared NUnit EditMode base class isolating <see cref="SaveManager"/> to a temporary directory,
/// resetting <see cref="LocalClock"/>, <see cref="EconomyBalanceConfig"/>, and cleaning up spawned test objects.
/// Never touches developer save files in <c>Application.persistentDataPath</c>.
/// </summary>
public abstract class EconomyTestHarnessBase
{
    protected string IsolatedSaveDir { get; private set; }
    protected EconomyBalanceConfig TestBalance { get; private set; }
    private readonly List<UnityEngine.Object> trackedObjects = new List<UnityEngine.Object>();

    [SetUp]
    public virtual void BaseSetUp()
    {
        IsolatedSaveDir = Path.Combine(Path.GetTempPath(), "GravitaEconomyTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(IsolatedSaveDir);

        SaveManager.SimulateSaveFailureForTesting = false;
        SaveManager.CustomSaveDirectoryForTesting = IsolatedSaveDir;
        SaveManager.ClearMemoryCacheForTesting();

        LocalClock.ResetToSystemClock();
        AdPolicyCoordinator.ResetRuntimeFlags();
        MissionService.DebugOverrideDayIndex = 0;
        MissionService.DebugUtcNowOverride = null;
        MissionService.SimulateRewardFailureForTesting = false;

        TestBalance = TrackAsset(ScriptableObject.CreateInstance<EconomyBalanceConfig>());
        TestBalance.ResetToBaselineDefaults();
        EconomyBalanceConfig.SetActiveConfig(TestBalance);

        // Initialize clean v2 profile in isolated directory
        var fresh = new ProfileData();
        fresh.MigrateIfNeeded();
        SaveManager.TrySaveProfile(fresh);
    }

    [TearDown]
    public virtual void BaseTearDown()
    {
        SaveManager.SimulateSaveFailureForTesting = false;
        LocalClock.ResetToSystemClock();
        AdPolicyCoordinator.ResetRuntimeFlags();
        MissionService.DebugOverrideDayIndex = 0;
        MissionService.DebugUtcNowOverride = null;
        MissionService.SimulateRewardFailureForTesting = false;
        EconomyBalanceConfig.SetActiveConfig(null);

        for (int i = trackedObjects.Count - 1; i >= 0; i--)
        {
            if (trackedObjects[i] != null)
            {
                if (!Application.isPlaying && trackedObjects[i] is GameObject go)
                {
                    MonoBehaviour[] behaviours = go.GetComponents<MonoBehaviour>();
                    for (int b = 0; b < behaviours.Length; b++)
                    {
                        if (behaviours[b] != null)
                            InvokeLifecycleIfPresent(behaviours[b], "OnDestroy");
                    }
                }
                UnityEngine.Object.DestroyImmediate(trackedObjects[i]);
            }
        }
        trackedObjects.Clear();

        SaveManager.ClearMemoryCacheForTesting();
        SaveManager.CustomSaveDirectoryForTesting = null;

        if (!string.IsNullOrEmpty(IsolatedSaveDir) && Directory.Exists(IsolatedSaveDir))
        {
            try
            {
                Directory.Delete(IsolatedSaveDir, recursive: true);
            }
            catch
            {
                // Ignore transient OS file handle cleanup delays in temp directory
            }
        }
    }

    protected T TrackAsset<T>(T asset) where T : UnityEngine.Object
    {
        if (asset != null)
            trackedObjects.Add(asset);
        return asset;
    }

    protected GameObject CreateTestGameObject(string name)
    {
        var go = new GameObject(name);
        trackedObjects.Add(go);
        return go;
    }

    protected T SpawnSingleton<T>(string name) where T : Component
    {
        var go = CreateTestGameObject(name);
        var comp = go.AddComponent<T>();
        if (!Application.isPlaying)
        {
            InvokeLifecycleIfPresent(comp, "Awake");
        }
        return comp;
    }

    protected static void InvokeLifecycleIfPresent(object target, string methodName)
    {
        if (target == null)
            return;

        var method = target.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        method?.Invoke(target, null);
    }
}
