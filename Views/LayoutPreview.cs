using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using FlightCompanion.Game.Career;
using FlightCompanion.Game.Flight;
using FlightCompanion.Game.Missions;
using FlightCompanion.Game.People;
using FlightCompanion.Services;
using FlightCompanion.ViewModels;

namespace FlightCompanion.Views;

/// <summary>Draws the real window with made-up careers into image files, with no visible window, sim, mic or calls to the AI.</summary>
internal static class LayoutPreview
{
    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        Game.L.Norwegian = true;
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

        var now = DateTimeOffset.Now;
        Shot(dir, "negotiating", Negotiating(now, overloaded: false), now, phone: true);
        Shot(dir, "overloaded", Negotiating(now, overloaded: true), now, phone: true);
        Shot(dir, "empty", new CareerState(), now, phone: false);
        PeopleShots(dir, now);
    }

    private static void PeopleShots(string dir, DateTimeOffset now)
    {
        var c = new CareerState { CompanyName = "Vidda Air Charter", PilotName = "Matt", CashNok = 41250, Reputation = 38 };
        var rng = new Random(4);
        Game.People.CastFactory.LocalsOnly = true;
        for (int i = 0; i < 9; i++)
        {
            var p = Game.People.CastFactory.Create(rng);
            p.Talks = 1 + rng.Next(5);
            p.FlightsWithYou = i % 3 == 0 ? 0 : rng.Next(6);
            p.Trust = -70 + rng.NextDouble() * 150;
            p.LastContact = now.AddHours(-i * 7);
            p.Memories.Add(new MemoryNote(now.AddDays(-2), "Piloten pratet om bagasjen din og ville ikke gi seg."));
            if (p.FlightsWithYou > 0) p.Memories.Add(new MemoryNote(now.AddDays(-1), "Du var livredd i svingen over elva, men landingen var som en fjær."));
            c.People.Add(p);
            if (p.FlightsWithYou == 0) continue;
            var m = new Mission { Title = "Helg på lodgen", CallerId = p.Id, Phase = MissionPhase.Complete, CreatedAt = now.AddDays(-3), LandedAt = now.AddDays(-1),
                Plan = new MissionPlan { PickupId = "ENNA", DropoffId = "ENMP", PriceNok = 7800 } };
            m.Review = new FlightReview { Stars = 4.5, PayNok = 7800, TipNok = 600, RepairNok = 0, FuelCostNok = 900, ReputationDelta = 1.5, TrustDelta = 8, LateMinutes = 0, LandingVerdict = "God landing", Points = ["I rute"] };
            c.Missions.Add(m);
        }
        var host = new GameHost(c);
        var vm = new MainViewModel(host, live: false);
        System.Threading.Thread.Sleep(300);
        Dispatcher.UIThread.RunJobs();
        foreach (var (name, id) in new[] { ("people", (string?)null), ("person", c.People[1].Id) })
        {
            vm.PreviewPeople(id);
            var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            w.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
            w.Close();
        }
        host.Dispose();
    }

    private static CareerState Negotiating(DateTimeOffset now, bool overloaded)
    {
        var c = new CareerState { CompanyName = "Vidda Air Charter", PilotName = "Matt", CashNok = 41250, Reputation = 38, FlightsCompleted = 7, HoursFlown = 11.4 };
        var klaus = new Person { Name = "Klaus Reinhardt", Job = "retired engineer who has fished here for 40 years", Nationality = "German", WeightKg = 94, FlightsWithYou = 2, Trust = 30 };
        var walter = new Person { Name = "Walter Vogel", WeightKg = 81 };
        var uwe = new Person { Name = "Uwe Krause", WeightKg = 77 };
        c.People.AddRange([klaus, walter, uwe]);
        var m = new Mission
        {
            Title = "Laksefellesskapet", CallerId = klaus.Id, Phase = MissionPhase.Negotiating, CreatedAt = now,
            Plan = new MissionPlan
            {
                PickupId = "ENNA", DropoffId = "ENMP", PickupTime = now.AddDays(1).Date.AddHours(14), PriceNok = 7800, BaggageKg = overloaded ? 60 : 35,
                BaggageNote = "Fishing gear",
                Passengers =
                [
                    new() { PersonId = klaus.Id, Name = klaus.Name, Kg = 94, KgKnown = true },
                    new() { PersonId = walter.Id, Name = walter.Name, Kg = 81 },
                ],
            },
        };
        if (overloaded) m.Plan.Passengers.Add(new PlanPassenger { PersonId = uwe.Id, Name = uwe.Name, Kg = 77 });
        m.Plan.Changes.Add(new PlanChange(now, "passengers", "2", overloaded ? "3" : "2"));
        m.Plan.Changes.Add(new PlanChange(now, "baggage", "20 kg", overloaded ? "60 kg" : "35 kg"));
        c.Missions.Add(m);
        var other = new Mission { Title = "Reintelling", CallerId = klaus.Id, Phase = MissionPhase.Booked, CreatedAt = now, Plan = new MissionPlan { PickupId = "ENRA", DropoffId = "ENRA", PickupTime = now.AddDays(2).Date.AddHours(10), PriceNok = 4200 } };
        c.Missions.Add(other);
        c.Talks.Add(new TalkRecord { When = now.AddHours(-1), Mode = "Phone", Names = ["Ingrid Somby"], PersonIds = [], MissionTitle = "Til flyet", Offence = 2, HungUp = true, Lines = ["Matt: Hei, kan du komme nå?", "Ingrid Somby: Ja, jeg står klar.", "Matt: En stygg kommentar.", "Ingrid Somby: Hva faen sa du?"] });
        c.Talks.Add(new TalkRecord { When = now.AddHours(-3), Mode = "Intercom", Names = ["Klaus Reinhardt", "Uwe Krause"], PersonIds = [], MissionTitle = "Laksefellesskapet", Lines = ["Matt: Da lander vi.", "Klaus: Fin tur."] });
        for (int i = 0; i < 14; i++) c.Inbox.Add(new SmsMessage(now.AddHours(-2 - i), "Ingrid Somby", "Planken kom hel fram. Du skylder meg en kaffe."));
        return c;
    }

    private static void Shot(string dir, string name, CareerState career, DateTimeOffset now, bool phone)
    {
        var host = new GameHost(career);
        if (phone)
        {
            var who = career.People[0];
            host.PreviewSeed(new Telemetry { Time = now, Lat = 70.0689, Lon = 24.9735, OnGround = true, FuelGal = 31, Title = "Cessna Skyhawk 172", OatC = -4 }, null,
            [
                new ChatLine(now, who.Name, "Ja, hallo! Klaus her igjen, fra laksefolket. Du husker meg?", false),
                new ChatLine(now, "Matt", "Selvfølgelig. Hvor mange er dere denne gangen, og hvor mye utstyr?", true),
                new ChatLine(now, who.Name, "Tre nå, Uwe blir også med. Og utstyret, åh, kanskje seksti kilo. Ikke mye.", false),
                new ChatLine(now, "Matt", "Klaus, det er mye for en 172 inn til Mollisjok.", true),
            ]);
        }
        var vm = new MainViewModel(host, live: false);
        System.Threading.Thread.Sleep(300);   // let the view model's first queued picture land, then replace it below
        Dispatcher.UIThread.RunJobs();
        vm.RefreshNow();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame();
        frame?.Save(Path.Combine(dir, name + ".png"));
        if (w.FindControl<Avalonia.Controls.ScrollViewer>("RightScroll") is { } scroll)
        {
            File.AppendAllText(Path.Combine(dir, "scroll.txt"), $"{name}: viewport {scroll.Viewport.Height:0}, content {scroll.Extent.Height:0}, can scroll {scroll.Extent.Height > scroll.Viewport.Height}\n");
            scroll.ScrollToEnd();
            Dispatcher.UIThread.RunJobs();
            File.AppendAllText(Path.Combine(dir, "scroll.txt"), $"{name}: after scrolling to the end, offset {scroll.Offset.Y:0}\n");
        }
        if (name == "overloaded")
        {
            vm.PreviewPlane(new PlanePos(69.85, 24.8, 215, 95, [(70.06, 24.97), (70.0, 24.9), (69.92, 24.85), (69.85, 24.8)], 69.52911, 24.39231));
            vm.PreviewMap();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(2500);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            w.CaptureRenderedFrame()?.Save(Path.Combine(dir, "map.png"));
            foreach (var (tab, file) in new[] { (0, "logflights.png"), (1, "logtalks.png") })
            {
                vm.PreviewLog(tab);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                w.CaptureRenderedFrame()?.Save(Path.Combine(dir, file));
            }
        }
        w.Close();
        host.Dispose();
    }
}
