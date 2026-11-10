using System.IO;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// A project has an interrupted save that must be recovered before it can be read. It is an <see cref="IOException"/> with the same message the
/// callers always showed, but typed, so that the universal "Abrir proyecto" flow can recognise the state without comparing texts and can ask the user
/// before recovering. Only a read that does NOT recover (<c>recoverInterruptedSave:false</c>, diagnostics, backup, rebuild) raises it.
/// </summary>
internal sealed class InterruptedSaveException : IOException
{
    internal InterruptedSaveException(string message, string pendingPath) : base(message) { PendingPath = pendingPath; }
    /// <summary>The pending recovery record (a folder) that was found.</summary>
    internal string PendingPath { get; }
}
