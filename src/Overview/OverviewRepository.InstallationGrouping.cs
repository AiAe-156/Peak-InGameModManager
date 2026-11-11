#nullable enable
using System;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewRepository
{
    internal void PersistInstallationGrouping(string profileId, Action<OverviewState> apply)
    {
        lock (_saveLock)
        {
            Profiles.SaveInstallationGrouping(profileId, apply);
            var information = PackWorkspaceInformation.Read(Profiles.RootDirectory);
            if (information != null)
            {
                var shared = new OverviewState { pack = information.pack, groups = information.groups, mods = information.rules };
                apply(shared); PackWorkspaceInformation.Write(Profiles.RootDirectory, shared, information.resourceDirectory);
            }
            if (ViewedProfileId != profileId || _appliedView) return;
            // Merge precisely this delta into both draft and its baseline. Other edits stay dirty.
            apply(State);
            apply(_savedDraftProfile.state);
            _savedDraftSignature = DraftSignature(_savedDraftProfile.state, _savedDraftProfile.config, _savedDraftProfile.mods);
            _loadedRevision = ++_sharedRevision;
        }
    }
}
