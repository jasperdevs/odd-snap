using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Yoink.Services;

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
                list.Add(new AudioDevice(dev.ID, dev.FriendlyName, true));
                dev.Dispose();
            }
        }
        catch { }
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
                list.Add(new AudioDevice(dev.ID, dev.FriendlyName, false));
                dev.Dispose();
            }
        }
        catch { }
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
        catch { return null; }
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
        catch { return null; }
    }
}
