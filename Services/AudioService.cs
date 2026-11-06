using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FlightCompanion.Services
{
    public interface IAudioService
    {
        event EventHandler<byte[]>? AudioDataAvailable;
        event EventHandler<float>? VolumeChanged;
        
        bool IsRecording { get; }
        bool IsPlaying { get; }
        
        Task StartRecordingAsync();
        Task StopRecordingAsync();
        Task<byte[]> GetRecordedAudioAsync();
        Task PlayAudioAsync(byte[] audioData);
        Task PlayAudioStreamAsync(Stream audioStream);
        void StopPlayback();
        float GetCurrentVolume();
    }

    public class AudioService : IAudioService, IDisposable
    {
        private WaveInEvent? _waveIn;
        private WaveOutEvent? _waveOut;
        private MemoryStream? _recordingStream;
        private BinaryWriter? _recordingWriter;
        private bool _isRecording;
        private bool _isPlaying;
        private float _currentVolume;
        private readonly object _playLock = new(); // guards _waveOut across the play thread + barge-in

        public event EventHandler<byte[]>? AudioDataAvailable;
        public event EventHandler<float>? VolumeChanged;

        public bool IsRecording => _isRecording;
        public bool IsPlaying => _isPlaying;

        public Task StartRecordingAsync()
        {
            return Task.Run(() =>
            {
                if (_isRecording) return;

                _recordingStream = new MemoryStream();
                _recordingWriter = new BinaryWriter(_recordingStream);

                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 16, 1), // 16kHz, 16-bit, mono
                    BufferMilliseconds = 50
                };

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;

                _waveIn.StartRecording();
                _isRecording = true;

                Console.WriteLine("Recording started");
            });
        }

        public Task StopRecordingAsync()
        {
            return Task.Run(() =>
            {
                if (!_isRecording || _waveIn == null) return;

                _waveIn.StopRecording();
                _isRecording = false;

                Console.WriteLine("Recording stopped");
            });
        }

        public Task<byte[]> GetRecordedAudioAsync()
        {
            return Task.Run(() =>
            {
                if (_recordingStream == null) return Array.Empty<byte>();

                var audioData = _recordingStream.ToArray();
                
                // Create WAV file format
                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);
                
                // WAV header
                writer.Write("RIFF".ToCharArray());
                writer.Write(36 + audioData.Length);
                writer.Write("WAVE".ToCharArray());
                writer.Write("fmt ".ToCharArray());
                writer.Write(16); // Subchunk1Size
                writer.Write((short)1); // AudioFormat (PCM)
                writer.Write((short)1); // NumChannels
                writer.Write(16000); // SampleRate
                writer.Write(16000 * 1 * 2); // ByteRate
                writer.Write((short)2); // BlockAlign
                writer.Write((short)16); // BitsPerSample
                writer.Write("data".ToCharArray());
                writer.Write(audioData.Length);
                writer.Write(audioData);

                return ms.ToArray();
            });
        }

        public async Task PlayAudioAsync(byte[] audioData)
        {
            StopPlayback();

            await Task.Run(() =>
            {
                // Everything the native audio device touches is created, used, AND disposed on
                // THIS one thread. Barge-in (StopPlayback) only nulls the shared field under a
                // lock — it never touches the native object — so there's no cross-thread race
                // (the previous version disposed from two threads and crashed the process).
                WaveOutEvent? waveOut = null;
                WaveStream? reader = null;
                MemoryStream? ms = null;
                try
                {
                    ms = new MemoryStream(audioData);
                    // ElevenLabs returns MP3; try MP3 first, fall back to WAV.
                    try { reader = new Mp3FileReader(ms); }
                    catch { ms.Position = 0; reader = new WaveFileReader(ms); }

                    waveOut = new WaveOutEvent { Volume = 1.0f };
                    waveOut.Init(reader);
                    lock (_playLock) { _waveOut = waveOut; _isPlaying = true; }
                    waveOut.Play();

                    // Loop on the LOCAL handle; break if barge-in cleared/replaced the field.
                    while (waveOut.PlaybackState == PlaybackState.Playing)
                    {
                        lock (_playLock) { if (!ReferenceEquals(_waveOut, waveOut)) break; }
                        Thread.Sleep(40);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error playing audio: {ex.Message}");
                }
                finally
                {
                    lock (_playLock) { if (ReferenceEquals(_waveOut, waveOut)) { _waveOut = null; _isPlaying = false; } }
                    try { waveOut?.Dispose(); } catch { }  // Dispose stops playback; only this thread does it
                    try { reader?.Dispose(); } catch { }
                    try { ms?.Dispose(); } catch { }
                }
            });
        }

        public async Task PlayAudioStreamAsync(Stream audioStream)
        {
            if (_isPlaying) StopPlayback();

            await Task.Run(() =>
            {
                try
                {
                    // Copy stream to memory for processing
                    using var ms = new MemoryStream();
                    audioStream.CopyTo(ms);
                    ms.Position = 0;

                    // Try to read as MP3 first (ElevenLabs returns MP3)
                    WaveStream reader;
                    try
                    {
                        reader = new Mp3FileReader(ms);
                    }
                    catch
                    {
                        // Fall back to WAV if not MP3
                        ms.Position = 0;
                        reader = new WaveFileReader(ms);
                    }

                    using (reader)
                    {
                        _waveOut = new WaveOutEvent
                        {
                            Volume = 0.8f
                        };

                        _waveOut.Init(reader);
                        _waveOut.PlaybackStopped += OnPlaybackStopped;
                        
                        _isPlaying = true;
                        _waveOut.Play();

                        // Wait for playback to complete
                        while (_waveOut.PlaybackState == PlaybackState.Playing)
                        {
                            Thread.Sleep(100);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error playing audio stream: {ex.Message}");
                    _isPlaying = false;
                }
            });
        }

        public void StopPlayback()
        {
            // Only signal — null the shared field under the lock. The playing thread notices
            // (_waveOut != its local), breaks its loop, and disposes the device itself (Dispose
            // stops playback). This thread never touches the native object -> no race, no crash.
            lock (_playLock)
            {
                _waveOut = null;
                _isPlaying = false;
            }
        }

        public float GetCurrentVolume()
        {
            return _currentVolume;
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_recordingWriter != null)
            {
                _recordingWriter.Write(e.Buffer, 0, e.BytesRecorded);
                
                // Calculate volume level
                float max = 0;
                for (int i = 0; i < e.BytesRecorded; i += 2)
                {
                    short sample = BitConverter.ToInt16(e.Buffer, i);
                    float sampleF = sample / 32768f;
                    if (Math.Abs(sampleF) > max) max = Math.Abs(sampleF);
                }
                
                _currentVolume = max;
                VolumeChanged?.Invoke(this, max);
                
                // Notify listeners of new audio data
                var audioData = new byte[e.BytesRecorded];
                Array.Copy(e.Buffer, audioData, e.BytesRecorded);
                AudioDataAvailable?.Invoke(this, audioData);
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _waveIn?.Dispose();
            _waveIn = null;
            _recordingWriter?.Close();
            _recordingWriter = null;
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            _isPlaying = false;
            _waveOut?.Dispose();
            _waveOut = null;
        }

        public void Dispose()
        {
            StopRecordingAsync().Wait();
            StopPlayback();
            _recordingStream?.Dispose();
            _recordingWriter?.Dispose();
            _waveIn?.Dispose();
            _waveOut?.Dispose();
        }
    }
}