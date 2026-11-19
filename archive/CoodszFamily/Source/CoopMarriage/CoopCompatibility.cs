using CoopMarriage.Compatibility;
using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace CoopMarriage;

internal static class CoopCompatibility
{
	private static readonly object Sync = new object();
	private static ICoopAdapter _adapter;
	private static bool _dedicatedServer;
	private static string _loggedDescription;
	private static MethodInfo _isPlayerHero;

	internal static bool IsPlayerHero(Hero hero)
	{
		if (hero == null) return false;
		if (_isPlayerHero == null)
		{
			var type = AccessTools.TypeByName("GameInterface.Services.Heroes.Extensions.HeroExtensions");
			var method = type?.GetMethod("IsPlayerHero", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Hero) }, null);
			if (method?.ReturnType == typeof(bool)) _isPlayerHero = method;
		}
		try { return _isPlayerHero != null && (bool)_isPlayerHero.Invoke(null, new object[] { hero }); }
		catch (Exception ex)
		{
			RuntimeProof.Write("Player identity unavailable: " + ex.GetBaseException().Message);
			return false;
		}
	}

	internal static void Initialize(bool dedicatedServer)
	{
		lock (Sync)
		{
			_dedicatedServer = dedicatedServer;
			_adapter = null;
			_loggedDescription = null;
			_isPlayerHero = null;
		}
		RuntimeProof.Write("[CoopMarriage] Running environment: " + (dedicatedServer ? "DedicatedServer" : "Client"));
	}

	internal static bool IsAuthoritative
	{
		get
		{
			ICoopAdapter adapter;
			lock (Sync)
			{
				if (_adapter == null || !_adapter.IsAvailable)
				{
					_adapter = CoopAdapterFactory.Create(_dedicatedServer);
				}
				adapter = _adapter;
				if (_loggedDescription != adapter.Description)
				{
					RuntimeProof.Write("[CoopMarriage] Coop compatibility adapter: " + adapter.Description);
					_loggedDescription = adapter.Description;
				}
			}
			return adapter.IsAuthoritative;
		}
	}

	internal static bool IsAvailable
	{
		get
		{
			lock (Sync)
			{
				if (_adapter == null || !_adapter.IsAvailable)
				{
					_adapter = CoopAdapterFactory.Create(_dedicatedServer);
				}
				return _adapter.IsAvailable;
			}
		}
	}
}
