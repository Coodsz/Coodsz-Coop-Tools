using System;
using System.Linq;
using System.Net;
using HarmonyLib;

namespace RealmOfThronesCoopCompat
{
    // For our own live tests only: with COODSZ_TEST_LOOPBACK_BIND=1 set for the server, Coop's game
    // port listens on 127.0.0.1 only, so Windows asks no firewall question for a new server path.
    // Coop binds every network (Coop.Core NetworkConfig.BindAddressIPv4 is IPAddress.Any, with no
    // setting to change it), so the address is swapped where Coop hands it to LiteNetLib's
    // NetManager.Start(IPAddress, IPAddress, int). Hosts never set the variable, so a real server
    // keeps listening on every network and players on other PCs can join.
    internal static class TestLoopback
    {
        public const string Variable = "COODSZ_TEST_LOOPBACK_BIND";

        private static readonly object Gate = new object();
        private static bool _done;

        public static bool IsOn(string value) => string.Equals(value?.Trim(), "1", StringComparison.Ordinal);

        public static void Install()
        {
            if (!IsOn(Environment.GetEnvironmentVariable(Variable)))
                return;
            SubModule.Log("Test loopback: " + Variable + "=1, so Coop's game port listens on 127.0.0.1 only and players on other PCs cannot join.");
            SafeTryPatch();
            if (!_done)
                AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
                {
                    try
                    {
                        if (!_done && e.LoadedAssembly.GetName().Name == "LiteNetLib")
                            SafeTryPatch();
                    }
                    catch { }
                };
        }

        private static void SafeTryPatch()
        {
            try
            {
                lock (Gate)
                {
                    if (!_done)
                        TryPatch();
                }
            }
            catch (Exception ex)
            {
                SubModule.Log("Test loopback: could not patch NetManager.Start, so the server listens on every network: " + ex.Message);
            }
        }

        private static void TryPatch()
        {
            var manager = Coop.FindType("LiteNetLib.NetManager");
            if (manager == null)
                return;
            _done = true;
            var start = AccessTools.GetDeclaredMethods(manager).FirstOrDefault(method =>
                method.Name == "Start" && method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(new[] { typeof(IPAddress), typeof(IPAddress), typeof(int) }));
            if (start == null)
            {
                SubModule.Log("Test loopback: LiteNetLib's NetManager.Start(IPAddress, IPAddress, int) not found, so the server listens on every network.");
                return;
            }
            new Harmony(MapRedirect.HarmonyId + ".testloopback").Patch(start, prefix: new HarmonyMethod(typeof(TestLoopback), nameof(Prefix)));
            SubModule.Log("Test loopback: NetManager.Start patched.");
        }

        private static void Prefix(ref IPAddress __0, ref IPAddress __1, int __2)
        {
            __0 = IPAddress.Loopback;
            __1 = IPAddress.IPv6Loopback;
            SubModule.Log("Test loopback: the game port " + __2 + " listens on 127.0.0.1 and ::1 only.");
        }
    }
}
