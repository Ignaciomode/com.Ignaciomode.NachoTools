/// <summary>
/// Connects an <see cref="ISaveParticipant{TSave}"/> to whichever <see cref="SaveManager{TSave}"/>
/// is currently alive, in either arrival order, so participants do not each hand-roll it.
///
/// Two cases have to work:
///  - The participant lives in the same scene. SaveManager should run early (e.g.
///    [DefaultExecutionOrder(-100)] on the concrete subclass), so its variable is already
///    set by the time the participant's Awake() runs and the binding registers immediately.
///  - The participant is DontDestroyOnLoad while SaveManager is not. The old manager is
///    destroyed on a scene change and a new one appears, so the binding also listens for
///    <see cref="SaveManager{TSave}.OnManagerReady"/> and re-registers with each new manager.
///
/// Registration itself is order-safe: SaveManager.Register imports immediately when the
/// load has already completed, so binding late is not the same as missing the data.
/// </summary>
public sealed class SaveParticipantBinding<TSave>
{
    private ISaveParticipant<TSave> _participant;
    private SaveManager<TSave> _bound;

    public void Bind(ISaveParticipant<TSave> participant, ScriptableVariable<SaveManager<TSave>> variable)
    {
        _participant = participant;

        SaveManager<TSave>.OnManagerReady += Attach;

        if (variable != null && variable.value != null) Attach(variable.value);
    }

    public void Unbind()
    {
        SaveManager<TSave>.OnManagerReady -= Attach;

        if (_bound != null)
        {
            _bound.Unregister(_participant);
            _bound = null;
        }
    }

    private void Attach(SaveManager<TSave> manager)
    {
        if (_bound == manager) return;

        if (_bound != null) _bound.Unregister(_participant);
        _bound = manager;
        _bound.Register(_participant);
    }
}
