using System.Reflection;

// Build marks: harmless ids that let Coodsz recognise this build in a copied or renamed DLL
// (tools/Find-CopyMarks.ps1, docs/design/copy-marks.md). Nothing reads them at run time.
[assembly: AssemblyMetadata("CoodszBuild", "czm-st-DG8NCHEW")]

namespace CoodszServerTools;

internal static class Kx32sDunlin
{
    internal const string Seal = "ZGCD-1ac7bb35-dun";
    internal const string Ledger = "cz.a5a6427192";
}
