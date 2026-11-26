using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Wideska.Models;

namespace Wideska.Services;
public sealed class LocalStore(string? storageDirectory = null)
{
    private readonly string directory = storageDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wideska");
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<T?> ReadAsync<T>(string name, bool encrypted, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            string path = Path.Combine(directory, name);
            if (!File.Exists(path)) return default;
            byte[] data = await File.ReadAllBytesAsync(path, token);
            return JsonSerializer.Deserialize<T>(encrypted ? WindowsProtection.Transform(data, false) : data);
        }
        catch (Exception ex) when (ex is IOException or JsonException or System.ComponentModel.Win32Exception) { return default; }
        finally { gate.Release(); }
    }
    public async Task WriteAsync<T>(string name, T value, bool encrypted, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(directory);
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(value);
            if (encrypted) data = WindowsProtection.Transform(data, true);
            string path = Path.Combine(directory, name);
            await File.WriteAllBytesAsync(path + ".tmp", data, token);
            File.Move(path + ".tmp", path, true);
        }
        finally { gate.Release(); }
    }
    public async Task DeleteAsync(string name)
    {
        await gate.WaitAsync();
        try { File.Delete(Path.Combine(directory, name)); }
        finally { gate.Release(); }
    }
}
internal static class WindowsProtection
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    public static byte[] Transform(byte[] data, bool protect)
    {
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            Blob output;
            bool success = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try { byte[] result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result; }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
}
