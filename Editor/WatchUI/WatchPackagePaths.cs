using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor.PackageManager;

[assembly: InternalsVisibleTo("AppleWatchController.Build.Editor")]
[assembly: InternalsVisibleTo("AppleWatchController.WatchUI.Tests.Editor")]

namespace AppleWatchController.Editor
{
    /// <summary>Resolves package resources independently of the package's physical install location.</summary>
    public static class WatchPackagePaths
    {
        /// <summary>The stable Package Manager identifier.</summary>
        public const string PackageId = "com.ilter.apple-watch-controller";

        /// <summary>Returns a Package Manager asset path for a package-relative resource.</summary>
        /// <exception cref="ArgumentException">The relative path is blank, absolute, or contains traversal.</exception>
        /// <exception cref="InvalidOperationException">Package metadata is unavailable.</exception>
        public static string AssetPath(string relativePath)
        {
            var path = ValidateRelativePath(relativePath);
            return GetPackage().assetPath.TrimEnd('/') + "/" + path;
        }

        /// <summary>Returns a physical file path for a package-relative resource.</summary>
        /// <remarks>Does not create or modify the resource.</remarks>
        /// <exception cref="ArgumentException">The relative path is blank, absolute, or contains traversal.</exception>
        /// <exception cref="InvalidOperationException">Package metadata is unavailable.</exception>
        public static string FilePath(string relativePath)
        {
            var path = ValidateRelativePath(relativePath);
            return Path.GetFullPath(Path.Combine(GetPackage().resolvedPath, path));
        }

        private static string ValidateRelativePath(string relativePath)
        {
            var normalized = relativePath?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized)
                || normalized.Contains(':') || normalized.Split('/').Any(part => part == ".." || part == "."))
                throw new ArgumentException("A package-relative path without traversal is required.", nameof(relativePath));
            return normalized;
        }

        private static PackageInfo GetPackage()
        {
            var package = PackageInfo.FindForAssembly(typeof(WatchPackagePaths).Assembly);
            if (package == null || package.name != PackageId || string.IsNullOrWhiteSpace(package.assetPath)
                || string.IsNullOrWhiteSpace(package.resolvedPath))
                throw new InvalidOperationException("Apple Watch Controller package metadata is unavailable. Install the package through Package Manager.");
            return package;
        }
    }
}
