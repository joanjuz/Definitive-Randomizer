using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ThreeDS.Progressive.Services;

/// <summary>
/// Synthetic regression for an external edit after WhyNot validated AfterSha256.
/// Expected: reject Undo, preserve the external edit and every other file, and leave history unchanged.
/// No production hooks: a Windows oplock pauses the restore-blob open until the external write completes.
/// The timeouts are watchdogs only; the oplock notification orders the operations, never a delay.
/// </summary>
internal static class UndoToctouChecks
{
    internal static void Run(Action<bool,string> check)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("This regression requires Windows file oplocks.");
        string root=Path.Combine(Path.GetTempPath(),"undo-toctou-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            VerifyControls(check,Path.Combine(root,"controls"));
            string first=Path.Combine(root,"first.bin"),second=Path.Combine(root,"second.bin");
            var history=new ProjectHistory(root,"USUM",Path.Combine(root,"metadata"));
            File.WriteAllBytes(first,[1]);File.WriteAllBytes(second,[10]);
            history.Commit([new FileChange(first,[1],[2]),new FileChange(second,[10],[20])],"Tests","Synthetic edit",[]);
            var entry=history.Read().Entries.Single();
            string journal=Directory.GetFiles(Path.Combine(root,"metadata","history"),"*.json").Single();
            byte[] historyBefore=File.ReadAllBytes(journal);
            string blob=Path.Combine(history.StorageDirectory,RestorePoints.FolderName,entry.Id.ToString("N"),"000.bin");
            check(RestorePoints.WhyNot(history.StorageDirectory,root,entry.Id) is null,"UndoToctou_BaselineCanUndo");

            Exception? failure=null;
            using(var paused=new ReadOpenBarrier(blob))
            {
                // WhyNot checks first.bin before it touches 000.bin. Once this notification arrives,
                // the target hash was accepted, but Undo cannot yet read first.bin into FileChange.
                var undo=Task.Run(()=>{try{RestorePoints.Undo(history,root,entry.Id);}catch(Exception ex){failure=ex;}});
                try
                {
                    paused.WaitForOpen();
                    check(!undo.IsCompleted&&File.ReadAllBytes(first).SequenceEqual(new byte[]{2}),"UndoToctou_PausedAfterTargetValidation");
                    File.WriteAllBytes(first,[3]);
                    check(File.ReadAllBytes(first).SequenceEqual(new byte[]{3}),"UndoToctou_ExternalEditWrittenBeforeSecondRead");
                }
                finally
                {
                    paused.Release();
                    if(!undo.Wait(TimeSpan.FromSeconds(15)))throw new TimeoutException("Undo did not finish after releasing the blob read.");
                }
            }
            bool rejected=failure is IOException or InvalidOperationException;
            bool firstIntact=File.ReadAllBytes(first).SequenceEqual(new byte[]{3});
            bool secondIntact=File.ReadAllBytes(second).SequenceEqual(new byte[]{20});
            bool historyIntact=File.ReadAllBytes(journal).SequenceEqual(historyBefore);
            Console.WriteLine($"UndoToctou observations: rejected={rejected}; externalEditPreserved={firstIntact}; otherFilePreserved={secondIntact}; historyUnchanged={historyIntact}; exception={failure?.GetType().Name??"none"}; first={Convert.ToHexString(File.ReadAllBytes(first))}; second={Convert.ToHexString(File.ReadAllBytes(second))}; entries={history.Read().Entries.Count}");
            check(rejected&&firstIntact&&secondIntact&&historyIntact,"UndoToctou_RejectsExternalEditWithoutChangingFilesOrHistory");
        }
        finally{Directory.Delete(root,true);}
    }

    private static void VerifyControls(Action<bool,string> check,string root)
    {
        Directory.CreateDirectory(root);
        string path=Path.Combine(root,"control.bin");File.WriteAllBytes(path,[1]);
        var history=new ProjectHistory(root,"USUM",Path.Combine(root,"metadata"));
        history.Commit([new FileChange(path,[1],[2])],"Tests","Synthetic edit",[]);
        var entry=history.Read().Entries.Single();
        RestorePoints.Undo(history,root,entry.Id);
        check(File.ReadAllBytes(path).SequenceEqual(new byte[]{1})&&history.Read().Entries.Count==2
            &&history.Read().Entries[^1].Parameters.GetValueOrDefault("UndoOf")==entry.Id.ToString(),"UndoToctou_ControlNormalUndoRestoresAndRecords");
        history.Commit([new FileChange(path,[1],[2])],"Tests","Synthetic edit",[]);
        entry=history.Read().Entries[^1];
        string journal=Directory.GetFiles(Path.Combine(root,"metadata","history"),"*.json").Single();
        byte[] before=File.ReadAllBytes(journal);File.WriteAllBytes(path,[3]);
        bool rejected=false;
        try{RestorePoints.Undo(history,root,entry.Id);}catch(InvalidOperationException){rejected=true;}
        check(rejected&&File.ReadAllBytes(path).SequenceEqual(new byte[]{3})&&File.ReadAllBytes(journal).SequenceEqual(before),
            "UndoToctou_ControlEarlierExternalEditIsRejectedWithoutWrites");
    }

    /// <summary>Holds the incompatible read-open until Release closes the oplock handle.</summary>
    private sealed class ReadOpenBarrier:IDisposable
    {
        private const uint RequestLevel1=0x00090000;
        private readonly EventWaitHandle _notification=new(false,EventResetMode.ManualReset);
        private readonly SafeFileHandle _file;
        private readonly IntPtr _overlapped;
        private bool _released;

        [StructLayout(LayoutKind.Sequential)]
        private struct Overlapped
        {
            public IntPtr Internal,InternalHigh;
            public uint Offset,OffsetHigh;
            public IntPtr Event;
        }

        internal ReadOpenBarrier(string path)
        {
            _file=File.OpenHandle(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,FileOptions.Asynchronous);
            _overlapped=Marshal.AllocHGlobal(Marshal.SizeOf<Overlapped>());
            // The low bit suppresses IOCP delivery: this native OVERLAPPED belongs to this
            // event-based barrier, not to the CLR's ThreadPoolBoundHandle callback machinery.
            var eventHandle=new IntPtr(_notification.SafeWaitHandle.DangerousGetHandle().ToInt64()|1);
            Marshal.StructureToPtr(new Overlapped{Event=eventHandle},_overlapped,false);
            bool completed=DeviceIoControl(_file,RequestLevel1,IntPtr.Zero,0,IntPtr.Zero,0,out _,_overlapped);
            int error=Marshal.GetLastWin32Error();
            if(completed||error!=997)
            {
                _file.Dispose();Marshal.FreeHGlobal(_overlapped);_notification.Dispose();
                throw new IOException("Cannot establish the pending oplock barrier (requires a local oplock-capable filesystem).",new Win32Exception(error));
            }
        }
        internal void WaitForOpen()
        {
            if(!_notification.WaitOne(TimeSpan.FromSeconds(15)))throw new TimeoutException("No restore-blob open reached the oplock barrier.");
            if(!GetOverlappedResult(_file,_overlapped,out _,false))throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        internal void Release()
        {
            if(_released)return;
            // Closing the handle acknowledges the break and releases the blocked open. It also
            // cancels a pending oplock if the worker failed before reaching the blob.
            _file.Dispose();
            if(!_notification.WaitOne(TimeSpan.FromSeconds(15)))throw new TimeoutException("Oplock cancellation did not complete.");
            _released=true;
        }
        public void Dispose()
        {
            Release();Marshal.FreeHGlobal(_overlapped);_notification.Dispose();
        }
        [DllImport("kernel32.dll",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle file,uint code,IntPtr input,uint inputSize,IntPtr output,uint outputSize,out uint returned,IntPtr overlapped);
        [DllImport("kernel32.dll",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOverlappedResult(SafeFileHandle file,IntPtr overlapped,out uint transferred,[MarshalAs(UnmanagedType.Bool)]bool wait);
    }
}
