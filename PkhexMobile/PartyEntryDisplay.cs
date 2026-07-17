using PKHeX.Core;

namespace PkhexMobile;

public sealed record PartyEntryDisplay(int Slot, string SpeciesName, string Nickname, int Level, PKM Source);

public sealed record StatRow(string Label, string Value);
