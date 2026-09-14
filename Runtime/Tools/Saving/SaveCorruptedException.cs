using System;

/// <summary>
/// Thrown by an <see cref="ISaveBackend"/> when a save exists but cannot be parsed as valid
/// data for its backing store, as opposed to a transient I/O or platform fault. Kept
/// backend-agnostic so <see cref="SaveManager{TSave}"/> can tell "this save is bad, start
/// fresh" apart from "something else went wrong, don't touch the file" without referencing
/// any specific backend's exception types.
/// </summary>
public class SaveCorruptedException : Exception
{
    public SaveCorruptedException(string message) : base(message) { }

    public SaveCorruptedException(string message, Exception innerException) : base(message, innerException) { }
}
