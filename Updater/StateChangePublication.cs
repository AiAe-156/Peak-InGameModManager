namespace MCE.UpdateWorker;
/// <summary>Call under the owner's state lock. Failed writes never acknowledge a state.</summary>
internal sealed class StateChangePublication
{
    private string? published;
    internal bool Publish(System.Func<string> capture, System.Action write)
    {
        string candidate=capture();
        if(candidate==published)return false;
        write();
        // write may advance the business revision. Store the committed representation.
        published=capture();
        return true;
    }
}
