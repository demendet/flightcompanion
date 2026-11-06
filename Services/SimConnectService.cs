using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlightCompanion.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.FlightSimulator.SimConnect;

namespace FlightCompanion.Services
{
    public interface ISimConnectService
    {
        event EventHandler<AircraftData>? AircraftDataReceived;
        event EventHandler<bool>? ConnectionStatusChanged;
        
        bool IsConnected { get; }
        AircraftData CurrentAircraftData { get; }

        Task ConnectAsync();
        Task DisconnectAsync();
        void RequestAircraftData();

        // Writes payload station weights (pounds) into the sim. Index 0 -> PAYLOAD STATION WEIGHT:2,
        // index 1 -> :3, ... (station 1 = pilot is never touched). Missing stations are ignored by
        // SimConnect. Returns false if not connected.
        bool SetPayloadStations(double[] poundsPerStation);
    }

    public class SimConnectService : ISimConnectService, IDisposable
    {
        private SimConnect? _simConnect;
        private MessageForm? _messageForm;
        private readonly IConfiguration _configuration;
        private bool _isConnected;
        private AircraftData _currentAircraftData;
        
        private const int WM_USER_SIMCONNECT = 0x0402;
        
        public event EventHandler<AircraftData>? AircraftDataReceived;
        public event EventHandler<bool>? ConnectionStatusChanged;
        
        public bool IsConnected => _isConnected;
        public AircraftData CurrentAircraftData => _currentAircraftData;

        private enum DATA_DEFINE_ID
        {
            AIRCRAFT_DATA,
            PAYLOAD_DATA
        }

        private enum DATA_REQUEST_ID
        {
            AIRCRAFT_REQUEST
        }

        // 8 settable payload stations (PAYLOAD STATION WEIGHT:2 .. :9). Station 1 (pilot) is left alone.
        private const int PayloadStationCount = 8;
        private const int PayloadFirstStation = 2;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct PayloadData
        {
            public double S0, S1, S2, S3, S4, S5, S6, S7;
        }

        public SimConnectService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task ConnectAsync()
        {
            try
            {
                Console.WriteLine("Attempting to connect to SimConnect...");
                
                _messageForm = new MessageForm(this);
                Console.WriteLine($"MessageForm created with handle: 0x{_messageForm.Handle:X}");
                
                _simConnect = new SimConnect(
                    _configuration["SimConnect:AppName"] ?? "FlightCompanion",
                    _messageForm.Handle,
                    WM_USER_SIMCONNECT,
                    null,
                    0);

                Console.WriteLine("SimConnect object created, setting up event handlers...");

                _simConnect.OnRecvOpen += SimConnect_OnRecvOpen;
                _simConnect.OnRecvQuit += SimConnect_OnRecvQuit;
                _simConnect.OnRecvException += SimConnect_OnRecvException;
                _simConnect.OnRecvSimobjectData += SimConnect_OnRecvSimobjectData;

                SetupDataDefinitions();
                
                Console.WriteLine("Data definitions set up, waiting for connection...");
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect to SimConnect: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                _isConnected = false;
                ConnectionStatusChanged?.Invoke(this, false);
                throw; // Re-throw so UI can show the error
            }
        }

        private void SetupDataDefinitions()
        {
            if (_simConnect == null) return;

            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "PLANE ALT ABOVE GROUND", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "PLANE HEADING DEGREES MAGNETIC", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "PLANE HEADING DEGREES TRUE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "GROUND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "AIRSPEED INDICATED", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "AIRSPEED TRUE", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "VERTICAL SPEED", "feet per minute", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "PLANE PITCH DEGREES", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "PLANE BANK DEGREES", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "ROTATION VELOCITY BODY Y", "radians per second", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "FUEL TOTAL QUANTITY", "gallons", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "FUEL TOTAL QUANTITY WEIGHT", "pounds", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "TOTAL WEIGHT", "pounds", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "SIM ON GROUND", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "BRAKE PARKING INDICATOR", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "ENG COMBUSTION:1", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "ENG COMBUSTION:2", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "GEAR TOTAL PCT EXTENDED", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "FLAPS HANDLE PERCENT", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "AUTOPILOT MASTER", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "AUTOPILOT ALTITUDE LOCK", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "AUTOPILOT HEADING LOCK", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "AUTOPILOT AIRSPEED HOLD", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "AMBIENT WIND DIRECTION", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "AMBIENT WIND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "AMBIENT TEMPERATURE", "celsius", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "BAROMETER PRESSURE", "inches of mercury", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "ATC ID", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "ATC TYPE", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "ATC AIRLINE", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DATA_DEFINE_ID.AIRCRAFT_DATA, "ATC FLIGHT NUMBER", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            _simConnect.RegisterDataDefineStruct<AircraftData>(DATA_DEFINE_ID.AIRCRAFT_DATA);

            // Settable payload stations 2..9 (station 1 = pilot, never touched).
            for (int i = 0; i < PayloadStationCount; i++)
            {
                _simConnect.AddToDataDefinition(DATA_DEFINE_ID.PAYLOAD_DATA,
                    $"PAYLOAD STATION WEIGHT:{PayloadFirstStation + i}", "pounds",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            }
            _simConnect.RegisterDataDefineStruct<PayloadData>(DATA_DEFINE_ID.PAYLOAD_DATA);
        }

        public bool SetPayloadStations(double[] poundsPerStation)
        {
            if (_simConnect == null || !_isConnected) return false;
            try
            {
                var p = new PayloadData
                {
                    S0 = Get(poundsPerStation, 0), S1 = Get(poundsPerStation, 1),
                    S2 = Get(poundsPerStation, 2), S3 = Get(poundsPerStation, 3),
                    S4 = Get(poundsPerStation, 4), S5 = Get(poundsPerStation, 5),
                    S6 = Get(poundsPerStation, 6), S7 = Get(poundsPerStation, 7)
                };
                _simConnect.SetDataOnSimObject(DATA_DEFINE_ID.PAYLOAD_DATA,
                    SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, p);
                Console.WriteLine($"Payload set: [{string.Join(", ", poundsPerStation)}] lbs");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SetPayloadStations failed: {ex.Message}");
                return false;
            }

            static double Get(double[] a, int i) => i < a.Length ? a[i] : 0.0;
        }

        public void RequestAircraftData()
        {
            if (_simConnect != null && _isConnected)
            {
                _simConnect.RequestDataOnSimObject(
                    DATA_REQUEST_ID.AIRCRAFT_REQUEST,
                    DATA_DEFINE_ID.AIRCRAFT_DATA,
                    SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE,
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                    0, 0, 0);
            }
        }

        private void SimConnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            _isConnected = true;
            ConnectionStatusChanged?.Invoke(this, true);
            Console.WriteLine($"Connected to SimConnect - App: {data.szApplicationName}, Version: {data.dwApplicationVersionMajor}.{data.dwApplicationVersionMinor}");
            
            // Start data requests like AvaloniaTest does
            RequestDataOnConnected();
        }

        private void RequestDataOnConnected()
        {
            _simConnect?.RequestDataOnSimObject(
                DATA_REQUEST_ID.AIRCRAFT_REQUEST, 
                DATA_DEFINE_ID.AIRCRAFT_DATA, 
                SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }

        // Handle Windows messages like AvaloniaTest does
        public bool HandleWindowsEvent(int message)
        {
            switch (message)
            {
                case WM_USER_SIMCONNECT:
                    {
                        if (_simConnect != null)
                        {
                            try
                            {
                                _simConnect.ReceiveMessage();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"SimConnect message processing error: {ex.Message}");
                                RecoverFromError(ex);
                            }
                            return true;
                        }
                    }
                    break;
            }
            return false;
        }

        private void RecoverFromError(Exception exception)
        {
            Console.WriteLine($"SimConnect error: {exception.Message}");
            DisconnectAsync();
        }

        private void SimConnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            _isConnected = false;
            ConnectionStatusChanged?.Invoke(this, false);
            Console.WriteLine("SimConnect quit");
        }

        private void SimConnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            var exceptionName = Enum.GetName(typeof(SIMCONNECT_EXCEPTION), data.dwException) ?? "Unknown";
            Console.WriteLine($"SimConnect exception: {data.dwException} ({exceptionName}) - Request ID: {data.dwSendID}, Index: {data.dwIndex}");
        }

        private void SimConnect_OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            if (data.dwRequestID == (uint)DATA_REQUEST_ID.AIRCRAFT_REQUEST)
            {
                _currentAircraftData = (AircraftData)data.dwData[0];
                AircraftDataReceived?.Invoke(this, _currentAircraftData);
            }
        }

        public Task DisconnectAsync()
        {
            return Task.Run(() =>
            {
                _simConnect?.Dispose();
                _messageForm?.Dispose();
                _isConnected = false;
                ConnectionStatusChanged?.Invoke(this, false);
            });
        }

        public void Dispose()
        {
            DisconnectAsync().Wait();
        }

        private class MessageForm : Form
        {
            private readonly SimConnectService _service;

            public MessageForm(SimConnectService service)
            {
                _service = service;
                // Hide the form - it's just for message processing
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Visible = false;
                
                // Create the handle immediately
                var handle = this.Handle; // Forces handle creation
            }

            protected override void WndProc(ref Message m)
            {
                if (!_service.HandleWindowsEvent(m.Msg))
                {
                    base.WndProc(ref m);
                }
            }
        }
    }
}