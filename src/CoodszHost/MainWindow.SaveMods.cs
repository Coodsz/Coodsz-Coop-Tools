using System.Windows;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;
using CoodszHost.Services;

namespace CoodszHost;

// The save mods card (Shared/SaveModsCard.xaml) on the Dashboard, under the server buttons: a save that needs
// mods this setup leaves out gets a short title, the mods as chips and one button that fixes it. Every
// button runs the code the host would use by hand: pick the setup, turn the mods on and save the
// setup, Add mods, or the save list.
public partial class MainWindow
{
    private SaveModCard? _saveModCard;

    /// <summary>Called once from the constructor.</summary>
    private void StartSaveModsCard()
    {
        ServerSaveMods.Command += OnSaveModsCommand;
    }

    private void ShowSaveModCard(SaveModCard? card)
    {
        _saveModCard = card;
        ServerSaveMods.Show(card);
    }

    private void HideSaveModCard()
    {
        if (_saveModCard != null)
            ShowSaveModCard(null);
    }

    /// <summary>The card for a refused save, with the best fix this setup allows.</summary>
    private SaveModCard BuildSaveModCard(string saveName, CampaignSaveInfo info, SaveModChange change)
    {
        var profile = _settings.Current();
        var others = _settings.Profiles
            .Where(item => !ReferenceEquals(item, profile))
            .Select(item => new SaveModSetup(item.Name, item.SaveName ?? "", ProfileModules(item)))
            .ToList();
        var canTurnOn = !profile.Locked && change.Removed.All(id => FindModule(id) is { CanToggle: true });
        return SaveModCard.Build(saveName, info.Modules, change,
            id => FindModule(id)?.Name,
            id => FindModule(id) != null,
            others,
            canTurnOn,
            canAddMods: !profile.Locked,
            locked: profile.Locked);
    }

    private ModuleRow? FindModule(string id) =>
        _modules.FirstOrDefault(row => row.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    // The mods a saved setup turns on, by the same rules ApplyProfileModules uses when it is picked.
    private List<string> ProfileModules(HostProfile profile)
    {
        var installedIds = _modules.Select(row => row.Id).ToList();
        return _modules
            .Where(row => row.Required && StableCoop.KeepsRequired(profile, row.Id)
                          || (profile.Locked ? StableCoop.ShouldEnable(profile, row.Id, installedIds) : StableCoop.UnlockedEnables(profile, row.Id)))
            .Select(row => row.Id)
            .ToList();
    }

    private void OnSaveModsCommand(string command)
    {
        var card = _saveModCard;
        if (card == null)
            return;
        switch (command)
        {
            case "fix":
                FixSaveMods(card);
                break;
            case "othersave":
                ShowPage("Server");
                SaveBox.Focus();
                SaveBox.IsDropDownOpen = true;
                break;
            case "dismiss":
                HideSaveModCard();
                break;
        }
    }

    private void FixSaveMods(SaveModCard card)
    {
        // The card stays, with the line that says why nothing happened: the server, a mod test or a restart holds the mods,
        // or another server action (Start, Backup now, Save game) is still under way.
        if (ModListRunning)
        {
            SetControl(ServerHoldsModFiles || CrashRestartWaits || _restartingNow || _restartingAfterCrash ? ModListBlock.RunningLine : ModListBlock.BusyLine);
            return;
        }
        switch (card.Fix)
        {
            case SaveModFix.UseSetup:
                UseSetupForSave(card);
                break;
            case SaveModFix.TurnOn:
                TurnOnSaveMods(card);
                break;
            case SaveModFix.AddMods:
                AddMods_Click(this, new RoutedEventArgs());
                // Every mod is on the server now: turn them on and start, as Turn these mods back on does.
                if (card.Mods.All(chip => FindModule(chip.Id) != null))
                    TurnOnSaveMods(card);
                else
                    ShowSaveModCard(card with { Mods = card.Mods.Select(chip => chip with { Installed = FindModule(chip.Id) != null }).OrderBy(chip => chip.Installed).ToList() });
                break;
        }
    }

    // Use the setup this save was made with: the setup box does the switch, then the save is picked.
    private void UseSetupForSave(SaveModCard card)
    {
        if (!_settings.Profiles.Any(profile => profile.Name == card.SetupName))
        {
            HideSaveModCard();
            return;
        }

        ProfileBox.SelectedItem = card.SetupName;
        if (!Equals(SaveBox.SelectedItem, card.SaveName) && SaveBox.Items.Contains(card.SaveName))
            SaveBox.SelectedItem = card.SaveName;
        _actions.Note("Save mods", "Switched to " + card.SetupName + " for " + card.SaveName);
        HideSaveModCard();

        // Start only the save the card was about, on its setup, and never as a new campaign.
        var current = _settings.Current();
        var block = SaveModCard.UseSetupBlock(card.SaveName, card.SetupName, current.Name, SaveBox.SelectedItem as string, current.NewCampaign);
        if (block != null)
        {
            _actions.Note("Save mods", block);
            SetControl(block, failed: true);
            Append(block);
            ShowPage("Server");
            SaveBox.Focus();
            SaveBox.IsDropDownOpen = true;
            return;
        }

        Append("Switched to the " + card.SetupName + " setup. It has every mod " + card.SaveName + " was made with.");
        Start_Click(this, new RoutedEventArgs());
    }

    // Turn these mods back on: the same as ticking them on the Modules page and Save module config.
    private void TurnOnSaveMods(SaveModCard card)
    {
        var rows = card.Mods.Select(chip => FindModule(chip.Id)).OfType<ModuleRow>().ToList();
        if (_settings.Current().Locked || rows.Count != card.Mods.Count || rows.Any(row => !row.CanToggle))
        {
            HideSaveModCard();
            return;
        }

        foreach (var row in rows)
            row.Enabled = true;
        ModuleList.Items.Refresh();
        try
        {
            PersistProfile();
        }
        catch (InvalidOperationException ex)
        {
            SetControl(ex.Message, failed: true);
            return;
        }

        RefreshModuleSaveHint();
        UpdateModuleSummary();
        _actions.Note("Save mods", "Turned on " + string.Join(", ", rows.Select(row => row.Id)) + " for " + card.SaveName);
        Append("Turned on " + string.Join(", ", rows.Select(row => row.Name)) + " in " + _settings.Current().Name + ", because " + card.SaveName + " was made with them.");
        HideSaveModCard();
        Start_Click(this, new RoutedEventArgs());
    }
}
