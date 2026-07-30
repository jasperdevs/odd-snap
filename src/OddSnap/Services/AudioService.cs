using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OddSnap.Services;

/// <summary>Lists audio devices and provides capture streams for recording.</summary>
public static class AudioService
{
    public sealed record AudioDevice(string Id, string Name, bool IsInput);

    /// <summary>Get all active microphone input devices.</summary>
    public static List<AudioDevice> GetMicrophones()
    {
        var list = new List<AudioDevice>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (dev)
                    list.Add(new AudioDevice(dev.ID, dev.FriendlyName, true));
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("audio.enumerate-microphones", "Failed to enumerate active microphone devices.", ex);
        }
        return list;
    }

    /// <summary>Get all active audio output devices (for desktop audio capture via loopback).</summary>
    public static List<AudioDevice> GetDesktopAudioDevices()
    {
        var list = new List<AudioDevice>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (dev)
                    list.Add(new AudioDevice(dev.ID, dev.FriendlyName, false));
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("audio.enumerate-desktop-devices", "Failed to enumerate active desktop audio devices.", ex);
        }
        return list;
    }

    /// <summary>Get the default microphone device ID, or null.</summary>
    public static string? GetDefaultMicrophoneId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return dev.ID;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("audio.default-microphone", "Failed to resolve the default microphone.", ex);
            return null;
        }
    }

    /// <summary>Get the default desktop audio device ID, or null.</summary>
    public static string? GetDefaultDesktopAudioId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return dev.ID;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("audio.default-desktop-device", "Failed to resolve the default desktop audio device.", ex);
            return null;
        }
    }
}
