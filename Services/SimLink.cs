using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.Flight;
using Microsoft.FlightSimulator.SimConnect;

namespace FlightCompanion.Services;

/// <summary>
/// The only place that talks to the sim. Runs on its own thread with a wait handle, reconnects by itself, and stays
/// useful when the sim is not running, since the game keeps its clock and its phone either way.
/// </summary>
public sealed class SimLink : IDisposable
{
    private enum Def { Flight = 1, Avatar, Touchdown, Count, Tanks, StationBase = 100, WriteBase = 1000 }
    private enum Req { Flight = 1, Avatar, Touchdown, Count, Tanks, StationBase = 100, SpawnBase = 5000, RemoveBase = 7000 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct FlightData
    {
        public double Lat, Lon, Alt, Agl, Gs, Ias, Vs, Pitch, Bank, Heading, G, OnGround, Brake, Combustion, Stall, Overspeed;
        public double FuelGal, TotalLb, CgPercent, WindDir, WindKt, Oat;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Title;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct OneDouble { public double V; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct OneInt { public int V; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct StationData
    {
        public double Lb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
    }

    private static readonly string[] TankNames = ["CENTER", "CENTER2", "CENTER3", "LEFT MAIN", "LEFT AUX", "LEFT TIP", "RIGHT MAIN", "RIGHT AUX", "RIGHT TIP", "EXTERNAL1", "EXTERNAL2"];

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TankData
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 22)] public double[] Values;
    }

    private readonly Thread _thread;
    private readonly AutoResetEvent _signal = new(false);
    private readonly ManualResetEvent _stop = new(false);
    private readonly object _callLock = new();
    private SimConnect? _sc;
    private bool _connected;

    private int _stationCount = -1;
    private string[] _stationNames = [];
    private double[] _stationLb = [];
    private readonly Dictionary<string, double> _tankCapacity = [];
    private readonly Dictionary<string, double> _tankLevel = [];
    private readonly Dictionary<string, int> _writeDefs = [];
    private int _nextWriteDef = (int)Def.WriteBase;
    private bool _onFoot;
    private double _touchdownFpm;

    private readonly Dictionary<uint, uint> _spawned = [];   // request id -> object id
    private readonly HashSet<uint> _pendingRemove = [];
    private uint _nextSpawnReq = (uint)Req.SpawnBase, _nextRemoveReq = (uint)Req.RemoveBase;

    public event Action<Telemetry>? Sample;
    public event Action<Health>? HealthChanged;

    public bool Connected => _connected;
    public IReadOnlyList<SimStation> Stations => StationMapper.Map(_stationNames);
    public IReadOnlyDictionary<string, double> TankCapacityGal => _tankCapacity;
    public double[] StationPounds => _stationLb;

    public SimLink(bool connect = true)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "simconnect" };
        if (connect) _thread.Start();
    }

    private void Run()
    {
        HealthChanged?.Invoke(new Health(HealthLevel.Working, Game.L.T("Waiting for the sim")));
        while (!_stop.WaitOne(0))
        {
            if (_sc is null && !TryConnect())
            {
                if (_stop.WaitOne(5000)) break;
                continue;
            }
            int which = WaitHandle.WaitAny([_stop, _signal], 2000);
            if (which == 0) break;
            if (which == 1)
            {
                try { lock (_callLock) _sc?.ReceiveMessage(); }
                catch (COMException ex)
                {
                    Log.Error("Sim", "Connection lost", ex);
                    Drop();
                }
            }
        }
        Drop();
    }

    private bool TryConnect()
    {
        try
        {
            var sc = new SimConnect("FlightCompanion", IntPtr.Zero, 0, _signal, 0);
            sc.OnRecvOpen += OnOpen;
            sc.OnRecvQuit += (_, _) => Drop();
            sc.OnRecvException += (_, d) => Log.Info("Sim", $"SimConnect exception {(SIMCONNECT_EXCEPTION)d.dwException} on send {d.dwSendID}, index {d.dwIndex}");
            sc.OnRecvSimobjectData += OnData;
            sc.OnRecvAssignedObjectId += OnAssigned;
            _sc = sc;
            Define(sc);
            return true;
        }
        catch (COMException) { return false; }   // sim not running yet, normal
    }

    private void Define(SimConnect sc)
    {
        void Add(Def d, string name, string? unit, SIMCONNECT_DATATYPE type = SIMCONNECT_DATATYPE.FLOAT64)
            => sc.AddToDataDefinition(d, name, unit, type, 0f, SimConnect.SIMCONNECT_UNUSED);

        Add(Def.Flight, "PLANE LATITUDE", "degrees");
        Add(Def.Flight, "PLANE LONGITUDE", "degrees");
        Add(Def.Flight, "PLANE ALTITUDE", "feet");
        Add(Def.Flight, "PLANE ALT ABOVE GROUND", "feet");
        Add(Def.Flight, "GROUND VELOCITY", "knots");
        Add(Def.Flight, "AIRSPEED INDICATED", "knots");
        Add(Def.Flight, "VERTICAL SPEED", "feet per minute");
        Add(Def.Flight, "PLANE PITCH DEGREES", "degrees");
        Add(Def.Flight, "PLANE BANK DEGREES", "degrees");
        Add(Def.Flight, "PLANE HEADING DEGREES TRUE", "degrees");
        Add(Def.Flight, "G FORCE", "gforce");
        Add(Def.Flight, "SIM ON GROUND", "bool");
        Add(Def.Flight, "BRAKE PARKING POSITION", "bool");
        Add(Def.Flight, "ENG COMBUSTION:1", "bool");
        Add(Def.Flight, "STALL WARNING", "bool");
        Add(Def.Flight, "OVERSPEED WARNING", "bool");
        Add(Def.Flight, "FUEL TOTAL QUANTITY", "gallons");
        Add(Def.Flight, "TOTAL WEIGHT", "pounds");
        Add(Def.Flight, "CG PERCENT", "percent");
        Add(Def.Flight, "AMBIENT WIND DIRECTION", "degrees");
        Add(Def.Flight, "AMBIENT WIND VELOCITY", "knots");
        Add(Def.Flight, "AMBIENT TEMPERATURE", "celsius");
        Add(Def.Flight, "TITLE", null, SIMCONNECT_DATATYPE.STRING256);
        sc.RegisterDataDefineStruct<FlightData>(Def.Flight);

        // These two sit in their own definitions: if a future sim drops one, the main feed still works.
        Add(Def.Avatar, "IS AVATAR", "Bool", SIMCONNECT_DATATYPE.INT32);
        sc.RegisterDataDefineStruct<OneInt>(Def.Avatar);
        Add(Def.Touchdown, "PLANE TOUCHDOWN NORMAL VELOCITY", "feet per minute");
        sc.RegisterDataDefineStruct<OneDouble>(Def.Touchdown);

        Add(Def.Count, "PAYLOAD STATION COUNT", "number", SIMCONNECT_DATATYPE.INT32);
        sc.RegisterDataDefineStruct<OneInt>(Def.Count);

        foreach (var t in TankNames)
        {
            Add(Def.Tanks, $"FUEL TANK {t} LEVEL", "percent over 100");
            Add(Def.Tanks, $"FUEL TANK {t} CAPACITY", "gallons");
        }
        sc.RegisterDataDefineStruct<TankData>(Def.Tanks);
    }

    private void OnOpen(SimConnect sc, SIMCONNECT_RECV_OPEN data)
    {
        _connected = true;
        HealthChanged?.Invoke(new Health(HealthLevel.Good, Game.L.T("Connected to the sim")));
        Log.Info("Sim", $"Connected, sim version {data.dwApplicationVersionMajor}.{data.dwApplicationVersionMinor}");
        uint user = SimConnect.SIMCONNECT_OBJECT_ID_USER;
        // Every 6th frame is plenty for reactions, and light on the sim.
        sc.RequestDataOnSimObject(Req.Flight, Def.Flight, user, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 6, 0);
        sc.RequestDataOnSimObject(Req.Avatar, Def.Avatar, user, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        sc.RequestDataOnSimObject(Req.Touchdown, Def.Touchdown, user, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
        sc.RequestDataOnSimObject(Req.Count, Def.Count, user, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        sc.RequestDataOnSimObject(Req.Tanks, Def.Tanks, user, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
    }

    private void OnData(SimConnect sc, SIMCONNECT_RECV_SIMOBJECT_DATA d)
    {
        uint r = d.dwRequestID;
        if (r == (uint)Req.Flight)
        {
            var f = (FlightData)d.dwData[0];
            Sample?.Invoke(new Telemetry
            {
                Time = DateTimeOffset.UtcNow,
                Lat = f.Lat, Lon = f.Lon, AltFt = f.Alt, AglFt = f.Agl, GroundSpeedKt = f.Gs, IasKt = f.Ias, VerticalFpm = f.Vs,
                PitchDeg = -f.Pitch, BankDeg = f.Bank, HeadingTrue = f.Heading, GForce = f.G, OnGround = f.OnGround != 0,
                ParkingBrake = f.Brake != 0, EngineRunning = f.Combustion != 0, StallWarning = f.Stall != 0, OverspeedWarning = f.Overspeed != 0,
                FuelGal = f.FuelGal, TotalWeightLb = f.TotalLb, CgPercent = f.CgPercent, WindFromDeg = f.WindDir, WindKt = f.WindKt, OatC = f.Oat,
                OnFoot = _onFoot, TouchdownFpm = _touchdownFpm, Title = f.Title ?? "",
            });
        }
        else if (r == (uint)Req.Avatar) _onFoot = ((OneInt)d.dwData[0]).V != 0;
        else if (r == (uint)Req.Touchdown) _touchdownFpm = ((OneDouble)d.dwData[0]).V;
        else if (r == (uint)Req.Count)
        {
            int n = ((OneInt)d.dwData[0]).V;
            if (n != _stationCount && n is >= 0 and <= 60) DefineStations(sc, n);
        }
        else if (r == (uint)Req.Tanks)
        {
            var t = (TankData)d.dwData[0];
            for (int i = 0; i < TankNames.Length; i++)
            {
                double level = t.Values[i * 2], cap = t.Values[i * 2 + 1];
                if (cap > 0) { _tankLevel[TankNames[i]] = level; _tankCapacity[TankNames[i]] = cap; }
                else { _tankLevel.Remove(TankNames[i]); _tankCapacity.Remove(TankNames[i]); }
            }
        }
        else if (r >= (uint)Req.StationBase && r < (uint)Req.StationBase + 60)
        {
            int i = (int)(r - (uint)Req.StationBase);
            var s = (StationData)d.dwData[0];
            if (i < _stationCount)
            {
                _stationNames[i] = s.Name ?? "";
                _stationLb[i] = s.Lb;
            }
        }
    }

    private void DefineStations(SimConnect sc, int n)
    {
        for (int i = _stationCount < 0 ? 0 : _stationCount; i < n; i++)
        {
            var def = (Def)((int)Def.StationBase + i);
            sc.AddToDataDefinition(def, $"PAYLOAD STATION WEIGHT:{i + 1}", "pounds", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
            sc.AddToDataDefinition(def, $"PAYLOAD STATION NAME:{i + 1}", null, SIMCONNECT_DATATYPE.STRING256, 0f, SimConnect.SIMCONNECT_UNUSED);
            sc.RegisterDataDefineStruct<StationData>(def);
            sc.RequestDataOnSimObject((Req)((int)Req.StationBase + i), def, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        _stationCount = Math.Max(_stationCount, n);
        Array.Resize(ref _stationNames, n);
        Array.Resize(ref _stationLb, n);
        for (int i = 0; i < n; i++) _stationNames[i] ??= "";
    }

    private void WriteVar(string name, string unit, double value)
    {
        var sc = _sc;
        if (sc is null || !_connected) return;
        lock (_callLock)
        {
            string key = name + "|" + unit;
            if (!_writeDefs.TryGetValue(key, out int def))
            {
                def = _nextWriteDef++;
                sc.AddToDataDefinition((Def)def, name, unit, SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
                sc.RegisterDataDefineStruct<OneDouble>((Def)def);
                _writeDefs[key] = def;
            }
            sc.SetDataOnSimObject((Def)def, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, new OneDouble { V = value });
        }
    }

    public void SetStations(IReadOnlyDictionary<int, double> poundsBySimIndex)
    {
        foreach (var (index, lb) in poundsBySimIndex) WriteVar($"PAYLOAD STATION WEIGHT:{index}", "pounds", lb);
    }

    public void SetFuelTotalGal(double gal)
    {
        foreach (var (tank, level) in FuelWrite.Levels(_tankCapacity, gal)) WriteVar($"FUEL TANK {tank} LEVEL", "percent over 100", level);
    }

    /// <summary>Places a static person or prop. Returns the handle for removing it later, or null when not connected.</summary>
    public uint? Spawn(string title, double lat, double lon, double headingDeg)
    {
        var sc = _sc;
        if (sc is null || !_connected) return null;
        lock (_callLock)
        {
            uint req = _nextSpawnReq++;
            try
            {
                sc.AICreateSimulatedObject(title, new SIMCONNECT_DATA_INITPOSITION
                {
                    Latitude = lat, Longitude = lon, Altitude = 0, Pitch = 0, Bank = 0, Heading = headingDeg, OnGround = 1, Airspeed = 0,
                }, (Req)req);
                return req;
            }
            catch (COMException ex)
            {
                Log.Error("Sim", $"Could not place {title}", ex);
                return null;
            }
        }
    }

    public void Remove(uint spawnRequest)
    {
        var sc = _sc;
        if (sc is null) return;
        lock (_callLock)
        {
            if (_spawned.Remove(spawnRequest, out uint obj))
            {
                try { sc.AIRemoveObject(obj, (Req)(_nextRemoveReq++)); }
                catch (COMException ex) { Log.Error("Sim", "Could not remove an object", ex); }
            }
            else _pendingRemove.Add(spawnRequest);   // the sim has not told us its id yet
        }
    }

    private void OnAssigned(SimConnect sc, SIMCONNECT_RECV_ASSIGNED_OBJECT_ID d)
    {
        if (_pendingRemove.Remove(d.dwRequestID)) sc.AIRemoveObject(d.dwObjectID, (Req)(_nextRemoveReq++));
        else _spawned[d.dwRequestID] = d.dwObjectID;
    }

    private void Drop()
    {
        lock (_callLock)
        {
            bool was = _sc is not null;
            try { _sc?.Dispose(); } catch (COMException ex) { Log.Error("Sim", "Dispose failed", ex); }
            _sc = null;
            _connected = false;
            _stationCount = -1;
            _stationNames = [];
            _stationLb = [];
            _writeDefs.Clear();
            _nextWriteDef = (int)Def.WriteBase;
            _spawned.Clear();
            _pendingRemove.Clear();
            _tankCapacity.Clear();
            _tankLevel.Clear();
            if (was) HealthChanged?.Invoke(new Health(HealthLevel.Working, Game.L.T("Waiting for the sim")));
        }
    }

    public void Dispose()
    {
        _stop.Set();
        if (_thread.IsAlive) _thread.Join(2000);
        _signal.Dispose();
        _stop.Dispose();
    }

    /// <summary>
    /// Static people the sim can place, found in installed packages by reading their sim.cfg titles. Empty is fine: the
    /// mission still works, you just do not see anyone standing there.
    /// </summary>
    public static IReadOnlyList<string> FindPeopleTitles()
    {
        var titles = new List<string>();
        string community = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator 2024", "Packages", "Community");
        if (!Directory.Exists(community)) return titles;
        foreach (var pkg in Directory.EnumerateDirectories(community))
        {
            string name = Path.GetFileName(pkg);
            if (!name.Contains("flightcompanion", StringComparison.OrdinalIgnoreCase) && !name.Contains("engm", StringComparison.OrdinalIgnoreCase)) continue;
            string misc = Path.Combine(pkg, "SimObjects", "Misc");
            if (!Directory.Exists(misc)) continue;
            foreach (var cfg in Directory.EnumerateFiles(misc, "sim.cfg", SearchOption.AllDirectories))
            {
                if (!cfg.Contains("Human", StringComparison.OrdinalIgnoreCase) && !cfg.Contains("people", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var line in File.ReadLines(cfg))
                    if (line.StartsWith("title=", StringComparison.OrdinalIgnoreCase)) titles.Add(line[6..].Trim());
            }
        }
        return titles;
    }
}
