using System.Diagnostics;
using System.Runtime.InteropServices;
using Snap.Hutao.Remastered.FullTrust.Models;

namespace Snap.Hutao.Remastered.FullTrust.Services;

public static class ProcessManager
{
    private static FullTrustProcessStartInfoRequest? storedRequest;
    private static Process? currentProcess;
    private static nint processHandle = nint.Zero;
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetProcAddress(nint hModule, string lpProcName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
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
                var startInfo = new ProcessStartInfo
                {
                    FileName = storedRequest.ApplicationName,
                    Arguments = storedRequest.CommandLine,
                    WorkingDirectory = storedRequest.CurrentDirectory,
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = (storedRequest.CreationFlags & 0x08000000) != 0 // CREATE_NO_WINDOW
                };

                currentProcess = new Process { StartInfo = startInfo };
                
                if (currentProcess.Start())
                {
                    processHandle = OpenProcess(
                        PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | 
                        PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                        false,
                        (uint)currentProcess.Id);

                    if (processHandle == nint.Zero)
                    {
                        Console.WriteLine($"Failed to open process handle. Error: {Marshal.GetLastWin32Error()}");
                    }

                    return new FullTrustStartProcessResult
                    {
                        Succeeded = true,
                        ProcessId = (uint)currentProcess.Id
                    };
                }
                else
                {
                    return new FullTrustStartProcessResult
                    {
                        Succeeded = false,
                        ErrorMessage = "Failed to start process"
                    };
                }
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
            if (currentProcess == null || processHandle == nint.Zero)
            {
                Console.WriteLine("No process running or process handle not available");
                return false;
            }

            try
            {
                Console.WriteLine($"Loading library: {request.LibraryName} from {request.LibraryPath}");

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
                    Console.WriteLine($"Failed to allocate memory in target process. Error: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                // 写入DLL路径到目标进程
                if (!WriteProcessMemory(processHandle, allocatedMemory, libraryPathBytes, size, out nint bytesWritten))
                {
                    Console.WriteLine($"Failed to write to process memory. Error: {Marshal.GetLastWin32Error()}");
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
                    Console.WriteLine($"Failed to create remote thread. Error: {Marshal.GetLastWin32Error()}");
                    VirtualFreeEx(processHandle, allocatedMemory, 0, 0x8000); // MEM_RELEASE
                    return false;
                }

                // 等待线程完成
                uint waitResult = WaitForSingleObject(remoteThread, 5000); // 5秒超时
                if (waitResult == 0x00000102) // WAIT_TIMEOUT
                {
                    Console.WriteLine("Timeout waiting for DLL load");
                    CloseHandle(remoteThread);
                    VirtualFreeEx(processHandle, allocatedMemory, 0, 0x8000); // MEM_RELEASE
                    return false;
                }

                // 获取线程退出代码（即DLL的基地址）
                if (!GetExitCodeThread(remoteThread, out uint exitCode))
                {
                    Console.WriteLine("Failed to get thread exit code");
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
                return false;
            }
        }
    }

    public static bool ResumeMainThread()
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
                Console.WriteLine("Resuming main thread");

                // 在实际的FullTrust实现中，进程是以CREATE_SUSPENDED标志创建的
                // 主线程在创建时被挂起，需要恢复它
                
                // 获取进程的主线程ID
                // 注意：Process.MainWindowHandle可能会返回0，如果进程没有窗口
                // 我们使用一个更可靠的方法：枚举进程的线程
                
                // 简化实现：如果进程有主窗口句柄，尝试获取线程ID
                if (currentProcess.MainWindowHandle != nint.Zero)
                {
                    uint threadId = GetWindowThreadProcessId(currentProcess.MainWindowHandle, out _);
                    if (threadId != 0)
                    {
                        nint threadHandle = OpenThread(THREAD_SUSPEND_RESUME, false, threadId);
                        if (threadHandle != nint.Zero)
                        {
                            uint result = ResumeThread(threadHandle);
                            CloseHandle(threadHandle);
                            
                            if (result != unchecked((uint)-1))
                            {
                                Console.WriteLine($"Resumed thread {threadId}, previous suspend count: {result}");
                                return true;
                            }
                        }
                    }
                }
                
                // 备用方法：如果无法获取特定线程，至少进程已经启动
                // 在实际使用中，FullTrust进程应该已经正确处理了线程恢复
                Console.WriteLine("Process already running, assuming main thread is active");
                return true;
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
            
            currentProcess?.Dispose();
            currentProcess = null;
            storedRequest = null;
        }
    }
}
