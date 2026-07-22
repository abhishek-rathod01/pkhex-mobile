using CommunityToolkit.Maui.Storage;
using PKHeX.Core;

namespace PkhexMobile;

/// <summary>
/// Save-level (trainer) editor: OT name / IDs / gender / language, money and play time, plus a
/// read-only Pokedex completion card.
///
/// Every editable control here is gated on <see cref="TrainerFieldSupport"/>, an empirical
/// Write()+reload probe of the loaded save - not on <see cref="SaveFile.Generation"/>. See that
/// file for why: these members are all virtual with inherited do-nothing defaults, and at least one
/// save type reachable from the file picker (SAV3RSBox, the GameCube box dump) overrides none of
/// them while still reporting Generation == 3.
/// </summary>
public partial class TrainerInfoPage : ContentPage
{
    SaveFile? currentSave;
    TrainerFieldSupport? support;

    // Bounds resolved from the loaded save once the probe is done; used by the live clamp handlers.
    uint maxMoney = 9999999;
    uint maxTid = ushort.MaxValue;
    uint maxSid = ushort.MaxValue;
    int maxHours = TrainerFieldSupport.DefaultHoursCeiling;

    // Picker index -> value maps, rebuilt per save.
    readonly List<byte> genderValues = [];
    readonly List<int> languageValues = [];

    // Dirty/clean Save button tracking, same contract as PokemonDetailPage: disabled while clean,
    // enabled on the first real edit, disabled again immediately after a successful save.
    // isLoading suppresses the TextChanged/SelectedIndexChanged noise that populating the form
    // fires, so a fresh load never looks like a user edit.
    bool isLoading;
    bool isDirty;

    public TrainerInfoPage()
    {
        InitializeComponent();

        // Numeric entries carry a live clamp as well as MarkDirty. The clamp is not belt-and-braces:
        // Money is clamped by some generations' setters and not others (verified - Gen1/2/7/8 clamp
        // to MaxMoney, Gen3/4/5/9 store whatever they are given), so without a UI clamp the same
        // typed number would behave differently per save. Minutes and seconds are never clamped by
        // the library at all.
        MoneyEntry.TextChanged += (_, _) => { ClampEntry(MoneyEntry, maxMoney); MarkDirty(); };
        TidEntry.TextChanged += (_, _) => { ClampEntry(TidEntry, maxTid); MarkDirty(); };
        SidEntry.TextChanged += (_, _) => { ClampEntry(SidEntry, maxSid); MarkDirty(); };
        HoursEntry.TextChanged += (_, _) => { ClampEntry(HoursEntry, (uint)maxHours); MarkDirty(); };
        MinutesEntry.TextChanged += (_, _) => { ClampEntry(MinutesEntry, 59); MarkDirty(); };
        SecondsEntry.TextChanged += (_, _) => { ClampEntry(SecondsEntry, 59); MarkDirty(); };

        OtEntry.TextChanged += (_, _) => MarkDirty();
        GenderPicker.SelectedIndexChanged += (_, _) => MarkDirty();
        LanguagePicker.SelectedIndexChanged += (_, _) => MarkDirty();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (NavigationState.PendingSave is { } sav)
        {
            currentSave = sav;
            NavigationState.PendingSave = null;
            support = null;
        }

        if (currentSave is null || support is not null)
            return;

        var target = currentSave;
        SaveSummaryLabel.Text = $"{target.GetType().Name} · {target.Version}";

        // The probe serializes the whole save and re-parses it (a few MB on Gen9), so it is kept off
        // the UI thread. The form stays hidden until the verdict is in - showing enabled controls
        // first and disabling them a beat later would let a user start typing into a field that is
        // about to turn out to be a no-op.
        TrainerFieldSupport probed;
        try
        {
            probed = await Task.Run(() => TrainerFieldSupport.Probe(target));
        }
        catch (Exception ex)
        {
            ProbeStatusLabel.Text = $"Could not check this save's trainer fields: {ex.Message}";
            return;
        }

        if (!ReferenceEquals(currentSave, target))
            return; // a different save arrived while the probe was running

        support = probed;
        ApplySupport(target, probed);
    }

    private void ApplySupport(SaveFile sav, TrainerFieldSupport s)
    {
        ProbeStatusLabel.IsVisible = false;

        if (!s.AnySupported)
        {
            // No trainer block in this file at all. Showing the form here would display PKHeX.Core's
            // inherited defaults (OT "PKHeX", every number 0) as if they were the trainer's data.
            NotStoredLabel.Text = s.ProbeError is null
                ? TrainerFieldSupport.NotStoredExplanation(sav)
                : $"{TrainerFieldSupport.NotStoredExplanation(sav)}\n\nProbe detail: {s.ProbeError}";
            NotStoredBanner.IsVisible = true;
            PopulateDex(sav);
            return;
        }

        maxMoney = (uint)sav.MaxMoney;
        maxTid = TrainerFieldSupport.MaxDisplayTID(sav);
        maxSid = TrainerFieldSupport.MaxDisplaySID(sav);
        maxHours = s.HoursCeiling;

        isLoading = true;
        try
        {
            PopulateIdentity(sav, s);
            PopulateMoneyAndPlayTime(sav, s);
            PopulateDex(sav);

            IdentityCard.IsVisible = true;
            MoneyCard.IsVisible = true;
            SaveChangesBtn.IsVisible = true;
        }
        finally
        {
            isLoading = false;
            isDirty = false;
            UpdateSaveButtonState();
        }
    }

    private void PopulateIdentity(SaveFile sav, TrainerFieldSupport s)
    {
        // MaxLength, not a save-time truncation: on Gen2 an over-length name throws
        // IndexOutOfRangeException out of the OT setter itself (verified), so the only safe place to
        // stop it is before the character is accepted.
        OtEntry.MaxLength = sav.MaxStringLengthTrainer;
        OtEntry.Text = sav.OT;
        SetFieldState(OtEntry, OtNoteLabel, s[TrainerField.OT],
            enabledNote: $"Up to {sav.MaxStringLengthTrainer} characters. Characters this game cannot store are rejected on save.",
            disabledNote: "This save does not store an OT name.");

        // Six-digit (Gen7+) vs five-digit display is the save's own TrainerIDDisplayFormat decision,
        // not a generation guess - the two formats decompose ID32 differently.
        var sixDigit = sav.TrainerIDDisplayFormat == TrainerIDFormat.SixDigit;
        TidCaptionLabel.Text = sixDigit ? "Trainer ID (6-digit)" : "Trainer ID (5-digit)";
        SidCaptionLabel.Text = sixDigit ? "Secret ID (Gen 7+ format)" : "Secret ID";

        TidEntry.Text = sav.DisplayTID.ToString();
        SetFieldState(TidEntry, TidNoteLabel, s[TrainerField.DisplayTID],
            enabledNote: $"0 - {maxTid}.",
            disabledNote: "This save does not store a Trainer ID.");

        SidEntry.Text = sav.DisplaySID.ToString();
        SetFieldState(SidEntry, SidNoteLabel, s[TrainerField.DisplaySID],
            // The six-digit ceiling really is 4294, not 999999: ID32 is rebuilt as
            // (sid * 1_000_000) + tid and must fit a uint.
            enabledNote: $"0 - {maxSid}.",
            disabledNote: "This save has no Secret ID; the value shown is a fixed 0.");

        genderValues.Clear();
        genderValues.AddRange([0, 1]);
        GenderPicker.ItemsSource = new List<string> { "Male", "Female" };
        GenderPicker.SelectedIndex = sav.Gender == 1 ? 1 : 0;
        SetFieldState(GenderPicker, GenderNoteLabel, s[TrainerField.Gender],
            enabledNote: null,
            disabledNote: "This save does not store a trainer gender; the value shown is PKHeX.Core's fallback.");

        PopulateLanguage(sav, s);
    }

    private void PopulateLanguage(SaveFile sav, TrainerFieldSupport s)
    {
        languageValues.Clear();
        var names = new List<string>();

        // Per-generation language table straight from PKHeX.Core rather than a hardcoded list -
        // Korean is absent before Gen2, Chinese before Gen7, and so on.
        foreach (var id in Language.GetAvailableGameLanguages(sav.Context))
        {
            languageValues.Add(id);
            names.Add(DescribeLanguage(id));
        }

        // The current value can sit outside that table: Gen1-3 saves commonly report 0 (None) and
        // SAV3RSBox reports -1. Surface it rather than silently snapping the picker to a different
        // language than the file claims.
        var current = sav.Language;
        if (!languageValues.Contains(current))
        {
            languageValues.Insert(0, current);
            names.Insert(0, current <= 0 ? "Not set" : $"Unknown ({current})");
        }

        LanguagePicker.ItemsSource = names;
        LanguagePicker.SelectedIndex = languageValues.IndexOf(current);
        SetFieldState(LanguagePicker, LanguageNoteLabel, s[TrainerField.Language],
            enabledNote: null,
            // The Gen1/2/3 case, and the sharpest instance of this project's recurring bug class:
            // SAV1/SAV2/SAV3 all declare `public override int Language { get; set; }`, a plain
            // auto-property filled in during save detection and never written to Data[]. The
            // in-memory getter happily returns whatever you set - it just never reaches the file.
            disabledNote: "This save does not store a trainer language. PKHeX.Core infers it while reading the file, " +
                          "and the value here would be discarded on export, so it is shown read-only.");
    }

    private void PopulateMoneyAndPlayTime(SaveFile sav, TrainerFieldSupport s)
    {
        MoneyCaptionLabel.Text = "Money";
        MoneyEntry.Text = sav.Money.ToString();
        SetFieldState(MoneyEntry, MoneyNoteLabel, s[TrainerField.Money],
            enabledNote: $"0 - {maxMoney}.",
            disabledNote: "This save does not store money.");

        HoursEntry.Text = sav.PlayedHours.ToString();
        MinutesEntry.Text = sav.PlayedMinutes.ToString();
        SecondsEntry.Text = sav.PlayedSeconds.ToString();

        var playTimeEditable = s[TrainerField.PlayedHours] || s[TrainerField.PlayedMinutes] || s[TrainerField.PlayedSeconds];
        HoursEntry.IsEnabled = s[TrainerField.PlayedHours];
        MinutesEntry.IsEnabled = s[TrainerField.PlayedMinutes];
        SecondsEntry.IsEnabled = s[TrainerField.PlayedSeconds];

        PlayTimeNoteLabel.IsVisible = true;
        PlayTimeNoteLabel.Text = playTimeEditable
            // maxHours comes from the probe, not a constant: SAV1 keeps hours in one byte and its
            // setter pins anything >= 255 to 255 while flagging "played maximum".
            ? $"Hours 0 - {maxHours}, minutes and seconds 0 - 59."
            : "This save does not store play time.";
    }

    private void PopulateDex(SaveFile sav)
    {
        // Gated on the save's own HasPokeDex, which is false for storage-only dumps like SAV3RSBox.
        if (!sav.HasPokeDex)
        {
            DexCard.IsVisible = false;
            return;
        }

        var seen = sav.SeenCount;
        var caught = sav.CaughtCount;
        var total = sav.MaxSpeciesID;

        DexSeenLabel.Text = $"{seen} / {total}";
        DexCaughtLabel.Text = $"{caught} / {total}";
        DexPercentLabel.Text = $"{sav.PercentCaught:P1}";
        DexProgress.Progress = total > 0 ? Math.Clamp((double)caught / total, 0, 1) : 0;
        DexCard.IsVisible = true;
    }

    /// <summary>
    /// The project's established treatment for a field a given save cannot actually persist: leave
    /// the control visible and still showing the real current value, disable it, and say why inline
    /// (precedent: the Gen1/2 IV fields and the Form/Nature/Ability pickers on PokemonDetailPage).
    /// </summary>
    private static void SetFieldState(View control, Label note, bool enabled, string? enabledNote, string disabledNote)
    {
        control.IsEnabled = enabled;
        var text = enabled ? enabledNote : disabledNote;
        note.Text = text ?? string.Empty;
        note.IsVisible = !string.IsNullOrEmpty(text);
    }

    private static string DescribeLanguage(byte id) => (LanguageID)id switch
    {
        LanguageID.Japanese => "Japanese",
        LanguageID.English => "English",
        LanguageID.French => "French",
        LanguageID.Italian => "Italian",
        LanguageID.German => "German",
        LanguageID.Spanish => "Spanish",
        LanguageID.Korean => "Korean",
        LanguageID.ChineseS => "Chinese (Simplified)",
        LanguageID.ChineseT => "Chinese (Traditional)",
        var other => other.ToString(),
    };

    private static void ClampEntry(Entry entry, uint max)
    {
        if (!uint.TryParse(entry.Text, out var value) || value <= max)
            return;
        entry.Text = max.ToString();
        entry.CursorPosition = entry.Text.Length;
    }

    private void MarkDirty()
    {
        if (isLoading)
            return;
        isDirty = true;
        UpdateSaveButtonState();
    }

    private void UpdateSaveButtonState() => SaveChangesBtn.IsEnabled = isDirty && currentSave is not null;

    private async void OnSaveChangesClicked(object? sender, EventArgs e)
    {
        if (currentSave is null || support is null)
        {
            SaveStatusLabel.Text = "No save loaded - can't export.";
            return;
        }

        var sav = currentSave;
        var s = support;

        try
        {
            // Parse and validate everything BEFORE mutating the save, so a rejected field can't
            // leave the in-memory save half-edited. Each local defaults to the save's current value
            // so an unsupported (and therefore never-parsed) field is a no-op rather than a zero.
            uint tid = sav.DisplayTID, sid = sav.DisplaySID, money = sav.Money;
            uint hours = (uint)sav.PlayedHours, minutes = (uint)sav.PlayedMinutes, seconds = (uint)sav.PlayedSeconds;

            if (s[TrainerField.DisplayTID] && !TryParseBounded(TidEntry.Text, maxTid, out tid))
            {
                SaveStatusLabel.Text = $"Trainer ID must be a number from 0 to {maxTid}.";
                return;
            }
            if (s[TrainerField.DisplaySID] && !TryParseBounded(SidEntry.Text, maxSid, out sid))
            {
                SaveStatusLabel.Text = $"Secret ID must be a number from 0 to {maxSid}.";
                return;
            }
            if (s[TrainerField.Money] && !TryParseBounded(MoneyEntry.Text, maxMoney, out money))
            {
                SaveStatusLabel.Text = $"Money must be a number from 0 to {maxMoney}.";
                return;
            }
            if (s[TrainerField.PlayedHours] && !TryParseBounded(HoursEntry.Text, (uint)maxHours, out hours))
            {
                SaveStatusLabel.Text = $"Hours must be a number from 0 to {maxHours}.";
                return;
            }
            if (s[TrainerField.PlayedMinutes] && !TryParseBounded(MinutesEntry.Text, 59, out minutes))
            {
                SaveStatusLabel.Text = "Minutes must be a number from 0 to 59.";
                return;
            }
            if (s[TrainerField.PlayedSeconds] && !TryParseBounded(SecondsEntry.Text, 59, out seconds))
            {
                SaveStatusLabel.Text = "Seconds must be a number from 0 to 59.";
                return;
            }

            // The six-digit format rebuilds ID32 as (sid * 1_000_000) + tid, and SetTrainerID7's own
            // comment notes a bad pair "overflow[s] back to sid:0" instead of failing - so the pair
            // is checked with PKHeX.Core's own predicate rather than trusting the per-field bounds.
            if (sav.TrainerIDDisplayFormat == TrainerIDFormat.SixDigit &&
                s[TrainerField.DisplayTID] && s[TrainerField.DisplaySID] &&
                !sav.IsValidTrainerID7(sid, tid))
            {
                SaveStatusLabel.Text = $"Trainer ID {tid} and Secret ID {sid} can't be combined - that pair overflows this game's 32-bit ID.";
                return;
            }

            if (s[TrainerField.OT])
            {
                var name = OtEntry.Text ?? string.Empty;
                if (name.Length > sav.MaxStringLengthTrainer)
                    name = name[..sav.MaxStringLengthTrainer];

                // A blank OT round-trips cleanly (empty in, empty out), so the charset check below
                // would wave it through - but every generation's trainer name is non-empty in a real
                // save, and clearing the field is far more likely to be a slip than an intent.
                if (string.IsNullOrWhiteSpace(name))
                {
                    SaveStatusLabel.Text = "OT name can't be blank.";
                    return;
                }

                // Charset check by round-trip through PKHeX.Core's own converter, rather than a
                // hardcoded per-generation allowed-character list. Gen1/2 use a bespoke single-byte
                // table where most non-ASCII input has no representation at all (verified: it comes
                // back as an empty string), and Gen3 silently drops part of it. Setting then reading
                // back catches all of that generically. Restored on mismatch so a rejected save
                // leaves the in-memory trainer name untouched.
                var previous = sav.OT;
                sav.OT = name;
                var readBack = sav.OT;
                if (readBack != name)
                {
                    sav.OT = previous;
                    SaveStatusLabel.Text = $"\"{name}\" can't be stored by this game's character set (it would become \"{readBack}\"). Try plain letters and numbers.";
                    return;
                }
            }

            if (s[TrainerField.DisplayTID])
                sav.DisplayTID = tid;
            if (s[TrainerField.DisplaySID])
                sav.DisplaySID = sid;
            if (s[TrainerField.Gender] && GenderPicker.SelectedIndex >= 0 && GenderPicker.SelectedIndex < genderValues.Count)
                sav.Gender = genderValues[GenderPicker.SelectedIndex];
            if (s[TrainerField.Language] && LanguagePicker.SelectedIndex >= 0 && LanguagePicker.SelectedIndex < languageValues.Count)
                sav.Language = languageValues[LanguagePicker.SelectedIndex];
            if (s[TrainerField.Money])
                sav.Money = money;

            // Hours first, then minutes and seconds: SAV1's hours setter zeroes minutes and seconds
            // when it hits its 255 ceiling, so writing them afterwards keeps what the user actually
            // typed instead of silently blanking two fields.
            if (s[TrainerField.PlayedHours])
                sav.PlayedHours = (int)hours;
            if (s[TrainerField.PlayedMinutes])
                sav.PlayedMinutes = (int)minutes;
            if (s[TrainerField.PlayedSeconds])
                sav.PlayedSeconds = (int)seconds;

            var bytes = sav.Write().ToArray();
            using var stream = new MemoryStream(bytes);
            var fileName = $"trainer_{DateTime.Now:yyyyMMdd_HHmmss}.sav";
            var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);

            if (result.IsSuccessful)
            {
                SaveStatusLabel.Text = $"Saved to: {result.FilePath}";
                SaveSummaryLabel.Text = $"{sav.GetType().Name} · {sav.Version}";
                PopulateDex(sav);

                isDirty = false;
                UpdateSaveButtonState();
            }
            else
            {
                SaveStatusLabel.Text = result.IsCancelled
                    ? "Save cancelled."
                    : $"Save failed: {result.Exception?.Message}";
            }
        }
        catch (Exception ex)
        {
            SaveStatusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private static bool TryParseBounded(string? text, uint max, out uint value) =>
        uint.TryParse(text, out value) && value <= max;
}
