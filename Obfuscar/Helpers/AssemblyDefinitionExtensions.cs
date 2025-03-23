﻿using Mono.Cecil;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using NuGet.Versioning;  // Add reference to NuGet.Versioning

namespace Obfuscar.Helpers
{
    public static class AssemblyDefinitionExtensions
    {
        public static string GetPortableProfileDirectory(this AssemblyDefinition assembly)
        {
            foreach (var custom in assembly.CustomAttributes)
            {
                if (custom.AttributeType.FullName == "System.Runtime.Versioning.TargetFrameworkAttribute")
                {
                    if (!custom.HasProperties)
                        continue;
                    var framework = custom.Properties.First(property => property.Name == "FrameworkDisplayName");
                    var content = framework.Argument.Value.ToString();
                    if (!string.Equals(content, ".NET Portable Subset"))
                    {
                        return null;
                    }

                    var parts = custom.ConstructorArguments[0].Value.ToString().Split(',');
                    var root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    return Environment.ExpandEnvironmentVariables(
                        Path.Combine(
                            root,
                            "Reference Assemblies",
                            "Microsoft",
                            "Framework",
                            parts[0],
                            (parts[1].Split('='))[1],
                            "Profile",
                            (parts[2].Split('='))[1]));
                }
            }

            return null;
        }

        public static IEnumerable<string> GetNetCoreDirectories(this AssemblyDefinition assembly)
        {
            var seenFrameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var custom in assembly.CustomAttributes)
            {
                if (custom.AttributeType.FullName == "System.Runtime.Versioning.TargetFrameworkAttribute")
                {
                    var framework = custom.ConstructorArguments[0].Value.ToString();

                    // Normalize framework string (e.g., ".NETCoreApp,Version=6.0" -> ".NETCoreApp,Version=v6.0")
                    if (framework.StartsWith(".NETCoreApp,Version=") && !framework.Contains("v"))
                    {
                        framework = framework.Replace("Version=", "Version=v");
                    }

                    // Skip if this framework has already been processed
                    if (!seenFrameworks.Add(framework))
                        continue;

                    // Handle .NET Core
                    if (framework.StartsWith(".NETCoreApp,Version="))
                    {
                        var versionStr = framework.Split('=')[1].Substring(1);

                        string[] profiles = new[]
                        {
                            "Microsoft.AspNetCore.App.Ref",
                            "Microsoft.NETCore.App.Ref",
                            "Microsoft.WindowsDesktop.App.Ref"
                        };

                        foreach (var profile in profiles)
                        {
                            var baseDir = Environment.OSVersion.Platform == PlatformID.Unix || 
                                          Environment.OSVersion.Platform == PlatformID.MacOSX
                                ? $"/usr/local/share/dotnet/packs/{profile}"
                                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "packs", profile);

                            if (Directory.Exists(baseDir))
                            {
                                yield return Path.Combine(
                                    FindBestVersionMatch(baseDir, versionStr),
                                    "ref",
                                    $"net{versionStr}");
                            }
                        }
                    }
                    // Handle .NET Standard
                    else if (framework.StartsWith(".NETStandard,Version="))
                    {
                        var versionStr = framework.Split('=')[1].Substring(1);

                        if (Version.TryParse(versionStr, out Version parsedVersion) && parsedVersion <= new Version(2, 0))
                        {
                            // For .NET Standard 1.x and 2.0, check the NuGet fallback folder
                            string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? Environment.GetEnvironmentVariable("HOME");
                            string nugetPackagesPath = Path.Combine(homeDir, ".nuget", "packages", "netstandard.library");

                            if (Directory.Exists(nugetPackagesPath))
                            {
                                string bestVersion = FindBestNuGetVersionMatch(nugetPackagesPath, versionStr);
                                if (!string.IsNullOrEmpty(bestVersion))
                                {
                                    yield return Path.Combine(nugetPackagesPath, bestVersion, "build", $"netstandard{versionStr}", "ref");
                                }
                            }
                        }
                        else
                        {
                            // For .NET Standard 2.1 and above, check the .NET SDK packs directory
                            var baseDir = Environment.OSVersion.Platform == PlatformID.Unix || 
                                                Environment.OSVersion.Platform == PlatformID.MacOSX
                                    ?  "/usr/local/share/dotnet/packs/NETStandard.Library.Ref"
                                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "packs", "NETStandard.Library.Ref");                       
                            if (Directory.Exists(baseDir))
                            {
                                yield return Path.Combine(FindBestVersionMatch(baseDir, versionStr), "ref", $"netstandard{versionStr}");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Finds the best matching version directory for a given major.minor version
        /// </summary>
        private static string FindBestVersionMatch(string baseDir, string versionStr)
        {
            if (!Directory.Exists(baseDir))
                return Path.Combine(baseDir, versionStr);

            // Get all version directories
            var allDirs = Directory.GetDirectories(baseDir);
            
            // Parse the requested version
            var requestedVersion = new NuGetVersion(versionStr);
            
            // Find matching directories with the same major.minor version
            var matchingDirs = new List<(string Path, NuGetVersion Version)>();
            
            foreach (var dir in allDirs)
            {
                var dirName = Path.GetFileName(dir);
                if (NuGetVersion.TryParse(dirName, out var dirVersion) && 
                    dirVersion.Major == requestedVersion.Major && 
                    dirVersion.Minor == requestedVersion.Minor)
                {
                    matchingDirs.Add((dir, dirVersion));
                }
            }
            
            // If there are no matching directories, fall back to exact version
            if (matchingDirs.Count == 0)
            {
                // Look for exact match
                var exactMatch = allDirs.FirstOrDefault(d => Path.GetFileName(d) == versionStr);
                if (exactMatch != null)
                    return exactMatch;
                
                // If no exact match exists either, return constructed path
                return Path.Combine(baseDir, $"{versionStr}.0");
            }
            
            // Sort directories by version and return the highest one
            // Stable versions are preferred over prerelease ones
            var bestMatch = matchingDirs
                .OrderByDescending(x => x.Version, VersionComparer.Default)
                .First();
            
            return bestMatch.Path;
        }
        
        /// <summary>
        /// Finds the best matching version directory for a given major.minor version in NuGet packages
        /// </summary>
        private static string FindBestNuGetVersionMatch(string baseDir, string versionStr)
        {
            if (!Directory.Exists(baseDir))
                return null;

            // Get all version directories
            var allDirs = Directory.GetDirectories(baseDir);
            
            // Parse the requested version
            if (!NuGetVersion.TryParse(versionStr, out var requestedVersion))
                return null;
            
            // Find matching directories with the same major.minor version
            var matchingDirs = new List<(string Path, NuGetVersion Version)>();
            
            foreach (var dir in allDirs)
            {
                var dirName = Path.GetFileName(dir);
                if (NuGetVersion.TryParse(dirName, out var dirVersion) && 
                    dirVersion.Major == requestedVersion.Major && 
                    dirVersion.Minor == requestedVersion.Minor)
                {
                    matchingDirs.Add((dir, dirVersion));
                }
            }
            
            // If there are no matching directories, fall back to exact version
            if (matchingDirs.Count == 0)
            {
                // Look for exact match
                var exactMatch = allDirs.FirstOrDefault(d => NuGetVersion.TryParse(Path.GetFileName(d), out var v) && v == requestedVersion);
                if (exactMatch != null)
                    return Path.GetFileName(exactMatch);
                
                // If no exact match exists either, return null
                return null;
            }
            
            // Sort directories by version and return the highest one
            // Stable versions are preferred over prerelease ones by default with VersionComparer
            var bestMatch = matchingDirs
                .OrderByDescending(x => x.Version, VersionComparer.Default)
                .First();
            
            return Path.GetFileName(bestMatch.Path);
        }

        public static bool MarkedToRename(this AssemblyDefinition assembly)
        {
            foreach (var custom in assembly.CustomAttributes)
            {
                if (custom.AttributeType.FullName == typeof(ObfuscateAssemblyAttribute).FullName)
                {
                    var rename = (bool)(Helper.GetAttributePropertyByName(custom, "AssemblyIsPrivate") ?? true);
                    return rename;
                }
            }

            // IMPORTANT: assume it should be renamed.
            return true;
        }

        public static bool CleanAttributes(this AssemblyDefinition assembly)
        {
            for (int i = 0; i < assembly.CustomAttributes.Count; i++)
            {
                CustomAttribute custom = assembly.CustomAttributes[i];
                if (custom.AttributeType.FullName == typeof(ObfuscateAssemblyAttribute).FullName)
                {
                    if ((bool)(Helper.GetAttributePropertyByName(custom, "StripAfterObfuscation") ?? true))
                    {
                        assembly.CustomAttributes.Remove(custom);
                    }
                }
            }

            return true;
        }
    }
}
