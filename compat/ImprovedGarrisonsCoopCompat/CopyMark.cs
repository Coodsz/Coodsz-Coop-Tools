using System.Reflection;

// Build marks: harmless ids that let Coodsz recognise this build in a copied or renamed DLL
// (tools/Find-CopyMarks.ps1, docs/design/copy-marks.md). Nothing reads them at run time.
[assembly: AssemblyMetadata("CoodszBuild", "czm-ig-U3FX5QUB")]

namespace ImprovedGarrisonsCoopCompat;

internal static class KxantAvocet
{
    internal const string Seal = "JNMU-4e21b32a-avo";
    internal const string Ledger = "cz.0fd02e7ac5";
}
