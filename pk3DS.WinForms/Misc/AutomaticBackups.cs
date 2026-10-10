namespace pk3DS.WinForms;

/// <summary>
/// Persistent automatic backups are disabled by default to prevent large
/// extracted-ROM backups from accumulating on disk. Existing backups may
/// still be restored manually; staged in-memory rollback is unaffected.
/// </summary>
internal static class AutomaticBackups
{
    internal static bool Enabled => false;
}
