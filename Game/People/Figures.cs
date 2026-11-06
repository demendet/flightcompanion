namespace FlightCompanion.Game.People;

/// <summary>
/// Which standing figure represents a person at the strip. The sim ships people as placeable objects named
/// Tarmac_Gender_Season_Look. The titles here are ones the user's own scenery already places, so they are known to exist.
/// Installed passenger figures are preferred when present, because the sim's own people are dressed as ramp workers.
/// </summary>
public static class Figures
{
    private static readonly string[] Winter = ["Caucasian"];
    private static readonly string[] Summer = ["Hispanic", "Caucasian"];

    public static bool IsWinter(int month) => month is >= 10 or <= 4;

    public static string Pick(string gender, int month, IReadOnlyList<string> installedPassengers, IReadOnlyCollection<string> simList, Random rng)
    {
        bool female = gender.Equals("female", StringComparison.OrdinalIgnoreCase);
        // Installed figures are named like ENGM_passenger_F01_W: F or M right after the last separator before the number.
        var matching = installedPassengers.Where(t => IsGender(t, female)).ToList();
        if (matching.Count > 0) return matching[rng.Next(matching.Count)];
        if (installedPassengers.Count > 0) return installedPassengers[rng.Next(installedPassengers.Count)];

        string g = female ? "Female" : "Male";
        bool winter = IsWinter(month);
        var looks = winter ? Winter : Summer;
        string title = $"Tarmac_{g}_{(winter ? "Winter" : "Summer")}_{looks[rng.Next(looks.Length)]}";

        // When the sim's own list is known and names people, trust it over the built-in guess.
        var listed = simList.Where(t => t.StartsWith($"Tarmac_{g}_{(winter ? "Winter" : "Summer")}_", StringComparison.OrdinalIgnoreCase)).ToList();
        return listed.Count > 0 ? listed[rng.Next(listed.Count)] : title;
    }

    private static bool IsGender(string title, bool female)
    {
        string t = title.ToUpperInvariant();
        int at = t.LastIndexOf("PASSENGER_", StringComparison.Ordinal);
        if (at < 0 || at + 10 >= t.Length) return false;
        return t[at + 10] == (female ? 'F' : 'M');
    }
}
