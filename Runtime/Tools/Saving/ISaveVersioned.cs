/// <summary>
/// Contract a save payload type must satisfy so <see cref="SaveManager{TSave}"/> can stamp
/// and read a format version without knowing anything else about the payload's shape.
/// </summary>
public interface ISaveVersioned
{
    int SaveVersion { get; set; }
}
