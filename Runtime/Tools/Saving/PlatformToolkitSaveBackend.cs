using System.Threading.Tasks;
using Unity.PlatformToolkit;

/// <summary>
/// <see cref="ISaveBackend"/> adapter over <see cref="PlatformToolkit.LocalSaving"/> (local
/// disk, or Steam Cloud when the platform provides it). Callers must await
/// <see cref="PlatformToolkitBootstrap.EnsureReady"/> before using this - it does not
/// initialize the toolkit itself, since <see cref="SaveManager{TSave}"/> already gates every
/// entry point on that.
/// </summary>
public class PlatformToolkitSaveBackend : ISaveBackend
{
    public async Task<bool> SaveExistsAsync(string saveName)
    {
        // SaveExists may only be called while no save is open, so it is deliberately its
        // own step rather than being folded into ReadFileAsync's using block.
        return await PlatformToolkit.LocalSaving.SaveExists(saveName);
    }

    public async Task<byte[]> ReadFileAsync(string saveName, string fileName)
    {
        try
        {
            var saveSystem = PlatformToolkit.LocalSaving;
            using (var readable = await saveSystem.OpenSaveReadable(saveName))
            {
                if (!await readable.ContainsFile(fileName)) return null;
                return await readable.ReadFile(fileName);
            }
        }
        catch (CorruptedSaveException e)
        {
            throw new SaveCorruptedException($"Save '{saveName}' is corrupted: {e.Message}", e);
        }
    }

    public async Task WriteFileAsync(string saveName, string fileName, byte[] bytes)
    {
        // Re-acquired per operation rather than cached: InvalidSystemException means the
        // saving system has to be recreated through this property.
        var saveSystem = PlatformToolkit.LocalSaving;
        using (var writable = await saveSystem.OpenSaveWritable(saveName))
        {
            await writable.WriteFile(fileName, bytes);
            await writable.Commit();
        }
    }

    public async Task DeleteSaveAsync(string saveName)
    {
        if (await SaveExistsAsync(saveName))
        {
            await PlatformToolkit.LocalSaving.DeleteSave(saveName);
        }
    }
}
