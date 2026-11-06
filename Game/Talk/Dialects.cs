using System.Text.RegularExpressions;

namespace FlightCompanion.Game.Talk;

public enum Dialect { Unknown, Oslo, Northern, Bergen, Trondersk }

/// <summary>
/// The voice decides how a person talks, because a northern voice reading Oslo words, or a Bergen voice reading Finnmark
/// words, sounds wrong however good the voice is. The words are written the way people say them, not in Nynorsk.
/// </summary>
public static class Dialects
{
    public static Dialect FromAccent(string? accent)
    {
        string a = (accent ?? "").ToLowerInvariant();
        if (a.Contains("north") || a.Contains("nord") || a.Contains("tromsø") || a.Contains("finnmark")) return Dialect.Northern;
        if (a.Contains("bergen") || a.Contains("vest")) return Dialect.Bergen;
        if (a.Contains("trønd") || a.Contains("trond")) return Dialect.Trondersk;
        if (a.Contains("oslo") || a.Contains("standard") || a.Contains("øst")) return Dialect.Oslo;
        return Dialect.Unknown;
    }

    public static string Region(Dialect d) => d switch
    {
        Dialect.Northern => "Northern Norway",
        Dialect.Bergen => "Bergen",
        Dialect.Trondersk => "Trøndelag",
        Dialect.Oslo => "the Oslo area",
        _ => "",
    };

    /// <summary>How to write speech so this voice sounds like itself. Empty when the voice has no Norwegian accent to match.</summary>
    public static string Instructions(Dialect d) => d switch
    {
        Dialect.Northern => """
            YOUR DIALECT IS NORTHERN NORWEGIAN. Write every line the way it is said up north, never textbook Norwegian and never Nynorsk.
            Use: æ (jeg), mæ, dæ, ka (hva), kor (hvor), kem (hvem), korsn (hvordan), koffer (hvorfor), e (er), no (nå), dokker (dere), te (til), ikke, sjøl, heile, ska, vil, hæ.
            Drop the ending on verbs: å fly, å ta, å kjør, å vent.
            Sound like this: "Æ e på flyplassen no, kor lenge e du unna?" "Ka koste det, da?" "Korsn ska vi få med oss det der?" "Det e sånn det e."
            Swearing is ordinary up here: faen, helvete, satan, when it suits the mood.
            """,
        Dialect.Bergen => """
            YOUR DIALECT IS BERGEN. Write every line the way it is said in Bergen. Do not write Nynorsk, write Bergen speech.
            Use: eg (jeg), meg, deg, ka (hva), kor (hvor), kem (hvem), korleis (hvordan), koffor (hvorfor), e (er), no (nå), ikkje, dokker (dere), te (til), sånn, ikkje sant, vel.
            Use only en and ei is never used: "en jente", "en bil".
            Sound like this: "Eg e på flyplassen no, kor lenge e du unna?" "Ka koste det, då?" "Det går vel fint, ikkje sant?"
            """,
        Dialect.Trondersk => """
            YOUR DIALECT IS TRØNDERSK. Write every line the way it is said in Trøndelag, never textbook Norwegian and never Nynorsk.
            Use: æ (jeg), mæ, dæ, ka (hva), kor (hvor), kem (hvem), korsn (hvordan), kafor (hvorfor), e (er), no (nå), itj (ikke), dokker (dere), te (til), sjøl.
            Drop the ending on verbs: å kjør, å ta, å vent.
            Sound like this: "Æ e på flyplassen no, kor lenge e du unna?" "Ka koste det, da?" "Det går itj, vettu."
            """,
        Dialect.Oslo => """
            YOUR DIALECT IS OSLO. Write ordinary spoken bokmål the way people in Oslo talk. Never use dialect forms like æ, eg, ka, kor, ikkje or no for nå, and never Nynorsk.
            Use: jeg, meg, deg, hva, hvor, hvem, hvordan, hvorfor, er, nå, ikke, dere, til, liksom, ass, da, sant, a.
            Sound like this: "Jeg er på flyplassen nå, hvor lenge er du unna?" "Hva koster det, da?" "Det går jo fint, ass."
            """,
        _ => "",
    };

    private static readonly (Regex From, string Bergen, string North)[] Words =
    [
        (W("Hvorfor"), "Koffor", "Koffer"), (W("hvorfor"), "koffor", "koffer"),
        (W("Hvordan"), "Korleis", "Korsn"), (W("hvordan"), "korleis", "korsn"),
        (W("Hvor"), "Kor", "Kor"), (W("hvor"), "kor", "kor"),
        (W("Hva"), "Ka", "Ka"), (W("hva"), "ka", "ka"),
        (W("Er"), "E", "E"), (W("er"), "e", "e"),
        (W("nå"), "no", "no"), (W("Nå"), "No", "No"),
        (W("ikke"), "ikkje", "ikke"),
        (W("Jeg"), "Eg", "Æ"), (W("jeg"), "eg", "æ"),
    ];

    private static Regex W(string word) => new($@"\b{word}\b", RegexOptions.CultureInvariant);

    /// <summary>The pre-recorded gasps and quick reactions follow the same dialect as everything else the person says.</summary>
    public static string Adapt(string line, Dialect d)
    {
        if (d is Dialect.Unknown or Dialect.Oslo) return line;
        foreach (var (from, bergen, north) in Words)
        {
            string to = d == Dialect.Bergen ? bergen : north;
            if (d == Dialect.Trondersk && from.ToString().Contains("ikke")) to = "itj";
            line = from.Replace(line, to);
        }
        return line;
    }
}
