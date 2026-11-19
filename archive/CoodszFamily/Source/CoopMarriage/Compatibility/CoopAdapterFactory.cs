using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace CoopMarriage.Compatibility;

internal static class CoopAdapterFactory
{
	internal static ICoopAdapter Create(bool dedicatedServer)
	{
		Type type = AccessTools.TypeByName("Common.ModInformation") ?? FindCompatibleModInformation();
		if (type == null)
		{
			return new UnavailableCoopAdapter(dedicatedServer,
				"required public type Common.ModInformation (or compatible exported ModInformation type) was not found");
		}

		PropertyInfo isClient = FindStaticBool(type, "IsClient");
		PropertyInfo isServer = FindStaticBool(type, "IsServer");
		if (isClient == null && isServer == null)
		{
			return new UnavailableCoopAdapter(dedicatedServer,
				type.Assembly.GetName().Name + ": expected public static bool IsClient or IsServer on " + type.FullName);
		}

		return new ReflectionCoopAdapter(type, isClient, isServer, dedicatedServer);
	}

	private static Type FindCompatibleModInformation()
	{
		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			string assemblyName = assembly.GetName().Name ?? string.Empty;
			Type[] types;
			try
			{
				types = assembly.GetExportedTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				types = ex.Types.Where(x => x != null).ToArray();
			}
			catch
			{
				continue;
			}

			Type match = types.FirstOrDefault(x => x.Name == "ModInformation" &&
				(assemblyName.Equals("Common", StringComparison.OrdinalIgnoreCase) ||
				 assemblyName.IndexOf("Coop", StringComparison.OrdinalIgnoreCase) >= 0 ||
				 assemblyName.IndexOf("Berrula", StringComparison.OrdinalIgnoreCase) >= 0 ||
				 (x.Namespace ?? string.Empty).IndexOf("Coop", StringComparison.OrdinalIgnoreCase) >= 0) &&
				(FindStaticBool(x, "IsClient") != null || FindStaticBool(x, "IsServer") != null));
			if (match != null)
			{
				return match;
			}
		}
		return null;
	}

	private static PropertyInfo FindStaticBool(Type type, string name)
	{
		PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
		return property != null && property.PropertyType == typeof(bool) && property.GetIndexParameters().Length == 0
			? property
			: null;
	}

	private sealed class ReflectionCoopAdapter : ICoopAdapter
	{
		private readonly PropertyInfo _isClient;
		private readonly PropertyInfo _isServer;
		private readonly bool _dedicatedServer;

		internal ReflectionCoopAdapter(Type type, PropertyInfo isClient, PropertyInfo isServer, bool dedicatedServer)
		{
			_isClient = isClient;
			_isServer = isServer;
			_dedicatedServer = dedicatedServer;
			string location;
			try { location = type.Assembly.Location; } catch { location = "<unavailable>"; }
			Description = "reflection/public-properties; assembly=" + type.Assembly.FullName +
				"; location=" + location + "; type=" + type.FullName;
		}

		public bool IsAvailable => true;

		public string Description { get; }

		public bool IsAuthoritative
		{
			get
			{
				try
				{
					if (_isServer != null)
					{
						return (bool)_isServer.GetValue(null, null);
					}
					return !(bool)_isClient.GetValue(null, null);
				}
				catch (Exception ex)
				{
					RuntimeProof.Write("[CoopMarriage] Coop authority property failed: " + ex.GetType().FullName + ": " + ex.Message);
					return _dedicatedServer;
				}
			}
		}
	}

	private sealed class UnavailableCoopAdapter : ICoopAdapter
	{
		private readonly bool _dedicatedServer;

		internal UnavailableCoopAdapter(bool dedicatedServer, string reason)
		{
			_dedicatedServer = dedicatedServer;
			Description = "unavailable; " + reason;
		}

		public bool IsAvailable => false;

		public bool IsAuthoritative => _dedicatedServer;

		public string Description { get; }
	}
}
