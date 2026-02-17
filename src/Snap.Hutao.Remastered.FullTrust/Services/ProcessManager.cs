using Snap.Hutao.Remastered.FullTrust.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Snap.Hutao.Remastered.FullTrust.Services;

public static class ProcessManager
{
    private static FullTrustProcessStartInfoRequest? storedRequest;
    private static Process? currentProcess;
    private static nint processHandle = nint.Zero;
    private static nint mainThreadHandle = nint.Zero;
    private static readonly object lockObject = new();

    private const uint PROCESS_CREATE_THREAD = 0x0002;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint MEM_COMMIT = 0x00001000;
    private const uint MEM_RESERVE = 0x00002000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint THREAD_SUSPEND_RESUME = 0x0002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAllocEx(nint hProcess, nint lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(nint hProcess, nint lpBaseAddress, byte[] lpBuffer, uint nSize, out nint lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern nint GetProcAddress(nint hModule, string lpProcName);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern nint GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateRemoteThread(nint hProcess, nint lpThreadAttributes, uint dwStackSize, nint lpStartAddress, nint lpParameter, uint dwCreationFlags, out nint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(nint hThread, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(nint hProcess, nint lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(nint hThread);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_NEW_CONSOLE = 0x00000010;
    private const uint CREATE_NO_WINDOW = 0x08000000;

    public static void StoreRequest(FullTrustProcessStartInfoRequest request)
    {
        lock (lockObject)
        {
            storedRequest = request;
        }
    }

    public static FullTrustStartProcessResult StartStoredProcess()
    {
        lock (lockObject)
        {
            if (storedRequest == null)
            {
                return new FullTrustStartProcessResult
                {
                    Succeeded = false,
                    ErrorMessage = "No stored process request"
                };
            }

            try
            {
                STARTUPINFO startupInfo = new STARTUPINFO();
                startupInfo.cb = Marshal.SizeOf<STARTUPINFO>();

                PROCESS_INFORMATION processInfo = new PROCESS_INFORMATION();

                string commandLine = string.IsNullOrEmpty(storedRequest.CommandLine)
                    ? $"\"{storedRequest.ApplicationName}\""
                    : $"\"{storedRequest.ApplicationName}\" {storedRequest.CommandLine}";

                bool success = CreateProcess(
                    null, // 使用命令行参数中的应用程序名
                    commandLine,
                    nint.Zero,
                    nint.Zero,
                    false,
                    CREATE_SUSPENDED,
                    nint.Zero,
                    storedRequest.CurrentDirectory,
                    ref startupInfo,
                    out processInfo);

                if (!success)
                {
                    int error = Marshal.GetLastWin32Error();
                    return new FullTrustStartProcessResult
                    {
                        Succeeded = false,
                        ErrorMessage = $"Failed to create process with CREATE_SUSPENDED flag. Error code: {error}"
                    };
                }

                currentProcess = Process.GetProcessById(processInfo.dwProcessId);

                processHandle = processInfo.hProcess;
                mainThreadHandle = processInfo.hThread;

                return new FullTrustStartProcessResult
                {
                    Succeeded = true,
                    ProcessId = (uint)processInfo.dwProcessId
                };
            }
            catch (Exception ex)
            {
                return new FullTrustStartProcessResult
                {
                    Succeeded = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }

    public static bool LoadLibrary(FullTrustLoadLibraryRequest request)
    {
        lock (lockObject)
        {
            if (currentProcess == null)
            {
                Console.WriteLine("No process running");
                return false;
            }

            try
            {
                Console.WriteLine($"Loading library: {request.LibraryName} from {request.LibraryPath}");

                CloseHandle(processHandle);
                // 重新打开进程句柄以获取必要的权限
                processHandle = OpenProcess(
                    PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                    PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                    false,
                    (uint)currentProcess.Id);

                if (processHandle == nint.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"Failed to open process handle with required permissions. Error: {error}");
                    return false;
                }

                // 获取LoadLibraryW函数地址
                nint kernel32Handle = GetModuleHandle("kernel32.dll");
                if (kernel32Handle == nint.Zero)
                {
                    Console.WriteLine("Failed to get kernel32 module handle");
                    return false;
                }

                nint loadLibraryAddr = GetProcAddress(kernel32Handle, "LoadLibraryW");
                if (loadLibraryAddr == nint.Zero)
                {
                    Console.WriteLine("Failed to get LoadLibraryW address");
                    return false;
                }

                // 在目标进程中分配内存
                byte[] libraryPathBytes = System.Text.Encoding.Unicode.GetBytes(request.LibraryPath + "\0");
                uint size = (uint)libraryPathBytes.Length;

                nint allocatedMemory = VirtualAllocEx(
                    processHandle,
                    nint.Zero,
                    size,
                    MEM_COMMIT | MEM_RESERVE,
                    PAGE_READWRITE);

                if (allocatedMemory == nint.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"Failed to allocate memory in target process. Error: {error}");
                    return false;
                }

                // 写入DLL路径到目标进程
                if (!WriteProcessMemory(processHandle, allocatedMemory, libraryPathBytes, size, out nint bytesWritten))
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"Failed to write to process memory. Error: {error}");
                    VirtualFreeEx(processHandle, allocatedMemory, 0, 0x8000); // MEM_RELEASE
                    return false;
                }

                // 创建远程线程调用LoadLibraryW
                nint threadId;
                nint remoteThread = CreateRemoteThread(
                    processHandle,
                    nint.Zero,
                    0,
                    loadLibraryAddr,
                    allocatedMemory,
                    0,
                    out threadId);

                if (remoteThread == nint.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"Failed to create remote thread. Error: {error}");
                    VirtualFreeEx(processHandle, allocatedMemory, 0, 0x8000); // MEM_RELEASE
                    return false;
                }

                // 等待线程完成
                uint waitResult = WaitForSingleObject(remoteThread, 10000); // 10秒超时
                if (waitResult == 0x00000102) // WAIT_TIMEOUT
                {
                    Console.WriteLine("Timeout waiting for DLL load");
                    CloseHandle(remoteThread);
                    VirtualFreeEx(processHandle, allocatedMemory, 0, 0x8000); // MEM_RELEASE
                    return false;
                }
                else if (waitResult == 0xFFFFFFFF) // WAIT_FAILED
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"Wait failed. Error: {error}");
                    CloseHandle(remoteThread);
                    VirtualFreeEx(processHandle, allocatedMemory, 0, 0x8000); // MEM_RELEASE
                    return false;
                }

                // 获取线程退出代码（即DLL的基地址）
                if (!GetExitCodeThread(remoteThread, out uint exitCode))
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"Failed to get thread exit code. Error: {error}");
                }
                else
                {
                    Console.WriteLine($"DLL loaded at address: 0x{exitCode:X8}");
                }

                // 清理
                CloseHandle(remoteThread);
                VirtualFreeEx(processHandle, allocatedMemory, 0, 0x8000); // MEM_RELEASE

                return exitCode != 0; // LoadLibrary成功返回非零值
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading library: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }
    }

    public static bool ResumeMainThread()
    {
        lock (lockObject)
        {
            if (currentProcess == null || mainThreadHandle == nint.Zero)
            {
                Console.WriteLine("No process running or thread handle not available");
                return false;
            }

            try
            {
                Console.WriteLine("Resuming main thread");

                uint result = ResumeThread(mainThreadHandle);

                if (result != unchecked((uint)-1))
                {
                    Console.WriteLine($"Resumed main thread, previous suspend count: {result}");

                    CloseHandle(mainThreadHandle);
                    mainThreadHandle = nint.Zero;

                    return true;
                }
                else
                {
                    Console.WriteLine($"Failed to resume thread. Error: {Marshal.GetLastWin32Error()}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resuming main thread: {ex.Message}");
                return false;
            }
        }
    }

    public static void Cleanup()
    {
        lock (lockObject)
        {
            if (processHandle != nint.Zero)
            {
                CloseHandle(processHandle);
                processHandle = nint.Zero;
            }

            if (mainThreadHandle != nint.Zero)
            {
                CloseHandle(mainThreadHandle);
                mainThreadHandle = nint.Zero;
            }

            currentProcess?.Dispose();
            currentProcess = null;
            storedRequest = null;
        }
    }
}
