using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// A file held so that nobody else can change it, for as long as this handle stays open.
/// The handle has READ|DELETE access and shares only READ. A writer, a File.Replace, a rename
/// over or away and a delete all fail with a sharing violation, and the open itself fails while
/// a writer or a writable mapping already exists. Bytes read here therefore cannot go stale.
/// The lock follows the file through <see cref="MoveTo"/>, not the name.
/// </summary>
internal sealed class ExclusiveFile : IDisposable
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int NameCollision = unchecked((int)0xC0000035);
    private const int FileRenameInformation = 10, FileDispositionInformation = 13;
    private readonly FileStream _stream;
    internal string Path { get; private set; }

    private ExclusiveFile(string path, FileStream stream) { Path = path; _stream = stream; }

    /// <summary>Name of the original while its replacement is installed. Derived, so the journal format is unchanged.</summary>
    internal static string RetiredPath(string path, string token) => path + ".studio-" + token + ".old.tmp";

    internal static ExclusiveFile Open(string path)
    {
        FileStream stream;
        try
        {
            stream = new FileInfo(path).Create(FileMode.Open,
                FileSystemRights.ReadData | FileSystemRights.ReadAttributes | FileSystemRights.Delete,
                FileShare.Read, 1, FileOptions.None, null);
        }
        catch (IOException ex) when (ex.HResult == SharingViolation)
        {
            throw new IOException("Otro programa está usando el archivo: " + path + ". Ciérralo y vuelve a intentarlo.", ex);
        }
        try
        {
            // File.Replace refuses both. Renaming through a followed link would instead move the real file.
            var info = new FileInfo(path);
            if (info.LinkTarget is not null)
                throw new IOException("No se admite un vínculo simbólico como destino: " + path);
            if (info.IsReadOnly)
                throw new UnauthorizedAccessException("El archivo es de solo lectura: " + path);
        }
        catch { stream.Dispose(); throw; }
        return new ExclusiveFile(path, stream);
    }

    internal byte[] ReadAll()
    {
        // RandomAccess bypasses any stream buffer: these are the bytes on disk now.
        var handle = _stream.SafeFileHandle;
        byte[] bytes = new byte[checked((int)RandomAccess.GetLength(handle))];
        for (int done = 0; done < bytes.Length;)
        {
            int read = RandomAccess.Read(handle, bytes.AsSpan(done), done);
            if (read == 0) throw new EndOfStreamException("El archivo terminó antes de lo esperado: " + Path);
            done += read;
        }
        return bytes;
    }

    /// <summary>
    /// Renames through this handle and never replaces, so losing the name to a new file is an
    /// error and that file survives. No share check applies, which is what keeps the lock held.
    /// </summary>
    internal void MoveTo(string path)
    {
        // Through ntdll with an NT path. kernel32 rejects long paths here, and resolves a bare
        // name against the process working directory rather than against this file's folder.
        string nt = System.IO.Path.GetFullPath(path);
        nt = nt.StartsWith("\\\\?\\", StringComparison.Ordinal) ? @"\??\" + nt[4..]
            : nt.StartsWith(@"\\", StringComparison.Ordinal) ? @"\??\UNC\" + nt[2..] : @"\??\" + nt;
        // FILE_RENAME_INFORMATION: ReplaceIfExists stays false in the zeroed head of the buffer.
        int lengthOffset = 2 * IntPtr.Size, nameOffset = lengthOffset + sizeof(int);
        byte[] information = new byte[nameOffset + (nt.Length + 1) * sizeof(char)];
        BitConverter.TryWriteBytes(information.AsSpan(lengthOffset), nt.Length * sizeof(char));
        MemoryMarshal.AsBytes(nt.AsSpan()).CopyTo(information.AsSpan(nameOffset));
        int status = NtSetInformationFile(_stream.SafeFileHandle, out _, information, (uint)information.Length, FileRenameInformation);
        if (status == NameCollision)
            throw new IOException("Otro proceso creó el archivo mientras se reemplazaba. Se conserva para revisión: " + path);
        if (status < 0)
            throw new IOException("No se pudo mover " + Path + " a " + path + ".", Marshal.GetExceptionForHR(RtlNtStatusToDosError(status) | unchecked((int)0x80070000)));
        Path = path;
    }

    /// <summary>Removes the file with its last handle, without a window in which it can be opened by name.</summary>
    internal void DeleteOnClose()
    {
        int status = NtSetInformationFile(_stream.SafeFileHandle, out _, [1], 1, FileDispositionInformation);
        if (status < 0) throw new IOException("No se pudo eliminar " + Path + ".");
    }

    /// <summary>
    /// File.Replace keeps the attributes, creation time and ACL of the replaced file. A rename does
    /// not, so the staged file takes them first. Alternate data streams are not carried over.
    /// </summary>
    internal static void CopyMetadata(string source, string staged)
    {
        const FileAttributes kept = FileAttributes.Hidden | FileAttributes.System | FileAttributes.NotContentIndexed;
        File.SetCreationTimeUtc(staged, File.GetCreationTimeUtc(source));
        var attributes = File.GetAttributes(source) & kept;
        if (attributes != 0) File.SetAttributes(staged, File.GetAttributes(staged) | attributes);
        string from = new FileInfo(source).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        string to = new FileInfo(staged).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        // Usually identical: both inherit from the same folder. Volumes without ACLs compare equal too.
        if (from == to) return;
        // Built from text because a descriptor persists only sections it considers modified.
        var security = new FileSecurity();
        security.SetSecurityDescriptorSddlForm(from, AccessControlSections.Access);
        new FileInfo(staged).SetAccessControl(security);
    }

    public void Dispose() => _stream.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock { public IntPtr Status, Information; }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtSetInformationFile(SafeFileHandle file, out IoStatusBlock status, byte[] information, uint length, int informationClass);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int RtlNtStatusToDosError(int status);
}
