using FlightCompanion.Game.Aviation;

namespace FlightCompanion.Game.World;

public enum PlaceKind { BushStrip, Beach, Airport }

public sealed record Place
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string[] Aliases { get; init; } = [];
    public required double Lat { get; init; }
    public required double Lon { get; init; }
    public required double ElevationFt { get; init; }
    public required double RunwayM { get; init; }
    public bool Soft { get; init; }
    public PlaceKind Kind { get; init; } = PlaceKind.BushStrip;
    public string Brief { get; init; } = "";
    /// <summary>NOK per US gallon. Null means nobody sells fuel here, only what you hauled in yourself.</summary>
    public double? FuelPriceNokPerGal { get; init; }
    /// <summary>Remote places where tourists, hunters and anglers usually are.</summary>
    public bool Remote { get; init; }
    /// <summary>Nearest station that actually reports a METAR.</summary>
    public string MetarStation { get; init; } = "ENNA";
    /// <summary>Where the passengers wait, when different from the airport reference point.</summary>
    public double? MeetLat { get; init; }
    public double? MeetLon { get; init; }

    public RunwayInfo Runway => new(RunwayM, ElevationFt, Soft);
    public double MeetingLat => MeetLat ?? Lat;
    public double MeetingLon => MeetLon ?? Lon;
}

public sealed record PointOfInterest(string Name, double Lat, double Lon, string Note, double RangeKm = 10);

/// <summary>
/// The map. Coordinates and elevations of the user's strips come from their own scenery projects.
/// Strip lengths there are placeholders in the scenery, so those lengths are estimates and may need tuning in the sim.
/// </summary>
public static class WorldData
{
    public static readonly IReadOnlyList<Place> Places =
    [
        new()
        {
            Id = "ENMP", Name = "Mollisjok", Aliases = ["Mollisjok Lodge", "Mollisjokk", "Mollisjohka", "the lodge", "Moloshock", "Molosjok", "Mollisjokk", "Mollishok", "lodgen"],
            Lat = 69.52911, Lon = 24.39231, ElevationFt = 1215, RunwayM = 550, Soft = true, Remote = true,
            MeetLat = 69.52880, MeetLon = 24.38900, MetarStation = "ENNA",
            Brief = "Timber lodge on the Finnmarksvidda, no road in summer, gravel and grass strip beside the river. Red sheds and cabins around it.",
        },
        new()
        {
            Id = "ENRA", Name = "Ravnastua", Aliases = ["Ravnastua Fjellstue", "Ravdnastuva"],
            Lat = 69.52717, Lon = 24.95613, ElevationFt = 1034, RunwayM = 450, Soft = true, Remote = true, MetarStation = "ENNA",
            Brief = "Old mountain inn in the middle of the vidda between Karasjok and Mollisjok, reached on foot or by snowmobile.",
        },
        new()
        {
            Id = "ENMB", Name = "Karasjok", Aliases = ["Maggie Beach", "Karasjok beach", "Karasjohka"],
            Lat = 69.45602, Lon = 25.45854, ElevationFt = 396, RunwayM = 400, Soft = true, Remote = true, MetarStation = "EFIV",
            Brief = "Sandy river bank outside Karasjok, the Sami capital. Short and soft, you land on the beach.",
        },
        new()
        {
            Id = "ENNA", Name = "Lakselv", Aliases = ["Banak", "Lakselv Banak"],
            Lat = 70.0689, Lon = 24.9735, ElevationFt = 25, RunwayM = 2700, Kind = PlaceKind.Airport,
            FuelPriceNokPerGal = 118, MetarStation = "ENNA",
            Brief = "Long runway at the head of the Porsanger fjord, where most tourists arrive on the airline.",
        },
        new()
        {
            Id = "ENAT", Name = "Alta", Aliases = ["Alta Airport"],
            Lat = 69.9761, Lon = 23.3717, ElevationFt = 9, RunwayM = 2253, Kind = PlaceKind.Airport,
            FuelPriceNokPerGal = 122, MetarStation = "ENAT",
            Brief = "The biggest town in the region, on the fjord. Airline connections to Oslo.",
        },
        new()
        {
            Id = "ENKA", Name = "Kautokeino", Aliases = ["Guovdageaidnu"],
            Lat = 69.0403, Lon = 23.0342, ElevationFt = 1165, RunwayM = 600, Soft = true, Remote = true, MetarStation = "EFET",
            Brief = "Sami town far south on the plateau, gravel strip, Easter festival and reindeer country.",
        },
        new()
        {
            Id = "EFIV", Name = "Ivalo", Aliases = ["Ivalo Finland"],
            Lat = 68.6073, Lon = 27.4053, ElevationFt = 481, RunwayM = 2500, Kind = PlaceKind.Airport,
            FuelPriceNokPerGal = 125, MetarStation = "EFIV",
            Brief = "Finnish Lapland, across the border, airline connections to Helsinki.",
        },
        new()
        {
            Id = "ENHF", Name = "Hammerfest", Aliases = [],
            Lat = 70.6797, Lon = 23.6686, ElevationFt = 266, RunwayM = 880, Kind = PlaceKind.Airport,
            FuelPriceNokPerGal = 128, MetarStation = "ENHF",
            Brief = "Short sloped coastal runway, famous for crosswinds.",
        },
        new()
        {
            Id = "ENHV", Name = "Honningsvag", Aliases = ["Honningsvåg", "North Cape", "Nordkapp"],
            Lat = 71.0097, Lon = 25.9836, ElevationFt = 46, RunwayM = 880, Kind = PlaceKind.Airport,
            FuelPriceNokPerGal = 132, MetarStation = "ENHV",
            Brief = "The town beside the North Cape, tourists love it, the weather there often does not love you.",
        },
        new()
        {
            Id = "ENKR", Name = "Kirkenes", Aliases = ["Høybuktmoen"],
            Lat = 69.7258, Lon = 29.8913, ElevationFt = 283, RunwayM = 2115, Kind = PlaceKind.Airport,
            FuelPriceNokPerGal = 125, MetarStation = "ENKR",
            Brief = "Far east by the Russian border, long leg from the vidda.",
        },
    ];

    /// <summary>What people here would point at. Positions in towns are close, not surveyed.</summary>
    public static readonly IReadOnlyList<PointOfInterest> Landmarks =
    [
        new("Sametinget", 69.4735, 25.5060, "The Sami Parliament in Karasjok, a striking wooden building shaped like a lavvo, known to every Sami", 5),
        new("Sapmi Park", 69.4715, 25.5153, "The Sami culture park in Karasjok", 4),
        new("Karasjok old church", 69.4722, 25.5111, "Finnmark's oldest church, the only building left standing here in 1944 when everything else was burned", 4),
        new("Karasjok", 69.4719, 25.5111, "The Sami capital of Norway, a small town on the river, Sametinget is here, coldest place in the country", 14),
        new("the Karasjohka river", 69.40, 25.60, "Salmon river running through Karasjok", 8),
        new("the Anarjohka river", 69.25, 25.20, "Border river with Finland, old gold panning country", 10),
        new("Iesjavri lake", 69.65, 24.25, "The biggest lake on the Finnmarksvidda, huge and shallow", 14),
        new("the Sautso canyon", 69.67, 23.57, "Northern Europe's biggest canyon, with the Alta river at the bottom", 12),
        new("the Alta dam", 69.71, 23.70, "The dam behind the Alta controversy, where Sami and environmental protesters stood up to the state around 1980", 8),
        new("the Northern Lights Cathedral", 69.967, 23.27, "The spiral titanium cathedral in Alta", 5),
        new("Alta", 69.97, 23.27, "The biggest town in Finnmark, at the head of the fjord", 14),
        new("Stabbursdalen", 70.15, 24.05, "The world's northernmost pine forest", 10),
        new("the Porsanger fjord", 70.25, 25.25, "Long fjord, reindeer swim across it every spring", 14),
        new("the North Cape", 71.171, 25.784, "307 m cliff, the end of Europe", 14),
        new("Mollisjok lodge", 69.52787, 24.38564, "The big old timber lodge with red sheds around it, right beside a very short gravel and grass strip by the river", 6),
        new("Ravnastua", 69.52717, 24.95613, "The old mountain inn alone in the middle of the vidda, a very small strip", 6),
        new("Kautokeino", 69.0100, 23.0400, "Sami town on the plateau, reindeer and silversmiths", 12),
        new("the Finnmarksvidda", 69.50, 24.60, "Treeless plateau with thousands of lakes and reindeer herds", 25),
    ];

    /// <summary>Kept for flights: every landmark. Airports count too, since they are what you see first when arriving.</summary>
    public static readonly IReadOnlyList<PointOfInterest> Sights =
        Landmarks.Concat(Places.Where(p => !Landmarks.Any(l => l.Name.Contains(p.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new PointOfInterest(p.Name, p.Lat, p.Lon, p.Brief, 12))).ToList();

    public static Place? ById(string? id) => Places.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Maps a place as spoken ("the beach at Karasjok", "banak") to a known place, or null.</summary>
    public static Place? Find(string? spoken)
    {
        if (string.IsNullOrWhiteSpace(spoken)) return null;
        string s = Normalize(spoken);
        foreach (var p in Places)
        {
            if (Normalize(p.Id) == s || Normalize(p.Name) == s) return p;
            if (p.Aliases.Any(a => Normalize(a) == s)) return p;
        }
        foreach (var p in Places)
            if (s.Contains(Normalize(p.Name)) || p.Aliases.Any(a => a.Length > 4 && s.Contains(Normalize(a)))) return p;
        return null;
    }

    public static Place? Nearest(double lat, double lon, double withinKm = double.MaxValue)
    {
        Place? best = null; double bestM = double.MaxValue;
        foreach (var p in Places)
        {
            double d = Geo.DistanceM(lat, lon, p.Lat, p.Lon);
            if (d < bestM) { bestM = d; best = p; }
        }
        return bestM <= withinKm * 1000 ? best : null;
    }

    public static PointOfInterest? NearestSight(double lat, double lon, double withinKm)
    {
        PointOfInterest? best = null; double bestM = withinKm * 1000;
        foreach (var s in Sights)
        {
            double d = Geo.DistanceM(lat, lon, s.Lat, s.Lon);
            if (d < bestM) { bestM = d; best = s; }
        }
        return best;
    }

    public static readonly string[] MetarStations = ["ENNA", "ENAT", "ENKR", "EFIV", "EFET", "ENHF", "ENHV"];

    private static string Normalize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s.ToLowerInvariant().Replace("å", "a").Replace("ø", "o").Replace("æ", "ae").Replace("á", "a"))
            if (char.IsLetterOrDigit(c) || c == ' ') sb.Append(c);
        return sb.ToString().Trim();
    }
}
