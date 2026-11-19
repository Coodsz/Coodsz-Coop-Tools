using System;
using System.IO;

namespace CoopMarriage;

internal static class RuntimeProof
{
	internal static void Write(string message)
	{
		try
		{
            string custom = Environment.GetEnvironmentVariable("BANNERLORD_USER_DIR");
			string text = !string.IsNullOrWhiteSpace(custom) && Path.IsPathRooted(custom)
                ? Path.Combine(custom, "logs")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "Configs");
			Directory.CreateDirectory(text);
			File.AppendAllText(Path.Combine(text, "CoopMarriage.runtime.log"), System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
		}
		catch
		{
		}
	}
}

