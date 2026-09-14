using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.PlatformToolkit;

/// <summary>
/// One shared, awaitable PlatformToolkit initialization.
///
/// PlatformToolkit.Initialize() cannot be called concurrently: it returns *immediately*
/// when another initialization is already in flight
/// (`if (s_IsInitialized || s_IsInitializing) return;`) rather than awaiting it. The
/// second caller therefore continues while s_Instance is still NullToolkit, whose
/// LocalSavingSystem/Capabilities throw "PlatformToolkit is not initialized". Any number of
/// callers that want it on the same frame (saving, achievements, ...) go through this
/// instead and await the same Task.
/// </summary>
public static class PlatformToolkitBootstrap
{
    private static Task _initialization;
    private static bool _failed;

    /// <summary>
    /// True once initialization has completed successfully. Callers that must not throw
    /// (cosmetic/optional platform features) can check this instead of catching.
    /// </summary>
    public static bool IsReady { get; private set; }

    /// <summary>
    /// Awaitable by any number of concurrent callers. A failure is cached and logged once
    /// rather than re-thrown per call site - in the Editor, Initialize() throws outright
    /// when no Play Mode Controls Settings asset is configured, which would otherwise spam
    /// an error for every save, load and achievement unlock of the session.
    /// </summary>
    public static async Task<bool> EnsureReady()
    {
        if (IsReady) return true;
        if (_failed) return false;

        _initialization ??= InitializeOnce();
        await _initialization;
        return IsReady;
    }

    private static async Task InitializeOnce()
    {
        try
        {
            await PlatformToolkit.Initialize();
            IsReady = true;
        }
        catch (Exception e)
        {
            _failed = true;
            Debug.LogError($"[PlatformToolkitBootstrap] PlatformToolkit failed to initialize: {e.Message}. " +
                           "Saving and platform achievements are unavailable for this session.");
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Statics survive play-mode exit when domain reload is disabled, and a stale "ready"
    /// would point at a toolkit instance from the previous session.
    /// </summary>
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _initialization = null;
        _failed = false;
        IsReady = false;
    }
#endif
}
