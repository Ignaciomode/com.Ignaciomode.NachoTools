/// <summary>
/// A system that owns a slice of the save file.
///
/// The point of this interface is that <see cref="SaveManager{TSave}"/> does not know what
/// any participant stores - it only collects and distributes. Adding a persisted system
/// means implementing this and registering; it must never mean editing SaveManager.
///
/// **Ordering is not your problem.** Register whenever you like; if the load has already
/// completed, <see cref="ImportFrom"/> is called immediately, otherwise it is called when
/// the load lands. Never gate persistence on execution order or on "Awake runs before
/// Start" - SaveManager's load is async, so the frame it finishes on is not predictable.
/// </summary>
public interface ISaveParticipant<TSave>
{
    /// <summary>Diagnostics only - named in warnings when this participant throws.</summary>
    string SaveKey { get; }

    /// <summary>
    /// Write this system's fields into the save object. Only touch fields you own:
    /// everything else is carried over from the last known good save so that a scene
    /// without your system cannot blank your data.
    /// </summary>
    void ExportTo(TSave data);

    /// <summary>
    /// Restore this system from the save object. <paramref name="data"/> is null for a
    /// fresh save or an explicit reset, which must restore defaults rather than no-op.
    /// May be called more than once per session (e.g. after a delete-save-and-reset), so
    /// it must assign rather than accumulate.
    /// </summary>
    void ImportFrom(TSave data);
}
