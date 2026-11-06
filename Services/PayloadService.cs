using System;
using System.Collections.Generic;
using System.Linq;
using FlightCompanion.Models;

namespace FlightCompanion.Services
{
    // A pending "you're on the ground at a pickup point — load the passengers?" prompt.
    public class PayloadPrompt
    {
        public FlightPlan Plan { get; init; } = null!;
        public string LocationName { get; init; } = string.Empty;
        public int PassengerCount { get; init; }
        public double BaggageKg { get; init; }
        // Total added weight (pax @ 84 kg incl. carry-on + hold baggage), for display.
        public double TotalKg => PassengerCount * PaxKg + BaggageKg;
        public const double PaxKg = 84.0;
    }

    public interface IPayloadService
    {
        // Fired when the aircraft is stopped on the ground within range of a lodge that has a
        // loadable trip waiting. The UI shows a "Load N passengers + M kg?" card.
        event EventHandler<PayloadPrompt>? LoadAvailable;
        // Fired after payload has actually been written to the sim.
        event EventHandler<PayloadPrompt>? PayloadApplied;
        // Fired when the aircraft leaves the area / prompt is no longer valid.
        event EventHandler? LoadCleared;

        // Push the plan's passengers + baggage into the sim via SimConnect payload stations.
        bool Load(PayloadPrompt prompt);
    }

    public class PayloadService : IPayloadService
    {
        // "A few meters" isn't realistic — lodge coordinates are approximate and the strip is
        // usually offset — so treat anything inside this radius, stopped, as "at the pickup".
        private const double ArriveKm = 0.30;   // ~300 m to arm the prompt
        private const double LeaveKm = 0.60;    // hysteresis so it doesn't flicker
        private const double GroundSpeedMax = 5.0; // knots — essentially stopped
        private const double PaxLb = PayloadPrompt.PaxKg * 2.20462; // per-passenger weight
        private const double KgToLb = 2.20462;
        private const int MaxPaxStations = 6; // stations 2..7 hold passengers; 9 holds baggage

        private readonly ISimConnectService _simConnect;
        private readonly IFlightPlanService _flightPlans;
        private readonly IDebugLogService _logger;

        private readonly HashSet<string> _loadedPlanIds = new();
        private string? _promptedPlanId;

        public event EventHandler<PayloadPrompt>? LoadAvailable;
        public event EventHandler<PayloadPrompt>? PayloadApplied;
        public event EventHandler? LoadCleared;

        public PayloadService(ISimConnectService simConnect, IFlightPlanService flightPlans, IDebugLogService logger)
        {
            _simConnect = simConnect;
            _flightPlans = flightPlans;
            _logger = logger;
            _simConnect.AircraftDataReceived += OnAircraftData;
        }

        private void OnAircraftData(object? sender, AircraftData data)
        {
            var onGround = data.OnGround > 0.5;
            var stopped = data.GroundSpeed < GroundSpeedMax;

            var nearest = _flightPlans.GetNearestLocation(data.Latitude, data.Longitude);
            var distKm = nearest == null ? double.MaxValue
                : Haversine(data.Latitude, data.Longitude, nearest.Latitude, nearest.Longitude);

            // Left the area (or airborne) — retract any standing prompt.
            if (_promptedPlanId != null && (!onGround || distKm > LeaveKm))
            {
                _promptedPlanId = null;
                LoadCleared?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!onGround || !stopped || nearest == null || distKm > ArriveKm) return;

            // Find a trip that departs from this lodge, isn't loaded yet, and isn't already prompted.
            var plan = FindLoadablePlan(nearest.Name);
            if (plan == null || _loadedPlanIds.Contains(plan.PlanId) || _promptedPlanId == plan.PlanId) return;

            _promptedPlanId = plan.PlanId;
            var prompt = new PayloadPrompt
            {
                Plan = plan,
                LocationName = nearest.Name,
                PassengerCount = plan.PassengerCount,
                BaggageKg = plan.BaggageWeight
            };
            _logger.LogMap($"[Payload] Arrived at {nearest.Name} ({distKm * 1000:F0} m) — {plan.PassengerCount} pax / {plan.BaggageWeight:F0} kg ready to load.");
            LoadAvailable?.Invoke(this, prompt);
        }

        private FlightPlan? FindLoadablePlan(string lodgeName)
        {
            bool Match(FlightPlan p) =>
                !string.IsNullOrEmpty(p.Origin) &&
                (p.Origin.Contains(lodgeName, StringComparison.OrdinalIgnoreCase) ||
                 lodgeName.Contains(p.Origin, StringComparison.OrdinalIgnoreCase)) &&
                p.Status is FlightPlanStatus.Pending or FlightPlanStatus.Accepted or FlightPlanStatus.InProgress;

            // Prefer a plan explicitly originating here; fall back to the active plan.
            return _flightPlans.FlightPlanHistory.FirstOrDefault(Match)
                   ?? (_flightPlans.ActiveFlightPlan != null && Match(_flightPlans.ActiveFlightPlan)
                        ? _flightPlans.ActiveFlightPlan : null);
        }

        public bool Load(PayloadPrompt prompt)
        {
            var stations = BuildStations(prompt.PassengerCount, prompt.BaggageKg);
            var ok = _simConnect.SetPayloadStations(stations);
            if (ok)
            {
                _loadedPlanIds.Add(prompt.Plan.PlanId);
                _promptedPlanId = null;
                _logger.LogMap($"[Payload] Loaded {prompt.PassengerCount} pax + {prompt.BaggageKg:F0} kg baggage at {prompt.LocationName} ({prompt.TotalKg:F0} kg total).");
                PayloadApplied?.Invoke(this, prompt);
            }
            else
            {
                _logger.LogMap("[Payload] Load failed — SimConnect not connected or rejected the write.");
            }
            return ok;
        }

        // Distribute passengers one-per-station across stations 2..7, hold baggage in station 9.
        private static double[] BuildStations(int pax, double baggageKg)
        {
            var arr = new double[8]; // maps to PAYLOAD STATION WEIGHT:2 .. :9
            int placed = 0;
            for (int i = 0; i < MaxPaxStations && placed < pax; i++, placed++)
                arr[i] = PaxLb;
            if (placed < pax) // more pax than stations — pile the rest onto the last cabin station
                arr[MaxPaxStations - 1] += (pax - placed) * PaxLb;
            arr[7] = baggageKg * KgToLb; // station 9 = hold baggage
            return arr;
        }

        private static double Haversine(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }
    }
}
