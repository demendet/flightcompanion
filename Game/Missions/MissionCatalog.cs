using FlightCompanion.Game.People;
using FlightCompanion.Game.World;

namespace FlightCompanion.Game.Missions;

public sealed record MissionTemplate
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Premise { get; init; }
    public int[]? Months { get; init; }
    public double MinReputation { get; init; }
    public int PaxMin { get; init; } = 1;
    public int PaxMax { get; init; } = 2;
    public double BagMinKg { get; init; } = 8;
    public double BagMaxKg { get; init; } = 25;
    public double CargoMinKg { get; init; }
    public double CargoMaxKg { get; init; }
    public string CargoNote { get; init; } = "";
    public string PickupRule { get; init; } = "any";
    public string DropRule { get; init; } = "any";
    public bool Urgent { get; init; }
    public bool Sightseeing { get; init; }
    public bool HasReturn { get; init; }
    public double PricePerHourNok { get; init; } = 5200;
    public double MinimumNok { get; init; } = 2500;
    public string[] LikelyTwists { get; init; } = [];
    public string? CallerJob { get; init; }
    public string? CallerNationality { get; init; }
    public double Weight { get; init; } = 1;
}

public static class MissionCatalog
{
    public static readonly IReadOnlyList<MissionTemplate> Templates =
    [
        new()
        {
            Id = "fishing_camp", Title = "Salmon camp", Months = [6, 7, 8, 9], PaxMin = 1, PaxMax = 3, BagMinKg = 15, BagMaxKg = 35,
            PickupRule = "airport", DropRule = "remote", HasReturn = true, CallerJob = "salmon angler",
            Premise = "You want to get out to {to} from {from} for some days of fishing, and be picked up again later.",
            LikelyTwists = ["heavy_bags", "salmon_home", "extra_friend", "pays_less"], Weight = 2,
        },
        new()
        {
            Id = "lodge_weekend", Title = "Weekend at the lodge", PaxMin = 2, PaxMax = 3, BagMinKg = 10, BagMaxKg = 22,
            PickupRule = "airport", DropRule = "remote", HasReturn = true,
            Premise = "You booked a stay near {to} and just landed on the airline at {from}. The road does not go there, so you need a lift.",
            LikelyTwists = ["heavy_bags", "late_passenger", "nervous_flyer", "generous", "forgot_something"], Weight = 2,
        },
        new()
        {
            Id = "aurora", Title = "Northern lights flight", Months = [9, 10, 11, 12, 1, 2, 3], PaxMin = 1, PaxMax = 3, BagMinKg = 1, BagMaxKg = 6,
            PickupRule = "airport", DropRule = "same", Sightseeing = true, PricePerHourNok = 6500, MinimumNok = 4500,
            Premise = "You want to fly above the clouds at night from {from} and see the northern lights, and come back to the same place. You have dreamed about it for years.",
            LikelyTwists = ["wants_detour", "nervous_flyer", "generous", "blogger_drama"], Weight = 2,
        },
        new()
        {
            Id = "scenic_canyon", Title = "Canyon and vidda sightseeing", PaxMin = 1, PaxMax = 3, BagMinKg = 1, BagMaxKg = 8,
            PickupRule = "airport", DropRule = "same", Sightseeing = true, PricePerHourNok = 5800, MinimumNok = 3500,
            Premise = "You want a sightseeing flight from {from} over the Sautso canyon and the Finnmarksvidda, and back to the same place.",
            LikelyTwists = ["wants_detour", "nervous_flyer", "blogger_drama"], Weight = 2,
        },
        new()
        {
            Id = "north_cape", Title = "North Cape day trip", MinReputation = 25, PaxMin = 1, PaxMax = 2, BagMinKg = 2, BagMaxKg = 10,
            PickupRule = "ENMP", DropRule = "ENHV", PricePerHourNok = 5600, MinimumNok = 6000,
            Premise = "You are staying at {from} and you refuse to go home without standing on the North Cape. You want to be flown to {to} today or tomorrow.",
            LikelyTwists = ["pays_less", "generous", "changed_mind"],
        },
        new()
        {
            Id = "reindeer_count", Title = "Reindeer count", Months = [3, 4, 5, 9, 10], PaxMin = 1, PaxMax = 2, BagMinKg = 8, BagMaxKg = 25,
            PickupRule = "remote", DropRule = "same", Sightseeing = true, CallerJob = "biologist",
            Premise = "You need to count reindeer herds on the plateau around {from} from low altitude, flying search lines for about an hour, and land back where you started.",
            LikelyTwists = ["heavy_bags", "wants_detour"],
        },
        new()
        {
            Id = "photographer", Title = "Photo flight", PaxMin = 1, PaxMax = 1, BagMinKg = 18, BagMaxKg = 40,
            PickupRule = "any", DropRule = "same", Sightseeing = true, CallerJob = "wildlife photographer", PricePerHourNok = 5400,
            Premise = "You need low and slow passes with the window open around {from} for a magazine story, and you bring all your camera cases.",
            LikelyTwists = ["heavy_bags", "wants_detour"],
        },
        new()
        {
            Id = "family_visit", Title = "Family visit", PaxMin = 1, PaxMax = 2, BagMinKg = 6, BagMaxKg = 18, CargoMinKg = 5, CargoMaxKg = 25, CargoNote = "Food and presents",
            PickupRule = "airport", DropRule = "remote", CallerNationality = "Norwegian",
            Premise = "You are visiting family out near {to}, and you bring food and presents from {from}.",
            LikelyTwists = ["heavy_bags", "nervous_flyer", "dog"],
        },
        new()
        {
            Id = "supply_lodge", Title = "Supply run", PaxMin = 0, PaxMax = 0, BagMinKg = 0, BagMaxKg = 0, CargoMinKg = 90, CargoMaxKg = 190, CargoNote = "Food, tools and building supplies",
            PickupRule = "airport", DropRule = "remote", CallerNationality = "Norwegian", PricePerHourNok = 4800,
            Premise = "You run a place out at {to} and you need supplies flown from {from}. You will not be on board. You meet the pilot on the phone only and your cousin loads it.",
            LikelyTwists = ["heavy_cargo"], Weight = 1.5,
        },
        new()
        {
            Id = "fuel_haul", Title = "Fuel for the outback", PaxMin = 0, PaxMax = 0, BagMinKg = 0, BagMaxKg = 0, CargoMinKg = 70, CargoMaxKg = 130, CargoNote = "Jerry cans of avgas",
            PickupRule = "airport", DropRule = "remote", CallerNationality = "Sami", PricePerHourNok = 4200,
            Premise = "You look after the place at {to} and you ask the pilot to bring jerry cans of avgas from {from}. Part of the fuel can stay in the shed for the pilot's own use afterwards.",
            LikelyTwists = [],
        },
        new()
        {
            Id = "nurse_visit", Title = "District nurse", PaxMin = 1, PaxMax = 1, BagMinKg = 8, BagMaxKg = 15, CargoMinKg = 3, CargoMaxKg = 12, CargoNote = "Medicine",
            PickupRule = "ENMB", DropRule = "remote", CallerJob = "village nurse", Urgent = false, PricePerHourNok = 4600,
            Premise = "An old patient out at {to} needs a visit and the track is impassable. You need to be there today.",
            LikelyTwists = [],
        },
        new()
        {
            Id = "medevac", Title = "Medevac: find them", Urgent = true, MinReputation = 20, PaxMin = 1, PaxMax = 1, BagMinKg = 0, BagMaxKg = 5,
            PickupRule = "remote", DropRule = "ENNA", CallerNationality = "Norwegian", CallerJob = "hunter", PricePerHourNok = 7000, MinimumNok = 6000,
            Premise = "Your hunting partner is badly hurt near {from}. You can see only lakes and a red shelter. The helicopter is grounded and you are scared and talking fast. Someone has to get to you now and take them to the hospital plane at {to}.",
            LikelyTwists = [], Weight = 0.5,
        },
        new()
        {
            Id = "hunters", Title = "Ptarmigan hunters", Months = [9, 10, 11, 2, 3], PaxMin = 2, PaxMax = 3, BagMinKg = 14, BagMaxKg = 30,
            PickupRule = "airport", DropRule = "remote", HasReturn = true, CallerJob = "hunter",
            Premise = "You and your friends are going ptarmigan hunting near {to}, flying in from {from}, with rifles, a tent and a dog if the pilot lets you.",
            LikelyTwists = ["dog", "heavy_bags", "salmon_home"],
        },
        new()
        {
            Id = "wedding", Title = "Wedding on the plateau", Months = [6, 7, 8], MinReputation = 35, PaxMin = 2, PaxMax = 3, BagMinKg = 10, BagMaxKg = 20,
            PickupRule = "airport", DropRule = "remote", PricePerHourNok = 7000, MinimumNok = 7000,
            Premise = "You are getting married at {to} in a tent by the lake, and it is all very last minute. You bring a dress, a cake and far too much champagne from {from}.",
            LikelyTwists = ["heavy_bags", "extra_friend", "generous", "late_passenger"], Weight = 0.5,
        },
        new()
        {
            Id = "dog_race", Title = "Finnmarkslopet checkpoint", Months = [2, 3], MinReputation = 30, PaxMin = 1, PaxMax = 2, BagMinKg = 8, BagMaxKg = 25, CargoMinKg = 20, CargoMaxKg = 60, CargoNote = "Dog food and veterinary kit",
            PickupRule = "ENKA", DropRule = "ENMB", CallerNationality = "Norwegian",
            Premise = "You are a race vet at Finnmarkslopet, Europe's longest sled dog race, and you need to hop between checkpoints from {from} to {to} with supplies.",
            LikelyTwists = ["dog", "heavy_cargo"],
        },
        new()
        {
            Id = "easter_festival", Title = "Sami Easter festival", Months = [3, 4], PaxMin = 2, PaxMax = 3, BagMinKg = 8, BagMaxKg = 18,
            PickupRule = "ENAT", DropRule = "ENKA",
            Premise = "You want to get to the Easter festival in Kautokeino. The joik competition starts soon and you are running late.",
            LikelyTwists = ["late_passenger", "pays_less"],
        },
        new()
        {
            Id = "ashes", Title = "A last flight", MinReputation = 40, PaxMin = 1, PaxMax = 2, BagMinKg = 1, BagMaxKg = 4,
            PickupRule = "remote", DropRule = "same", Sightseeing = true, MinimumNok = 3000,
            Premise = "Your father loved this land and you want to scatter his ashes over the lake he fished near {from}. You are quiet and careful and it means a lot to you.",
            LikelyTwists = [], Weight = 0.4,
        },
        new()
        {
            Id = "vip_hurry", Title = "Executive in a hurry", MinReputation = 45, PaxMin = 1, PaxMax = 1, BagMinKg = 5, BagMaxKg = 12,
            PickupRule = "remote", DropRule = "airport", PricePerHourNok = 9000, MinimumNok = 8000, CallerJob = "corporate executive",
            Premise = "You are stuck at {from}, your airline leaves from {to} soon, and money is no object if you make it.",
            LikelyTwists = ["pays_less", "generous"], Urgent = true, Weight = 0.7,
        },
    ];

    public static MissionTemplate? ById(string id) => Templates.FirstOrDefault(t => t.Id == id);
}

public static class TwistCatalog
{
    /// <summary>Hidden from the pilot until it surfaces. Each one changes the truth and tells the customer what to act out.</summary>
    public static void Apply(string twist, Mission m, Random rng)
    {
        var t = m.Truth;
        switch (twist)
        {
            case "heavy_bags":
            {
                double extra = Math.Round(8 + rng.NextDouble() * 24);
                t.BaggageKg += extra;
                m.SecretsForCaller.Add($"Your bags really weigh about {t.BaggageKg:0} kg in total, but on the phone you say about {t.ClaimedBaggageKg:0} kg, because you honestly never weighed them or because you are afraid of being told to leave things. If the pilot weighs them, act surprised, then bargain about what can stay behind.");
                break;
            }
            case "heavy_cargo":
            {
                double extra = Math.Round(15 + rng.NextDouble() * 35);
                t.CargoKg += extra;
                m.SecretsForCaller.Add($"The load is really {t.CargoKg:0} kg, not the {t.CargoKg - extra:0} kg you ordered. You did not know until someone counts it again.");
                break;
            }
            case "extra_friend":
            {
                var friend = new PlanPassenger { Name = "A friend", Kg = Math.Round(70 + rng.NextDouble() * 25), KgKnown = false };
                t.Passengers.Add(friend);
                m.SecretsForCaller.Add("You told the pilot the wrong number of people. One more friend turned up at the last minute. You only mention it when you meet the pilot in person, a bit embarrassed, and you hope it is fine.");
                break;
            }
            case "dog":
            {
                double dog = Math.Round(18 + rng.NextDouble() * 14);
                t.BaggageKg += dog;
                m.SecretsForCaller.Add($"You are bringing your dog, {dog:0} kg, and you forgot to mention it on the phone. You mention it only in person. The dog is good natured, and you will not leave it behind easily.");
                break;
            }
            case "salmon_home":
                t.ReturnExtraKg = Math.Round(18 + rng.NextDouble() * 35);
                m.SecretsForCaller.Add($"On the way home you will carry about {t.ReturnExtraKg:0} kg of fish or game you did not plan for. You are very proud of it.");
                break;
            case "late_passenger":
                m.SecretsForCaller.Add("One of your group is late for the pickup, and you keep making excuses and asking for ten more minutes. It genuinely is not your fault, you say.");
                break;
            case "nervous_flyer":
                m.SecretsForCaller.Add("You are much more scared of this flight than you admit on the phone. You grip things, you talk when frightened, and you ask whether the wing noises are normal.");
                foreach (var p in t.Passengers) p.KgKnown = false;
                break;
            case "wants_detour":
                m.SecretsForCaller.Add("Once you are airborne and enjoying it you ask for a detour over something you can see out of the window, and you offer cash on the spot, so the pilot has to think about fuel.");
                break;
            case "pays_less":
                m.SecretsForCaller.Add("After landing you try to pay less than agreed, with some reason that sounds reasonable to you. You give in if the pilot stands firm and you are treated well.");
                break;
            case "generous":
                m.SecretsForCaller.Add("You loved this and you tip generously if the flight was gentle and on time.");
                break;
            case "forgot_something":
                m.SecretsForCaller.Add("Ten minutes after takeoff you realise you left something important behind at the pickup, like medicine or a passport. You ask whether the pilot can turn back, and you feel terrible about it.");
                break;
            case "changed_mind":
                m.SecretsForCaller.Add("In the air you start wondering out loud whether you could land somewhere else instead, because of something you just remembered.");
                break;
            case "blogger_drama":
                m.SecretsForCaller.Add("You keep asking for something a little more dramatic for your video, like a steep turn or going lower, and you sulk if the pilot says no.");
                break;
        }
    }
}

public static class MissionFactory
{
    public static Mission Create(MissionTemplate tpl, CareerStateView career, Random rng, DateTimeOffset now, Person? caller = null)
    {
        caller ??= career.PickCaller(tpl, rng);
        var (from, to) = PickRoute(tpl, career.CurrentLat, career.CurrentLon, rng);
        var m = new Mission
        {
            TemplateId = tpl.Id, Title = tpl.Title, CreatedAt = now, CallerId = caller.Id,
            Urgent = tpl.Urgent, Sightseeing = tpl.Sightseeing,
            Premise = tpl.Premise.Replace("{from}", from.Name).Replace("{to}", to.Name),
            ReferredByName = caller.ReferredBy,
        };

        int paxCount = tpl.PaxMax == 0 ? 0 : tpl.PaxMin + rng.Next(tpl.PaxMax - tpl.PaxMin + 1);
        var truth = m.Truth;
        if (paxCount > 0)
        {
            truth.Passengers.Add(new PlanPassenger { PersonId = caller.Id, Name = caller.Name, Kg = caller.WeightKg });
            for (int i = 1; i < paxCount; i++)
            {
                var c = career.NewCompanion(caller, rng);
                m.CompanionIds.Add(c.Id);
                truth.Passengers.Add(new PlanPassenger { PersonId = c.Id, Name = c.Name, Kg = c.WeightKg });
            }
        }
        truth.ClaimedPassengers = paxCount;
        truth.BaggageKg = Math.Round(paxCount * (tpl.BagMinKg + rng.NextDouble() * (tpl.BagMaxKg - tpl.BagMinKg)));
        truth.ClaimedBaggageKg = truth.BaggageKg;
        truth.CargoKg = Math.Round(tpl.CargoMinKg + rng.NextDouble() * (tpl.CargoMaxKg - tpl.CargoMinKg));

        // The more truthful the person, the less likely a surprise. Regulars get a little more honest with a pilot they trust.
        double surprise = (1 - caller.Honesty) * 0.8 + 0.15 - Math.Max(0, caller.Trust) / 400.0;
        foreach (var tw in tpl.LikelyTwists)
            if (rng.NextDouble() < surprise * (tw is "heavy_bags" or "salmon_home" ? 1.4 : 0.8))
            {
                m.Twists.Add(tw);
                TwistCatalog.Apply(tw, m, rng);
            }

        m.Plan.PickupId = from.Id;
        m.Plan.DropoffId = to.Id;
        m.Plan.CargoNote = tpl.CargoNote;
        // The plan starts from what they will say, the truth stays hidden.
        foreach (var p in truth.Passengers.Take(truth.ClaimedPassengers))
            m.Plan.Passengers.Add(new PlanPassenger { PersonId = p.PersonId, Name = p.Name, Kg = PlanDefaults.PassengerKg });
        m.Plan.BaggageKg = 0;
        m.Plan.CargoKg = 0;

        return m;
    }

    /// <summary>Round trip for a drop-off: reversed route on the agreed date, with whatever they caught.</summary>
    public static Mission CreateReturn(Mission outbound, DateTimeOffset when, DateTimeOffset now)
    {
        var r = new Mission
        {
            TemplateId = outbound.TemplateId + "_return",
            Title = outbound.Title + ": going home",
            CreatedAt = now, CallerId = outbound.CallerId, CompanionIds = [.. outbound.CompanionIds],
            Phase = MissionPhase.Booked, FollowUpOf = outbound.Id,
            Premise = "You are at the end of your trip and the pilot is coming to fetch you, as agreed.",
            Truth = new MissionTruth
            {
                Passengers = outbound.Truth.Passengers.Select(p => new PlanPassenger { PersonId = p.PersonId, Name = p.Name, Kg = p.Kg }).ToList(),
                BaggageKg = outbound.Plan.BaggageKg + outbound.Truth.ReturnExtraKg,
                ClaimedBaggageKg = outbound.Plan.BaggageKg,
                ClaimedPassengers = outbound.Truth.Passengers.Count,
            },
        };
        r.Plan = new MissionPlan
        {
            PickupId = outbound.Plan.DropoffId, DropoffId = outbound.Plan.PickupId, PickupTime = when,
            Passengers = outbound.Plan.Passengers.Select(p => new PlanPassenger { PersonId = p.PersonId, Name = p.Name, Kg = p.Kg, KgKnown = p.KgKnown }).ToList(),
            BaggageKg = outbound.Plan.BaggageKg,
            PriceNok = outbound.Plan.PriceNok, Agreed = true,
        };
        if (outbound.Truth.ReturnExtraKg > 0)
        {
            r.Twists.Add("salmon_home");
            r.SecretsForCaller.Add($"You are bringing home about {outbound.Truth.ReturnExtraKg:0} kg more than you came with: your catch. The pilot planned for the old weight. Be proud of it, and fight for it if the pilot wants to leave it behind.");
        }
        return r;
    }

    private static (Place from, Place to) PickRoute(MissionTemplate tpl, double? lat, double? lon, Random rng)
    {
        var from = Pick(tpl.PickupRule, null, lat, lon, rng);
        var to = tpl.DropRule == "same" ? from : Pick(tpl.DropRule, from, lat, lon, rng);
        return (from, to);
    }

    private static Place Pick(string rule, Place? exclude, double? lat, double? lon, Random rng)
    {
        if (WorldData.ById(rule) is { } fixedPlace) return fixedPlace;
        IEnumerable<Place> pool = rule switch
        {
            "remote" => WorldData.Places.Where(p => p.Remote),
            "airport" => WorldData.Places.Where(p => p.Kind == PlaceKind.Airport && p.Id is "ENNA" or "ENAT" or "EFIV"),
            _ => WorldData.Places,
        };
        var list = pool.Where(p => p.Id != exclude?.Id).ToList();
        if (list.Count == 0) list = WorldData.Places.ToList();
        // Work tends to appear within reach of where the aircraft is, so the game does not become long ferry flights.
        if (lat is { } la && lon is { } lo)
        {
            double Score(Place p) => Aviation.Geo.DistanceNm(la, lo, p.Lat, p.Lon) + rng.NextDouble() * 80;
            return list.OrderBy(Score).First();
        }
        return list[rng.Next(list.Count)];
    }
}

/// <summary>The bits of career state the factory needs, so it does not depend on the whole save file.</summary>
public abstract class CareerStateView
{
    public abstract double? CurrentLat { get; }
    public abstract double? CurrentLon { get; }
    public abstract Person PickCaller(MissionTemplate tpl, Random rng);
    public abstract Person NewCompanion(Person lead, Random rng);
}
