using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NaughtyAttributes;
using UnityEngine;

/// <summary>
/// Owns the one save file. Knows how to read, write, version and recover it - and nothing
/// at all about what is in it. Systems that persist state implement
/// <see cref="ISaveParticipant{TSave}"/> and register here; see that interface for the
/// contract.
///
/// A game subclasses this with its concrete save payload type, e.g.
/// <c>class SaveManager : SaveManager&lt;MyGameSave&gt;</c>, and supplies
/// <see cref="CurrentSaveVersion"/> plus, if the save format ever changes,
/// <see cref="MigrateIfNeeded"/>. Anything about *when* to save (day-end, checkpoints, ...)
/// belongs in that subclass too - this base only exposes <see cref="RequestSave"/>.
/// </summary>
public abstract class SaveManager<TSave> : MonoBehaviour where TSave : class, ISaveVersioned, new()
{
    [SerializeField] private string saveName = "savedgame";
    [SerializeField] private string dataFile = "data";

    [SerializeField]
    [Required("Awake() assigns this manager into the variable so participants in other prefabs can register.")]
    private ScriptableVariable<SaveManager<TSave>> reference;

    /// <summary>
    /// Raised from Awake once this manager has assigned itself. Lets DontDestroyOnLoad
    /// participants (via <see cref="SaveParticipantBinding{TSave}"/>) find a new manager
    /// after a scene change, without depending on any particular game's event bus.
    /// </summary>
    public static event Action<SaveManager<TSave>> OnManagerReady;

    private readonly List<ISaveParticipant<TSave>> _participants = new List<ISaveParticipant<TSave>>();

    /// <summary>
    /// The last save we successfully read (or wrote). A new save starts from a clone of
    /// this and lets the *registered* participants overwrite their own slice, so a scene
    /// that does not contain some system cannot blank that system's data. Replaces
    /// hand-written read-the-file-back-mid-save blocks.
    /// </summary>
    private TSave _lastKnownGood;

    private bool _loadCompleted;
    private TaskCompletionSource<bool> _loadCompletionSource = new TaskCompletionSource<bool>();

    /// <summary>
    /// Completes when the initial load has landed (successfully or not - a missing save
    /// still counts as "we now know the state"). Await this before acting on persisted
    /// state; do not assume execution order, because the load is async.
    /// </summary>
    public Task LoadCompleted => _loadCompletionSource.Task;

    public bool IsLoaded => _loadCompleted;

    // Single-flight + coalesce. The backend throws if a save is already open, and
    // SaveGameAsync can be reachable from multiple triggers (a game event, a skill
    // purchase, the inspector button, the quit path) landing in the same frame.
    private Task _saveInFlight;
    private bool _resaveRequested;

    private bool _quitting;

    private ISaveBackend _backend;
    private ISaveBackend Backend => _backend ??= CreateBackend();

    /// <summary>Bumped whenever the saved format changes, so a load can migrate. See <see cref="MigrateIfNeeded"/>.</summary>
    protected abstract int CurrentSaveVersion { get; }

    /// <summary>
    /// The backend this manager persists through. Defaults to PlatformToolkit (local disk,
    /// or Steam Cloud when the platform provides it); override to use something else (e.g.
    /// a fake backend in tests).
    /// </summary>
    protected virtual ISaveBackend CreateBackend() => new PlatformToolkitSaveBackend();

    /// <summary>
    /// Called before the toolkit-specific work (initializing PlatformToolkit) so a subclass
    /// backed by something else can override this instead. Default: PlatformToolkit.
    /// </summary>
    protected virtual Task<bool> EnsureBackendReady() => PlatformToolkitBootstrap.EnsureReady();

    protected virtual void Awake()
    {
        if (reference != null) reference.value = this;

        // Before the first await, per the project's subscribe-in-Awake rule.
        Application.wantsToQuit += OnWantsToQuit;

        // Lets DontDestroyOnLoad participants find this scene's manager. Same-scene
        // participants pick it up off the variable instead.
        OnManagerReady?.Invoke(this);

        _ = LoadGameAsync();
    }

    protected virtual void OnDestroy()
    {
        Application.wantsToQuit -= OnWantsToQuit;
        if (reference != null && reference.value == this) reference.value = null;
    }

    #region Participants

    /// <summary>
    /// Registers a participant and restores it. If the load already landed the import
    /// happens now; otherwise it happens when it does. Either way the caller never has to
    /// reason about ordering.
    /// </summary>
    public void Register(ISaveParticipant<TSave> participant)
    {
        if (participant == null || _participants.Contains(participant)) return;
        _participants.Add(participant);

        if (_loadCompleted) SafeImport(participant, _lastKnownGood);
    }

    public void Unregister(ISaveParticipant<TSave> participant)
    {
        if (participant != null) _participants.Remove(participant);
    }

    // A participant that throws must not abort the load or save for everyone else - one
    // broken system should cost its own slice, not the whole file.
    private void SafeImport(ISaveParticipant<TSave> participant, TSave data)
    {
        try
        {
            participant.ImportFrom(data);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] Participant '{participant.SaveKey}' threw while importing: {e}");
        }
    }

    private void SafeExport(ISaveParticipant<TSave> participant, TSave data)
    {
        try
        {
            participant.ExportTo(data);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] Participant '{participant.SaveKey}' threw while exporting: {e}");
        }
    }

    #endregion

    #region Saving

    [Button("Save Game")]
    [ContextMenu("SaveGame")]
    private void SaveGameButton() => _ = RequestSave();

    /// <summary>
    /// Requests a save and returns a Task that completes once the caller's state is on
    /// disk. Only one save runs at a time; a request that arrives mid-save schedules
    /// exactly one more afterwards rather than queueing without bound or being dropped.
    /// </summary>
    public Task RequestSave()
    {
        // IsCompleted, not just null: RunSaveLoop can finish synchronously, in which case
        // its finally clears _saveInFlight *before* the assignment below stores the
        // already-completed Task. Testing for null alone would then treat that stale Task
        // as an in-flight save forever and silently stop saving.
        if (_saveInFlight != null && !_saveInFlight.IsCompleted)
        {
            _resaveRequested = true;
            return _saveInFlight;
        }

        _saveInFlight = RunSaveLoop();
        return _saveInFlight;
    }

    private async Task RunSaveLoop()
    {
        try
        {
            do
            {
                _resaveRequested = false;
                await SaveGameAsync();
            }
            while (_resaveRequested);
        }
        finally
        {
            _saveInFlight = null;
        }
    }

    /// <summary>
    /// The actual write. Private on purpose - everything goes through RequestSave so the
    /// single-flight guard cannot be bypassed.
    /// </summary>
    private async Task SaveGameAsync()
    {
        if (!await EnsureBackendReady()) return;

        // Start from what is already on disk so participants absent from this scene keep
        // their data, then let the ones that are here overwrite their own fields.
        var dataSave = Clone(_lastKnownGood) ?? new TSave();
        dataSave.SaveVersion = CurrentSaveVersion;

        foreach (var participant in _participants)
        {
            SafeExport(participant, dataSave);
        }

        try
        {
            await WriteSaveAsync(dataSave);
            _lastKnownGood = dataSave;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] Failed to write save '{saveName}': {e}");
        }
    }

    private async Task WriteSaveAsync(TSave data)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(data));
        await Backend.WriteFileAsync(saveName, dataFile, bytes);
    }

    #endregion

    #region Loading

    [Button("Load Game")]
    [ContextMenu("LoadGame")]
    private void LoadGameButton() => _ = LoadGameAsync();

    public async Task LoadGameAsync()
    {
        TSave loaded = null;
        bool needsRewrite = false;

        if (await EnsureBackendReady())
        {
            try
            {
                bool exists = await Backend.SaveExistsAsync(saveName);
                if (exists)
                {
                    loaded = await ReadSaveAsync();

                    // A truncated or empty file parses to null without throwing. Nothing
                    // else ever rewrites a bad save, so recover here or every future
                    // launch hits the same file.
                    if (loaded == null)
                    {
                        Debug.LogWarning($"[SaveManager] Save '{saveName}' was unreadable or empty. Resetting to defaults and rewriting it.");
                        needsRewrite = true;
                    }
                }
            }
            catch (SaveCorruptedException e)
            {
                Debug.LogWarning($"[SaveManager] Save '{saveName}' is corrupted ({e.Message}). Starting fresh and rewriting it.");
                needsRewrite = true;
            }
            catch (Exception e)
            {
                // Not rewritten: an I/O or platform fault is probably transient, and
                // overwriting the player's save on a transient fault would destroy it.
                Debug.LogError($"[SaveManager] Save '{saveName}' could not be read: {e}");
            }
        }

        if (loaded != null && !MigrateIfNeeded(loaded))
        {
            // A save from a newer build. Left on disk untouched rather than rewritten -
            // the player may simply have launched an older build by mistake.
            loaded = null;
        }

        _lastKnownGood = loaded;
        _loadCompleted = true;

        foreach (var participant in _participants)
        {
            SafeImport(participant, _lastKnownGood);
        }

        _loadCompletionSource.TrySetResult(true);

        if (needsRewrite) await RequestSave();
    }

    private async Task<TSave> ReadSaveAsync()
    {
        byte[] bytes = await Backend.ReadFileAsync(saveName, dataFile);
        return bytes == null ? null : JsonUtility.FromJson<TSave>(System.Text.Encoding.UTF8.GetString(bytes));
    }

    /// <summary>
    /// Brings an older save up to the current format. Returns false when the save cannot
    /// be used at all, in which case the caller starts fresh.
    ///
    /// Default just stamps the current version and accepts anything at or below it -
    /// override for real field-by-field migrations, and to reject saves newer than this
    /// build (SaveVersion used unread is how a save from a newer build ends up silently
    /// misinterpreted rather than admitted as unreadable).
    /// </summary>
    protected virtual bool MigrateIfNeeded(TSave data)
    {
        if (data.SaveVersion > CurrentSaveVersion)
        {
            Debug.LogWarning($"[SaveManager] Save is version {data.SaveVersion}, newer than this build's {CurrentSaveVersion}. " +
                             "Ignoring it rather than misreading it.");
            return false;
        }

        data.SaveVersion = CurrentSaveVersion;
        return true;
    }

    #endregion

    #region Reset

    [Button("Delete Save and Reset")]
    [ContextMenu("DeleteSaveAndReset")]
    private void DeleteSaveAndResetButton() => _ = DeleteSaveAndResetAsync();

    public async Task DeleteSaveAndResetAsync()
    {
        if (await EnsureBackendReady())
        {
            try
            {
                await Backend.DeleteSaveAsync(saveName);
                Debug.Log("Saved data deleted.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Could not delete save '{saveName}': {e}");
            }
        }

        _lastKnownGood = null;
        _loadCompleted = true;
        _loadCompletionSource.TrySetResult(true);

        // null means "fresh save": every participant restores its own defaults.
        foreach (var participant in _participants)
        {
            SafeImport(participant, null);
        }

        Debug.Log("Reset all to defaults.");
    }

    #endregion

    #region Quitting

    private bool OnWantsToQuit()
    {
        if (_quitting) return true;

        _quitting = true;
        _ = SaveBeforeQuit();
        return false;
    }

    /// <summary>
    /// The final save. Retries once, then quits regardless: a player must always be able
    /// to close the game. Returning false from wantsToQuit until a flag that is only set
    /// on success would make a throwing save leave the game unquittable.
    /// </summary>
    private async Task SaveBeforeQuit()
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                await RequestSave();
                break;
            }
            catch (Exception e)
            {
                if (attempt == 2) Debug.LogError($"[SaveManager] Final save failed, quitting without it: {e}");
                else Debug.LogWarning($"[SaveManager] Final save failed (attempt {attempt}), retrying: {e.Message}");
            }
        }

        Quit();
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        // Application.Quit() is ignored in the Editor, so the play session would hang.
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    #endregion

    /// <summary>
    /// JsonUtility round-trip: a save payload typically holds Lists, so a field copy would
    /// let the snapshot and the in-flight save share mutable state.
    /// </summary>
    private static TSave Clone(TSave source)
    {
        return source == null ? null : JsonUtility.FromJson<TSave>(JsonUtility.ToJson(source));
    }
}
