using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Wideska.Models;

namespace Wideska.Services;

public sealed class LocalStore(string? storageDirectory = null)
{
    private readonly string directory = storageDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wideska");
    private readonly SemaphoreSlim gate = new(1, 1);
    private string PathFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Storage names must be plain file names.", nameof(name));
        return Path.Combine(directory, name);
    }
    public async Task<T?> ReadAsync<T>(string name, bool encrypted, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            string path = PathFor(name);
            if (!File.Exists(path)) return default;
            byte[] data = await File.ReadAllBytesAsync(path, token);
            byte[] decoded = encrypted ? await Task.Run(() => WindowsProtection.Transform(data, false), token) : data;
            try { return await Task.Run(() => JsonSerializer.Deserialize<T>(decoded), token); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(decoded); }
        }
        catch (Exception ex) when (ex is IOException or JsonException or System.ComponentModel.Win32Exception) { return default; }
        finally { gate.Release(); }
    }
    public async Task WriteAsync<T>(string name, T value, bool encrypted, CancellationToken token = default)
    {
        string path = PathFor(name);
        await gate.WaitAsync(token);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            byte[] data = value is CalendarSnapshot
                ? await Task.Run(() => JsonSerializer.SerializeToUtf8Bytes(value), token)
                : JsonSerializer.SerializeToUtf8Bytes(value);
            if (encrypted)
            {
                byte[] plaintext = data;
                try { data = await Task.Run(() => WindowsProtection.Transform(plaintext, true), token); }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(plaintext); }
            }
            await File.WriteAllBytesAsync(temporary, data, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            gate.Release();
        }
    }
    public async Task DeleteAsync(string name)
    {
        await gate.WaitAsync();
        try { File.Delete(PathFor(name)); }
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
