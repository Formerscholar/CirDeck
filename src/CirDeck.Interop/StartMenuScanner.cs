using System.Runtime.InteropServices;
using System.Text;
using CirDeck.Core;

namespace CirDeck.Interop;

public static class StartMenuScanner
{
    public static IReadOnlyList<AppEntry> Scan(IEnumerable<string>? roots = null)
    {
        var found = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots ?? DefaultRoots())
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (ShouldSkip(name))
                {
                    continue;
                }

                if (!TryResolve(file, out var target, out var arguments, out var workingDirectory))
                {
                    continue;
                }

                if (!seen.Add(target))
                {
                    continue;
                }

                var source = root.Contains("Desktop", StringComparison.OrdinalIgnoreCase) ? "Desktop" : "StartMenu";
                found.Add(new AppEntry(name, target, arguments, workingDirectory, source));
            }
        }

        found.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
        return found;
    }

    public static IEnumerable<string> DefaultRoots()
    {
        var start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
        var userStart = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        yield return start;
        yield return userStart;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    }

    private static bool IsLaunchable(string target)
    {
        if (!File.Exists(target))
        {
            return false;
        }

        var extension = Path.GetExtension(target);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".msc", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSkip(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        return name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
            || name.Contains("卸载", StringComparison.Ordinal);
    }

    private static bool TryResolve(string linkPath, out string target, out string arguments, out string workingDirectory)
    {
        target = string.Empty;
        arguments = string.Empty;
        workingDirectory = string.Empty;
        object? instance = null;
        try
        {
            instance = new ShellLink();
            var file = (IPersistFile)instance;
            file.Load(linkPath, 0);
            var link = (IShellLinkW)instance;
            var buffer = new StringBuilder(1024);
            link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 4);
            if (buffer.Length == 0)
            {
                link.Resolve(IntPtr.Zero, 1);
                buffer.Clear();
                link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 4);
            }

            target = Environment.ExpandEnvironmentVariables(buffer.ToString()).Trim();
            if (target.Length == 0 || !IsLaunchable(target))
            {
                return false;
            }

            buffer.Clear();
            link.GetArguments(buffer, buffer.Capacity);
            arguments = buffer.ToString();
            buffer.Clear();
            link.GetWorkingDirectory(buffer, buffer.Capacity);
            workingDirectory = Environment.ExpandEnvironmentVariables(buffer.ToString()).Trim();
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            if (instance != null && Marshal.IsComObject(instance))
            {
                Marshal.ReleaseComObject(instance);
            }
        }
    }
}

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLink
{
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, IntPtr findData, uint flags);

    void GetIDList(out IntPtr pidl);

    void SetIDList(IntPtr pidl);

    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int capacity);

    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);

    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int capacity);

    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);

    void GetHotkey(out short hotkey);

    void SetHotkey(short hotkey);

    void GetShowCmd(out int showCmd);

    void SetShowCmd(int showCmd);

    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int capacity, out int iconIndex);

    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

    void Resolve(IntPtr hwnd, uint flags);

    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("0000010b-0000-0000-C000-000000000046")]
internal interface IPersistFile
{
    void GetClassID(out Guid classId);

    [PreserveSig]
    int IsDirty();

    void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);

    void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, bool remember);

    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);

    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
}
