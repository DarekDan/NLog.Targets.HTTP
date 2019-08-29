using System.Reflection;

// This should be the same version as below
[assembly: AssemblyFileVersion("1.0.0.0")]

#if DEBUG
[assembly: AssemblyInformationalVersion("1.0.0-PreRelease")]
#else
[assembly: AssemblyInformationalVersion("1.0.0")]
#endif
