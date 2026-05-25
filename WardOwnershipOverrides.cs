namespace STUWard;

internal static partial class WardOwnership
{
    private static bool ReloadOverrides(bool force)
    {
        if (ZNet.instance != null && !ZNet.instance.IsServer())
        {
            return false;
        }

        var snapshotOverrides = ManagedWardConfigFileService.CurrentSnapshot.WardLimitOverrides;
        var changed = force || WardLimitOverrides.Count != snapshotOverrides.Count;

        if (!changed)
        {
            foreach (var entry in snapshotOverrides)
            {
                if (!WardLimitOverrides.TryGetValue(entry.Key, out var currentValue) || currentValue != entry.Value)
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
        {
            return false;
        }

        WardLimitOverrides.Clear();
        foreach (var entry in snapshotOverrides)
        {
            WardLimitOverrides[entry.Key] = entry.Value;
        }

        return true;
    }
}
