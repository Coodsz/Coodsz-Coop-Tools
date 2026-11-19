using Mono.Cecil;
using Mono.Cecil.Cil;
// Usage: lbspatch <in.dll> <out.dll>
var module = ModuleDefinition.ReadModule(args[0], new ReaderParameters { InMemory = true });
var type = module.Types.First(t => t.FullName == "DedicatedServer.LauncherBattleSize.CampaignRuleOverrides");
var method = type.Methods.First(m => m.Name == "TryInstallOccupancyPausePatch");
if (method.Body.Instructions.Any(i => i.Operand is string s && s == "BANNERLORD_DS_AUTO_PAUSE"))
{
    Console.WriteLine("already patched");
    return;
}
var installed = type.Fields.First(f => f.Name == "_occupancyPausePatchInstalled");
var getVariable = Existing(module, "System.Environment", "GetEnvironmentVariable");
var equals = Existing(module, "System.String", "op_Equality");
var writeLine = Existing(module, "System.Console", "WriteLine", "System.String");
var il = method.Body.GetILProcessor();
var first = method.Body.Instructions[0];
// if (Environment.GetEnvironmentVariable("BANNERLORD_DS_AUTO_PAUSE") == "1") { mark installed; log; return; }
var added = new[]
{
    // Already handled: the original code just returns, so the line is logged once.
    il.Create(OpCodes.Ldsfld, installed),
    il.Create(OpCodes.Brtrue, first),
    il.Create(OpCodes.Ldstr, "BANNERLORD_DS_AUTO_PAUSE"),
    il.Create(OpCodes.Call, getVariable),
    il.Create(OpCodes.Ldstr, "1"),
    il.Create(OpCodes.Call, equals),
    il.Create(OpCodes.Brfalse, first),
    il.Create(OpCodes.Ldc_I4_1),
    il.Create(OpCodes.Stsfld, installed),
    il.Create(OpCodes.Ldstr, "[DedicatedServer] Auto pause is on (Coodsz Host): settlements and battles keep pausing campaign time."),
    il.Create(OpCodes.Call, writeLine),
    il.Create(OpCodes.Ret),
};
foreach (var instruction in added.Reverse())
    il.InsertBefore(method.Body.Instructions[0], instruction);
module.Write(args[1]);
Console.WriteLine("patched");

static MethodReference Existing(ModuleDefinition module, string type, string name, string? parameter = null) =>
    module.Types.SelectMany(t => t.Methods.Concat(t.NestedTypes.SelectMany(n => n.Methods)))
        .Where(m => m.HasBody)
        .SelectMany(m => m.Body.Instructions)
        .Select(i => i.Operand as MethodReference)
        .First(r => r != null && r.DeclaringType.FullName == type && r.Name == name
            && (parameter == null || (r.Parameters.Count == 1 && r.Parameters[0].ParameterType.FullName == parameter)))!;
