using System;
using System.Runtime.InteropServices;

namespace FlightCompanion.Models
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct AircraftData
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public double AltitudeAGL;
        public double Heading;
        public double TrueHeading;
        public double GroundSpeed;
        public double IndicatedAirspeed;
        public double TrueAirspeed;
        public double VerticalSpeed;
        public double Pitch;
        public double Bank;
        public double YawRate;
        public double FuelTotalQuantity;
        public double FuelTotalWeight;
        public double TotalWeight;
        public double OnGround;
        public double ParkingBrake;
        public double Engine1Running;
        public double Engine2Running;
        public double GearPosition;
        public double FlapsPosition;
        public double AutopilotMaster;
        public double AutopilotAltitudeHold;
        public double AutopilotHeadingHold;
        public double AutopilotSpeedHold;
        public double WindDirection;
        public double WindSpeed;
        public double AmbientTemperature;
        public double BarometricPressure;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AircraftTitle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ATCId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ATCType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ATCAirline;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ATCFlightNumber;
    }

    public class PlayerData
    {
        public string PlayerId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public AircraftData Aircraft { get; set; }
        public DateTime LastUpdate { get; set; }
        public bool IsLocal { get; set; }
        public string SessionId { get; set; } = string.Empty;
    }

    public class FlightPlan
    {
        public string PlanId { get; set; } = Guid.NewGuid().ToString();
        public string Origin { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public double OriginLat { get; set; }
        public double OriginLon { get; set; }
        public double DestinationLat { get; set; }
        public double DestinationLon { get; set; }
        public int PassengerCount { get; set; }
        public double BaggageWeight { get; set; }
        public DateTime DepartureTime { get; set; }
        public DateTime EstimatedArrivalTime { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerType { get; set; } = string.Empty;
        public double AgreedPrice { get; set; }
        public string SpecialRequests { get; set; } = string.Empty;
        public FlightPlanStatus Status { get; set; }
    }

    public enum FlightPlanStatus
    {
        Pending,
        Accepted,
        InProgress,
        Completed,
        Cancelled
    }

    public class Location
    {
        public string Name { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double BufferKm { get; set; }
        
        public bool IsNearLocation(double lat, double lon)
        {
            var distance = CalculateDistance(Latitude, Longitude, lat, lon);
            return distance <= BufferKm;
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

    public class PhoneCall
    {
        public string CallId { get; set; } = Guid.NewGuid().ToString();
        public string CallerName { get; set; } = string.Empty;
        public string CallerVoiceId { get; set; } = string.Empty;
        public string CallerPersonality { get; set; } = string.Empty;
        public CallState State { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string ConversationContext { get; set; } = string.Empty;
    }

    public enum CallState
    {
        Incoming,
        Active,
        OnHold,
        Ended,
        Missed
    }

    public class VoiceProfile
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string VoiceId { get; set; } = string.Empty;
        public string Personality { get; set; } = string.Empty;
    }

    public class ConversationMessage
    {
        public string Speaker { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsFromCaller { get; set; }
        public string SpeakerColor => IsFromCaller ? "#FF00BCFF" : "#FF00FF00"; // Blue for caller, green for pilot
    }
}