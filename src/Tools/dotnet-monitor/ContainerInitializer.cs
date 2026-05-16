// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal static class ContainerInitializer
    {
        // Ubuntu chiseled images use glibc; libc.so.6 is the stable soname and avoids
        // depending on a libc.so linker symlink that runtime/distroless images may not include.
        private const string LibC = "libc.so.6";

        private const string ContainerInitEnvironmentVariable = ToolIdentifiers.StandardPrefix + "ContainerInit";
        private const string SharedPathEnvironmentVariable = ToolIdentifiers.StandardPrefix + "Storage__DefaultSharedPath";

        private const uint RootUserId = 0;
        private const uint DiagnosticsGroupId = 1654;
        private const UnixFileMode SharedPathMode =
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupWrite |
            UnixFileMode.GroupExecute |
            UnixFileMode.SetGroup;
        private const uint ProcessUmask = 7; // 0007

        public static bool TryInitialize(out string? errorMessage)
        {
            errorMessage = null;

            if (!ToolIdentifiers.IsEnvVarEnabled(ContainerInitEnvironmentVariable))
            {
                return true;
            }

            if (!OperatingSystem.IsLinux())
            {
                errorMessage = "Container init failed: DotnetMonitor_ContainerInit is only supported on Linux.";
                return false;
            }

            try
            {
                string? sharedPath = Environment.GetEnvironmentVariable(SharedPathEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(sharedPath))
                {
                    errorMessage = $"Container init failed: {SharedPathEnvironmentVariable} must be set.";
                    return false;
                }

                if (!Path.IsPathFullyQualified(sharedPath))
                {
                    errorMessage = $"Container init failed: {SharedPathEnvironmentVariable} must be an absolute path.";
                    return false;
                }

                foreach (string path in Directory.EnumerateFileSystemEntries(sharedPath))
                {
                    DeleteFileSystemEntry(path);
                }

                ThrowIfFailed(nameof(Chown), Chown(sharedPath, RootUserId, DiagnosticsGroupId), sharedPath);
                File.SetUnixFileMode(sharedPath, SharedPathMode);
                Umask(ProcessUmask);
            }
            catch (Exception ex)
            {
                errorMessage = $"Container init failed: {ex.Message}";
                return false;
            }

            return true;
        }

        private static void DeleteFileSystemEntry(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.Directory) &&
                !attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
        }

        private static void ThrowIfFailed(string operation, int result, string path)
        {
            if (result == 0)
            {
                return;
            }

            int error = Marshal.GetLastWin32Error();
            throw new IOException($"{operation} failed for '{path}' with errno {error}.");
        }

        [DllImport(LibC, EntryPoint = "chown", SetLastError = true)]
        private static extern int Chown(string path, uint owner, uint group);

        [DllImport(LibC, EntryPoint = "umask")]
        private static extern uint Umask(uint mask);
    }
}
