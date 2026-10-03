namespace Empire.UI
{
    /// <summary>
    /// Optional adapter contract for the one-click allocation in Regiment Manager.
    /// The action must apply points through the source mode's existing progression
    /// and persistence methods, never by editing RegimentRecord.Progression directly.
    /// </summary>
    public interface IRegimentAutoProgressionSource
    {
        RegimentAction AutoApply(string id);
    }
}
