using System.Threading.Tasks;

/// <summary>
/// The storage <see cref="SaveManager{TSave}"/> reads and writes bytes through. Keeps the
/// save loop (versioning, participant fan-out, single-flight/coalesce) independent of which
/// platform actually stores the file - local disk, PlatformToolkit/Steam Cloud, or anything
/// else with the same shape.
/// </summary>
public interface ISaveBackend
{
    /// <summary>Must only be called while no save with this name is open.</summary>
    Task<bool> SaveExistsAsync(string saveName);

    /// <summary>
    /// Returns the raw bytes of <paramref name="fileName"/> inside save <paramref name="saveName"/>,
    /// or null if the save has no such file. Throws <see cref="SaveCorruptedException"/> if the
    /// save exists but cannot be opened/read as valid data.
    /// </summary>
    Task<byte[]> ReadFileAsync(string saveName, string fileName);

    /// <summary>Writes and commits <paramref name="bytes"/> as <paramref name="fileName"/> inside save <paramref name="saveName"/>.</summary>
    Task WriteFileAsync(string saveName, string fileName, byte[] bytes);

    /// <summary>Deletes the save if it exists; a no-op otherwise.</summary>
    Task DeleteSaveAsync(string saveName);
}
