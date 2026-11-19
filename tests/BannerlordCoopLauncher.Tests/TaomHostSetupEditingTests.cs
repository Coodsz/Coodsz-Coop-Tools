using BannerlordCoopLauncher.Core.Launch;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The server tool's Modules page: locks are visible, and an unlocked setup is edited with the
// same TaomCoop.Core rules as the client launcher.
public class TaomHostSetupEditingTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }

    private static TaomSetup Mixed()
    {
        var book = new TaomSetupBook();
        var setup = book.Create("Mine");
        Assert.Null(TaomLoadOrder.Add(setup, "Early", "EarlyMod", null));
        Assert.Null(TaomLoadOrder.Add(setup, "Wargs", "Alliance.Wargs", null));
        Assert.Null(TaomLoadOrder.Add(setup, "Late", "LateMod", null));
        Assert.Null(TaomLoadOrder.Add(setup, "Sleeping", "OffMod", null));
        TaomLoadOrder.MoveTo(setup, "EarlyMod", ExtraSlot.BeforeTaom, 0);
        TaomLoadOrder.MoveTo(setup, "Alliance.Wargs", ExtraSlot.BeforeCoop, 0);
        TaomLoadOrder.SetEnabled(setup, "OffMod", false);
        return setup;
    }

    [Fact]
    public void The_rows_follow_the_server_order_and_keep_mods_that_are_off()
    {
        var setup = Mixed();
        var rows = TaomLoadOrder.ServerRows(setup);

        // The rows of mods that are on are exactly the order the server loads.
        var loaded = rows.Where(row => row.IsCore || setup.Extras[row.ExtraIndex].Enabled).Select(row => row.ModuleId);
        Assert.Equal(TaomLoadOrder.ServerOrder(setup), loaded);

        // A mod that is off still has its row, so the host can turn it back on.
        var off = Assert.Single(rows, row => row.ModuleId == "OffMod");
        Assert.Equal("OffMod", setup.Extras[off.ExtraIndex].ModuleId);
        Assert.True(rows.ToList().FindIndex(row => row.ModuleId == "OffMod") > rows.ToList().FindIndex(row => row.ModuleId == TaomStack.CompatModuleId));

        // The core rows are every server module, in the sealed order.
        Assert.Equal(TaomStack.ServerModules, rows.Where(row => row.IsCore).Select(row => row.ModuleId));
        Assert.All(rows.Where(row => !row.IsCore), row => Assert.Equal(row.ModuleId, setup.Extras[row.ExtraIndex].ModuleId));
        Assert.Equal("DedicatedServer.Windows", rows[^1].ModuleId);
    }

    [Fact]
    public void A_mod_saved_before_coop_by_mistake_shows_where_it_loads()
    {
        var setup = new TaomSetup
        {
            Name = "Hand edited",
            Extras = { new TaomExtraMod { Name = "X", ModuleId = "XMod", Slot = ExtraSlot.BeforeCoop } }
        };
        var ids = TaomLoadOrder.ServerRows(setup).Select(row => row.ModuleId).ToList();
        Assert.True(ids.IndexOf("XMod") > ids.IndexOf(TaomStack.CompatModuleId));
        Assert.Equal(TaomLoadOrder.ServerOrder(setup), ids);
    }

    [Fact]
    public void Locked_setups_say_so_and_a_copy_can_be_changed()
    {
        Assert.Equal("TAOM plus is locked. Copy it to change mods or their order.", TaomSetupBook.LockedNote(TaomSetupBook.PlusName));
        Assert.Equal("Part of the TAOM core. It always loads in this order.", TaomSetupBook.CoreRowTip);

        var book = new TaomSetupBook();
        book.Ensure();
        var plus = book.Find(TaomSetupBook.PlusName)!;
        Assert.Throws<InvalidOperationException>(() => TaomLoadOrder.Move(plus, plus.Extras[0].ModuleId, 1));
        Assert.Throws<InvalidOperationException>(() => book.Rename(TaomSetupBook.PlusName, "Other"));

        var copy = book.CopyOf(TaomSetupBook.PlusName);
        Assert.False(copy.Locked);
        Assert.Equal(plus.Extras.Select(mod => mod.ModuleId), copy.Extras.Select(mod => mod.ModuleId));
        Assert.Same(copy, book.Current());
        Assert.True(TaomLoadOrder.Move(copy, copy.Extras[^1].ModuleId, -1) || copy.Extras.Count < 2);
    }

    [Fact]
    public void The_working_copy_shows_unsaved_changes_until_save_mods()
    {
        var book = new TaomSetupBook();
        var saved = book.Create("Mine");
        TaomLoadOrder.Add(saved, "A", "AMod", null);
        TaomLoadOrder.Add(saved, "B", "BMod", null);
        var editing = saved.Clone();
        Assert.True(editing.SameMods(saved));

        TaomLoadOrder.Move(editing, "BMod", -1);
        Assert.False(editing.SameMods(saved));
        Assert.Equal(new[] { "AMod", "BMod" }, saved.Extras.Select(mod => mod.ModuleId));

        // Save mods: the same steps as the client launcher.
        saved.Extras = editing.Extras.Select(mod => mod.Copy()).ToList();
        TaomLoadOrder.Normalize(saved);
        Assert.Equal(new[] { "BMod", "AMod" }, TaomLoadOrder.ServerOrder(saved).Where(id => id.EndsWith("Mod")));
    }

    [Fact]
    public void The_modules_page_shows_the_lock_and_the_edit_buttons()
    {
        var root = RepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "TaomCoopHost", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"LockedSetupTag\"", xaml);
        Assert.Contains("Text=\"🔒\"", xaml);
        Assert.Contains("Text=\"Locked\"", xaml);
        Assert.Contains("x:Name=\"LockedSetupText\"", xaml);
        Assert.Contains("Players need the same mods in the same order. Click Share mod list and send it to them.", xaml);

        // Since redesign wave 6 the setup is picked in the header (ModSetupBox); New setup, Copy, Rename and Delete sit at the
        // top of the Mods page, before Add mods and Save mods. Delete stays far right.
        var top = xaml.IndexOf("Click=\"RenameHostSetup_Click\"", StringComparison.Ordinal);
        Assert.True(top >= 0);
        Assert.True(top < xaml.IndexOf("Click=\"CopyHostSetup_Click\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"ModSetupBox\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"ModulesPage\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"ModulesPage\"", StringComparison.Ordinal) < xaml.IndexOf("Click=\"NewHostSetup_Click\"", StringComparison.Ordinal));
        var delete = xaml.IndexOf("Click=\"DeleteModSetup_Click\"", StringComparison.Ordinal);
        Assert.Contains("BarDanger", xaml[(delete - 200)..delete]);
        Assert.True(delete < xaml.IndexOf("Click=\"AddHostMods_Click\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Click=\"AddHostMods_Click\"", StringComparison.Ordinal) < xaml.IndexOf("Click=\"SaveHostMods_Click\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Click=\"SaveHostMods_Click\"", StringComparison.Ordinal) < xaml.IndexOf("Click=\"InstallHostMods_Click\"", StringComparison.Ordinal));
        foreach (var handler in new[] { "MoveHostModUp_Click", "MoveHostModDown_Click", "RemoveHostMod_Click", "ToggleHostMod_Click" })
            Assert.Contains("Click=\"" + handler + "\"", xaml);
        Assert.Contains("Visibility=\"{Binding EditVisibility}\"", xaml);
        Assert.Contains("ToolTip=\"{Binding RowTip}\"", xaml);
        Assert.Contains("CustomModWindow.cs", File.ReadAllText(Path.Combine(root, "src", "TaomCoopHost", "TaomCoopHost.csproj")));
    }

    [Fact]
    public void The_server_tool_edits_through_the_client_rules()
    {
        var host = SourceFiles.Window(RepoRoot(), "TaomCoopHost");
        var show = SourceFiles.Method(host, "private void ShowModSetups(");
        Assert.Contains("TaomLoadOrder.ServerRows(shown)", show);
        Assert.Contains("Tip = TaomSetupBook.CoreRowTip", show);
        Assert.Contains("Editable = !locked", show);
        Assert.Contains("TaomSetupBook.LockedNote(current.Name)", show);

        var add = SourceFiles.Method(host, "private async void AddHostMods_Click(");
        Assert.Contains("BannerlordCoopLauncher.CustomModWindow.Choose(", add);
        Assert.Contains("ConfirmBrokenHostMod(", add);
        Assert.Contains("TaomLoadOrder.Add(editing", add);
        Assert.Contains("TaomLoadOrder.Move(EditingSetup", SourceFiles.Method(host, "private void MoveHostMod("));
        Assert.Contains("TaomLoadOrder.Remove(editing", SourceFiles.Method(host, "private void RemoveHostMod_Click("));
        Assert.Contains("TaomLoadOrder.SetEnabled(EditingSetup", SourceFiles.Method(host, "private void ToggleHostMod_Click("));
        Assert.Contains("TaomAutoSort.Sort(setup", SourceFiles.Method(host, "private void AutoSortHost_Click("));

        var save = SourceFiles.Method(host, "private void SaveHostMods()");
        Assert.Contains("TaomLoadOrder.Normalize(saved)", save);
        Assert.Contains("if (saved.Locked)", save);

        // Unsaved changes are saved or dropped before another setup, a start or closing.
        Assert.Contains("ConfirmHostModChanges()", SourceFiles.Method(host, "private void ModSetupBox_SelectionChanged("));
        Assert.Contains("ConfirmHostModChanges()", SourceFiles.Method(host, "private async void Start_Click("));
        Assert.Contains("ConfirmHostModChanges()", SourceFiles.Method(host, "private void HostModSetups_Closing("));
        Assert.Contains("ConfirmHostModChanges()", SourceFiles.Method(host, "private void NewHostSetup_Click("));
        Assert.Contains("ConfirmHostModChanges()", SourceFiles.Method(host, "private void CopyHostSetup_Click("));
        Assert.Contains("_settings.ModSetups.Rename(", SourceFiles.Method(host, "private void SaveHostSetupName()"));
    }
}
