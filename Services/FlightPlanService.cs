using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FlightCompanion.Models;
using Microsoft.Extensions.Configuration;

namespace FlightCompanion.Services
{
    public interface IFlightPlanService
    {
        event EventHandler<FlightPlan>? FlightPlanCreated;
        event EventHandler<FlightPlan>? FlightPlanUpdated;
        event EventHandler<FlightPlan>? FlightPlanCompleted;
        
        FlightPlan? ActiveFlightPlan { get; }
        List<FlightPlan> FlightPlanHistory { get; }
        
        Task CreateFlightPlanAsync(FlightPlan plan);
        Task UpdateFlightPlanStatusAsync(string planId, FlightPlanStatus status);
        Task SetActiveFlightPlanAsync(string planId);
        double CalculateDistanceToDestination(double currentLat, double currentLon);
        double CalculateEstimatedTimeToDestination(double currentLat, double currentLon, double groundSpeed);
        bool IsAtLocation(double lat, double lon, Location location);
        Location? GetNearestLocation(double lat, double lon);
    }

    public class FlightPlanService : IFlightPlanService
    {
        private readonly IConfiguration _configuration;
        private readonly List<FlightPlan> _flightPlans = new();
        private FlightPlan? _activeFlightPlan;
        private readonly List<Location> _locations;

        public event EventHandler<FlightPlan>? FlightPlanCreated;
        public event EventHandler<FlightPlan>? FlightPlanUpdated;
        public event EventHandler<FlightPlan>? FlightPlanCompleted;

        public FlightPlan? ActiveFlightPlan => _activeFlightPlan;
        public List<FlightPlan> FlightPlanHistory => _flightPlans;

        public FlightPlanService(IConfiguration configuration)
        {
            _configuration = configuration;

            // Load locations
            _locations = new List<Location>
            {
                new Location
                {
                    Name = _configuration["Locations:MollisjokLodge:Name"] ?? "Mollisjok Lodge",
                    Latitude = double.Parse(_configuration["Locations:MollisjokLodge:Latitude"] ?? "69.5283", CultureInfo.InvariantCulture),
                    Longitude = double.Parse(_configuration["Locations:MollisjokLodge:Longitude"] ?? "24.3901", CultureInfo.InvariantCulture),
                    BufferKm = double.Parse(_configuration["Locations:MollisjokLodge:BufferKm"] ?? "1.0", CultureInfo.InvariantCulture)
                },
                new Location
                {
                    Name = _configuration["Locations:MaggieBeach:Name"] ?? "Maggie Beach",
                    Latitude = double.Parse(_configuration["Locations:MaggieBeach:Latitude"] ?? "69.4562", CultureInfo.InvariantCulture),
                    Longitude = double.Parse(_configuration["Locations:MaggieBeach:Longitude"] ?? "25.4585", CultureInfo.InvariantCulture),
                    BufferKm = double.Parse(_configuration["Locations:MaggieBeach:BufferKm"] ?? "1.0", CultureInfo.InvariantCulture)
                },
                new Location
                {
                    Name = _configuration["Locations:Ravnastua:Name"] ?? "Ravnastua Fjellstue",
                    Latitude = double.Parse(_configuration["Locations:Ravnastua:Latitude"] ?? "69.5269", CultureInfo.InvariantCulture),
                    Longitude = double.Parse(_configuration["Locations:Ravnastua:Longitude"] ?? "24.9556", CultureInfo.InvariantCulture),
                    BufferKm = double.Parse(_configuration["Locations:Ravnastua:BufferKm"] ?? "1.0", CultureInfo.InvariantCulture)
                }
            };
        }

        public Task CreateFlightPlanAsync(FlightPlan plan)
        {
            plan.Status = FlightPlanStatus.Pending;
            _flightPlans.Add(plan);
            
            FlightPlanCreated?.Invoke(this, plan);
            
            Console.WriteLine($"Flight plan created: {plan.Origin} to {plan.Destination} for {plan.CustomerName}");
            
            return Task.CompletedTask;
        }

        public Task UpdateFlightPlanStatusAsync(string planId, FlightPlanStatus status)
        {
            var plan = _flightPlans.FirstOrDefault(p => p.PlanId == planId);
            if (plan != null)
            {
                plan.Status = status;
                
                if (status == FlightPlanStatus.Completed)
                {
                    FlightPlanCompleted?.Invoke(this, plan);
                    
                    if (_activeFlightPlan?.PlanId == planId)
                    {
                        _activeFlightPlan = null;
                    }
                }
                else
                {
                    FlightPlanUpdated?.Invoke(this, plan);
                }
                
                Console.WriteLine($"Flight plan {planId} status updated to {status}");
            }
            
            return Task.CompletedTask;
        }

        public Task SetActiveFlightPlanAsync(string planId)
        {
            var plan = _flightPlans.FirstOrDefault(p => p.PlanId == planId);
            if (plan != null)
            {
                _activeFlightPlan = plan;
                plan.Status = FlightPlanStatus.InProgress;
                
                FlightPlanUpdated?.Invoke(this, plan);
                
                Console.WriteLine($"Active flight plan set: {plan.Origin} to {plan.Destination}");
            }
            
            return Task.CompletedTask;
        }

        public double CalculateDistanceToDestination(double currentLat, double currentLon)
        {
            if (_activeFlightPlan == null) return 0;

            return CalculateDistance(currentLat, currentLon, 
                _activeFlightPlan.DestinationLat, _activeFlightPlan.DestinationLon);
        }

        public double CalculateEstimatedTimeToDestination(double currentLat, double currentLon, double groundSpeed)
        {
            if (_activeFlightPlan == null || groundSpeed <= 0) return 0;

            var distance = CalculateDistanceToDestination(currentLat, currentLon);
            var distanceNm = distance * 0.539957; // Convert km to nautical miles
            return distanceNm / groundSpeed * 60; // Return time in minutes
        }

        public bool IsAtLocation(double lat, double lon, Location location)
        {
            return location.IsNearLocation(lat, lon);
        }

        public Location? GetNearestLocation(double lat, double lon)
        {
            return _locations
                .OrderBy(l => CalculateDistance(lat, lon, l.Latitude, l.Longitude))
                .FirstOrDefault();
        }

        private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371; // Earth radius in km
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRadians(double angle) => angle * (Math.PI / 180);
    }
}