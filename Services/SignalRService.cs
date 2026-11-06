using System;
using System.Threading.Tasks;
using FlightCompanion.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

namespace FlightCompanion.Services
{
    public interface ISignalRService
    {
        event EventHandler<PlayerData>? PlayerDataReceived;
        event EventHandler<string>? PlayerDisconnected;
        event EventHandler<PhoneCall>? IncomingCall;
        event EventHandler<FlightPlan>? FlightPlanReceived;
        event EventHandler<bool>? ConnectionChanged;
        
        bool IsConnected { get; }
        string SessionId { get; }
        
        Task ConnectAsync(string playerName);
        Task DisconnectAsync();
        Task SendPlayerDataAsync(PlayerData data);
        Task SendFlightPlanAsync(FlightPlan plan);
        Task JoinSessionAsync(string sessionId);
        Task CreateSessionAsync();
    }

    public class SignalRService : ISignalRService, IDisposable
    {
        private HubConnection? _hubConnection;
        private readonly IConfiguration _configuration;
        private readonly IDebugLogService _logger;
        private bool _isConnected;
        private string _sessionId = string.Empty;
        private string _playerName = string.Empty;

        public event EventHandler<PlayerData>? PlayerDataReceived;
        public event EventHandler<string>? PlayerDisconnected;
        public event EventHandler<PhoneCall>? IncomingCall;
        public event EventHandler<FlightPlan>? FlightPlanReceived;
        public event EventHandler<bool>? ConnectionChanged;

        public bool IsConnected => _isConnected;
        public string SessionId => _sessionId;

        public SignalRService(IConfiguration configuration, IDebugLogService logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task ConnectAsync(string playerName)
        {
            _playerName = playerName;
            
            var hubUrl = _configuration["SignalR:HubUrl"] ?? "https://localhost:7001/flighthub";
            _logger.LogMultiplayer($"Attempting to connect to SignalR hub at: {hubUrl} with player name: {playerName}");
            
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            // Register event handlers
            _hubConnection.On<PlayerData>("ReceivePlayerData", (data) =>
            {
                PlayerDataReceived?.Invoke(this, data);
            });

            _hubConnection.On<string>("PlayerDisconnected", (playerId) =>
            {
                PlayerDisconnected?.Invoke(this, playerId);
            });

            _hubConnection.On<PhoneCall>("IncomingCall", (call) =>
            {
                IncomingCall?.Invoke(this, call);
            });

            _hubConnection.On<FlightPlan>("ReceiveFlightPlan", (plan) =>
            {
                FlightPlanReceived?.Invoke(this, plan);
            });

            _hubConnection.On<string>("SessionCreated", (sessionId) =>
            {
                _sessionId = sessionId;
                _logger.LogMultiplayer($"Session created: {sessionId}");
            });

            _hubConnection.On<string>("JoinedSession", (sessionId) =>
            {
                _sessionId = sessionId;
                _logger.LogMultiplayer($"Joined session: {sessionId}");
            });

            _hubConnection.Reconnecting += (error) =>
            {
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
                _logger.LogWarning($"Connection lost. Attempting to reconnect... Error: {error?.Message}", "Multiplayer");
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += (connectionId) =>
            {
                _isConnected = true;
                ConnectionChanged?.Invoke(this, true);
                _logger.LogMultiplayer($"Reconnected with ID: {connectionId}");
                return Task.CompletedTask;
            };

            _hubConnection.Closed += (error) =>
            {
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
                _logger.LogWarning($"Connection closed: {error?.Message}", "Multiplayer");
                return Task.CompletedTask;
            };

            try
            {
                await _hubConnection.StartAsync();
                _isConnected = true;
                ConnectionChanged?.Invoke(this, true);
                _logger.LogMultiplayer($"Successfully connected to SignalR hub at {hubUrl}");

                // Register player
                _logger.LogMultiplayer($"Registering player: {_playerName}");
                await _hubConnection.InvokeAsync("RegisterPlayer", _playerName);
                _logger.LogMultiplayer($"Player {_playerName} registered successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to connect to SignalR hub at {hubUrl}", ex, "Multiplayer");
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_hubConnection != null)
            {
                _logger.LogMultiplayer("Disconnecting from SignalR hub...");
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
                _logger.LogMultiplayer("Disconnected from SignalR hub");
            }
            
            _isConnected = false;
            ConnectionChanged?.Invoke(this, false);
        }

        public async Task SendPlayerDataAsync(PlayerData data)
        {
            if (_hubConnection != null && _isConnected)
            {
                data.SessionId = _sessionId;
                _logger.LogMultiplayer($"Sending player data for session {_sessionId}: Lat={data.Aircraft.Latitude:F6}, Lon={data.Aircraft.Longitude:F6}, Alt={data.Aircraft.Altitude:F1}ft");
                await _hubConnection.InvokeAsync("SendPlayerData", data);
            }
            else
            {
                _logger.LogWarning("Cannot send player data - not connected to SignalR hub", "Multiplayer");
            }
        }

        public async Task SendFlightPlanAsync(FlightPlan plan)
        {
            if (_hubConnection != null && _isConnected)
            {
                _logger.LogMultiplayer($"Sending flight plan for session {_sessionId}: {plan.Origin} -> {plan.Destination}");
                await _hubConnection.InvokeAsync("SendFlightPlan", _sessionId, plan);
            }
            else
            {
                _logger.LogWarning("Cannot send flight plan - not connected to SignalR hub", "Multiplayer");
            }
        }

        public async Task JoinSessionAsync(string sessionId)
        {
            if (_hubConnection != null && _isConnected)
            {
                _logger.LogMultiplayer($"Attempting to join session: {sessionId} as player: {_playerName}");
                await _hubConnection.InvokeAsync("JoinSession", sessionId, _playerName);
                _sessionId = sessionId;
                _logger.LogMultiplayer($"Joined session {sessionId} successfully");
            }
            else
            {
                _logger.LogWarning($"Cannot join session {sessionId} - not connected to SignalR hub", "Multiplayer");
            }
        }

        public async Task CreateSessionAsync()
        {
            if (_hubConnection != null && _isConnected)
            {
                _logger.LogMultiplayer($"Creating new session for player: {_playerName}");
                var newSessionId = await _hubConnection.InvokeAsync<string>("CreateSession", _playerName);
                _sessionId = newSessionId;
                _logger.LogMultiplayer($"Created new session: {newSessionId}");
            }
            else
            {
                _logger.LogWarning("Cannot create session - not connected to SignalR hub", "Multiplayer");
            }
        }

        public void Dispose()
        {
            DisconnectAsync().Wait();
        }
    }
}