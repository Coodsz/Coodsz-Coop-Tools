// Coodsz Image Exporter. Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// src/CoopKeep.ImageExporter/ImageExporterSubModule.cs, translated from Turkish and changed for
// Coodsz Host (output under %AppData%\Coodsz\images, COODSZ_EXPORT_* switches, no banner attempt).
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;
using TaleWorlds.ObjectSystem;

namespace CoodszImageExporter;

/// <summary>
/// Saves the pictures the game draws itself (items, troops, lords) as PNG files once the game
/// data is loaded.
/// </summary>
/// <remarks>
/// <para>
/// The game never writes these pictures to disk; the inventory draws them on the fly. This module
/// uses the same renderer (<see cref="ThumbnailCacheManager"/>) and saves the result:
/// </para>
/// <code>
/// %AppData%\Coodsz\images\item\&lt;item id&gt;.png
/// %AppData%\Coodsz\images\troop\&lt;troop id&gt;.png
/// %AppData%\Coodsz\images\lord\&lt;hero id&gt;.png
/// </code>
/// <para>
/// When it is done it writes <c>export-done.txt</c> and quits the game. While that file is there
/// the module does nothing; Coodsz Host deletes it to draw again.
/// </para>
/// </remarks>
public sealed class ExporterSubModule : MBSubModuleBase
{
    private const int MaxInFlight = 6;

    private readonly Queue<Job> _queue = new();
    private readonly List<Job> _inFlight = new();
    private readonly List<PendingFix> _pendingFix = new();
    private string _root = "";
    private StreamWriter? _log;
    private bool _running;
    private bool _begun;
    private int _total;
    private int _done;
    private int _failed;
    private float _idle;

    /// <summary>
    /// With <c>COODSZ_EXPORT_AUTOSTART=1</c> a plain sandbox game starts from the main menu by
    /// itself; the export begins once the game data is loaded (at character creation), so no
    /// modded save or click is needed.
    /// </summary>
    private bool _autoStart;
    private float _menuTimer = -1;
    private float _beginTimer = -1;

    private sealed class Job
    {
        public string Path { get; set; } = "";
        public Func<Action<Texture>, Action, ThumbnailCreationData> Create { get; set; } = null!;
        public ThumbnailCreationData? Data { get; set; }
        public float Age { get; set; }
        public bool Finished { get; set; }
    }

    private sealed class PendingFix
    {
        public string Path { get; set; } = "";
        public float Age { get; set; }
        public long LastLength { get; set; } = -1;
    }

    protected override void OnBeforeInitialModuleScreenSetAsRoot()
    {
        base.OnBeforeInitialModuleScreenSetAsRoot();
        if (Environment.GetEnvironmentVariable("COODSZ_EXPORT_AUTOSTART") == "1" && !_autoStart)
        {
            _autoStart = true;
            _menuTimer = 4f;
        }
    }

    public override void OnGameLoaded(Game game, object initializerObject)
    {
        base.OnGameLoaded(game, initializerObject);
        if (game.GameType is Campaign) _beginTimer = 3f;
    }

    public override void OnGameInitializationFinished(Game game)
    {
        base.OnGameInitializationFinished(game);
        if (game.GameType is Campaign) _beginTimer = 5f;
    }

    public static string Root()
    {
        var custom = Environment.GetEnvironmentVariable("COODSZ_EXPORT_ROOT");
        return !string.IsNullOrWhiteSpace(custom)
            ? custom!
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coodsz", "images");
    }

    private void Begin()
    {
        if (_begun) return;
        _begun = true;

        _root = Root();
        Directory.CreateDirectory(_root);
        if (File.Exists(System.IO.Path.Combine(_root, "export-done.txt"))) return;

        _log = new StreamWriter(System.IO.Path.Combine(_root, "export.log"), false, new UTF8Encoding(false)) { AutoFlush = true };
        Log("Coodsz Image Exporter started " + DateTime.Now.ToString("O"));

        try
        {
            foreach (var item in MBObjectManager.Instance.GetObjectTypeList<ItemObject>())
            {
                if (item is null || string.IsNullOrWhiteSpace(item.StringId) || item.IsBannerItem) continue;
                var target = Target("item", item.StringId);
                if (File.Exists(target)) continue;
                var captured = item;
                _queue.Enqueue(new Job { Path = target, Create = (set, cancel) => new ItemThumbnailCreationData(captured, "", set, cancel) });
            }

            foreach (var character in MBObjectManager.Instance.GetObjectTypeList<CharacterObject>())
            {
                if (character is null || character.IsTemplate) continue;
                string kind;
                if (character.IsHero && character.HeroObject is { } hero && (hero.IsLord || hero.IsMinorFactionHero || hero.IsWanderer)) kind = "lord";
                else if (!character.IsHero && (character.Occupation == Occupation.Soldier || character.Occupation == Occupation.Bandit
                         || character.Occupation == Occupation.Mercenary || character.Occupation == Occupation.CaravanGuard
                         || character.Occupation == Occupation.Gangster)) kind = "troop";
                else continue;

                var id = kind == "lord" && character.HeroObject is { } h ? h.StringId : character.StringId;
                if (string.IsNullOrWhiteSpace(id)) continue;
                var target = Target(kind, id);
                if (File.Exists(target)) continue;

                var code = CharacterCode.CreateFrom(character);
                _queue.Enqueue(new Job { Path = target, Create = (set, cancel) => new CharacterThumbnailCreationData(code, set, cancel, true, 512, 240) });
            }

            // Banners cannot be saved: the engine keeps banner textures compressed, SaveToFile writes an
            // empty file and GetPixelData crashes the game (CoopKeep tried this on 2026-10-16).
        }
        catch (Exception e)
        {
            Log("! could not list the pictures: " + e);
        }

        _total = _queue.Count;
        _running = _total > 0;
        Log(_total + " pictures to draw");
        Message("Coodsz: drawing " + _total + " pictures...");
        if (!_running) Finish();
    }

    protected override void OnApplicationTick(float dt)
    {
        base.OnApplicationTick(dt);

        if (_menuTimer > 0 && (_menuTimer -= dt) <= 0)
        {
            try { MBGameManager.StartNewGame(new SandBox.SandBoxGameManager(() => new Campaign(CampaignGameMode.Campaign))); }
            catch (Exception e) { File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "coodsz-export-error.txt"), e + Environment.NewLine); }
        }

        if (_beginTimer > 0 && (_beginTimer -= dt) <= 0) Begin();

        if (!_running || ThumbnailCacheManager.Current is null) return;

        foreach (var job in _inFlight.ToList())
        {
            job.Age += dt;
            if (job.Finished) _inFlight.Remove(job);
            else if (job.Age > 20f)
            {
                Log("! timed out: " + job.Path);
                _failed++;
                job.Finished = true;
                _inFlight.Remove(job);
            }
        }

        while (_inFlight.Count < MaxInFlight && _queue.Count > 0)
        {
            var job = _queue.Dequeue();
            try
            {
                job.Data = job.Create(texture => OnTexture(job, texture), () => OnCancel(job));
                ThumbnailCacheManager.Current.CreateTexture(job.Data);
                _inFlight.Add(job);
            }
            catch (Exception e)
            {
                Log("! " + job.Path + ": " + e.Message);
                _failed++;
            }
        }

        foreach (var fix in _pendingFix.ToList())
        {
            fix.Age += dt;
            var length = File.Exists(fix.Path) ? new FileInfo(fix.Path).Length : -1;
            if (length > 0 && length == fix.LastLength && fix.Age > 0.5f)
            {
                if (!PngFix.SwapRedBlue(fix.Path)) Log("~ could not fix the colours: " + fix.Path);
                _pendingFix.Remove(fix);
            }
            else if (fix.Age > 30f)
            {
                Log("! never written to disk: " + fix.Path);
                _pendingFix.Remove(fix);
            }
            else fix.LastLength = length;
        }

        if (_queue.Count == 0 && _inFlight.Count == 0 && _pendingFix.Count == 0)
        {
            _idle += dt;
            if (_idle > 1f) Finish();
        }
    }

    private void OnTexture(Job job, Texture texture)
    {
        if (job.Finished) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(job.Path)!);
            // GetPixelData crashes the engine; only the engine's own saver is used. It writes the file
            // a little later and with red and blue swapped, so the file is fixed once it is on disk.
            texture.SaveToFile(job.Path, false);
            _pendingFix.Add(new PendingFix { Path = job.Path });
            _done++;
        }
        catch (Exception e)
        {
            Log("! could not save " + job.Path + ": " + e.Message);
            _failed++;
        }
        finally
        {
            job.Finished = true;
            try { if (job.Data is not null) ThumbnailCacheManager.Current.DestroyTexture(job.Data); } catch (Exception) { }
        }

        if ((_done + _failed) % 100 == 0) Message("Coodsz: " + (_done + _failed) + "/" + _total);
    }

    private void OnCancel(Job job)
    {
        if (job.Finished) return;
        job.Finished = true;
        _failed++;
        Log("! cancelled: " + job.Path);
    }

    private void Finish()
    {
        _running = false;
        Log("done: " + _done + " saved, " + _failed + " failed");
        File.WriteAllText(System.IO.Path.Combine(_root, "export-done.txt"), DateTime.Now.ToString("O") + "\n" + _done + " ok\n" + _failed + " failed\n");
        Message("Coodsz: saved " + _done + " pictures. The game closes now.");
        _log?.Dispose();
        _log = null;
        if (Environment.GetEnvironmentVariable("COODSZ_EXPORT_STAY") is null) Utilities.QuitGame();
    }

    /// <summary>The file name is the game's own id, so the host finds it whatever language the game runs in.</summary>
    private string Target(string kind, string id)
    {
        var safe = new string(id.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return System.IO.Path.Combine(_root, kind, safe + ".png");
    }

    private void Log(string line) => _log?.WriteLine(line);

    private static void Message(string text)
    {
        try { InformationManager.DisplayMessage(new InformationMessage(text)); }
        catch (Exception) { }
    }
}
