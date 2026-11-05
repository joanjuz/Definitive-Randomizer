using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Test-only Windows oplock, derived from UndoToctouChecks. Timeouts are watchdogs only.
    internal sealed class TransactionOpenBarrier:IDisposable
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

        internal TransactionOpenBarrier(string path)
        {
            // No data access: a pending exclusive writer must not fail its sharing check
            // merely because the instrumentation still owns a read-access handle.
            _file=CreateFile(path,0,7,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
            if(_file.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
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
            if(!_notification.WaitOne(TimeSpan.FromSeconds(15)))throw new TimeoutException("No transaction temporary open reached the oplock barrier.");
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
        [DllImport("kernel32.dll",EntryPoint="CreateFileW",ExactSpelling=true,SetLastError=true)]
        private static extern SafeFileHandle CreateFile([MarshalAs(UnmanagedType.LPWStr)]string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle file,uint code,IntPtr input,uint inputSize,IntPtr output,uint outputSize,out uint returned,IntPtr overlapped);
        [DllImport("kernel32.dll",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOverlappedResult(SafeFileHandle file,IntPtr overlapped,out uint transferred,[MarshalAs(UnmanagedType.Bool)]bool wait);
    }
