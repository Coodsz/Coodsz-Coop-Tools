using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RealmOfThronesCoopCompat
{
    // On the server and on players' PCs: RotRules.SkipWithoutItem as a prefix on Realm of Thrones'
    // ROTWeaponComponetData.patch1, found by name once ROT.dll is loaded, so a weapon Coop reads off
    // the network with no item no longer throws inside RoT's patch and drops the whole message.
    // patch1 is 112 bytes of IL, too big for the JIT to inline into the constructor, so the
    // constructor needs no rebuild. The first skip in each Coop session is logged.
    internal static class WeaponItemGate
    {
        private static readonly object Gate = new object();
        private static Harmony _harmony;
        private static bool _tried;

        public static void Install()
        {
            _harmony = new Harmony(MapRedirect.HarmonyId);
            RotRules.OnWeaponSkipped = () => SubModule.Log("Weapon gate: Coop sent a weapon with no item, so Realm of Thrones' ROTWeaponComponetData.patch1 was skipped for it and the message went through (logged once per session).");
            Coop.SessionChanged += _ => RotRules.ResetWeaponSkipLog();
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryPatch(e.LoadedAssembly);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                TryPatch(assembly);
        }

        private static void TryPatch(Assembly assembly)
        {
            try
            {
                if (_tried || assembly == null || assembly.GetName().Name != "ROT")
                    return;
                lock (Gate)
                {
                    if (_tried)
                        return;
                    _tried = true;
                }
                var type = assembly.GetType(RotRules.WeaponPatchType, false);
                var patch = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .SingleOrDefault(m => m.Name == RotRules.WeaponPatchMethod && m.GetParameters().FirstOrDefault()?.Name == "item"
                        && !m.GetParameters()[0].ParameterType.IsValueType);
                if (patch == null)
                {
                    SubModule.Log("Weapon gate: " + RotRules.WeaponPatchType + "." + RotRules.WeaponPatchMethod + "(item, ...) not found, so nothing is gated; Realm of Thrones may have fixed or moved it.");
                    return;
                }
                _harmony.Patch(patch, prefix: new HarmonyMethod(typeof(RotRules), nameof(RotRules.SkipWithoutItem)));
                SubModule.Log("Weapon gate: Realm of Thrones' ROTWeaponComponetData.patch1 is skipped for a weapon Coop sends with no item.");
            }
            catch (Exception ex)
            {
                SubModule.Log("Weapon gate could not be put in, so a weapon Coop sends with no item may still drop its message: " + ex.Message);
            }
        }
    }
}
