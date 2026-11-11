partial class PublicationHarness {
private void FlushInfrastructureRedraw()
    {
        if (!_infrastructureRedraw || _infrastructureLoading || !_opened || _building || IsEditingText || _dragged != null || _scroll.velocity.sqrMagnitude > 1) return;
        // Render captures/restores row focus; a selected row must not starve publication.
        Render(); // Only an accepted Render consumes the pending publication.
    }
}