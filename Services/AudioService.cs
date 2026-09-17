using System;
using System.IO;
using System.Windows.Media;

using Application = System.Windows.Application;

namespace AntigravityAutoPilot.Services;

public class AudioService
{
    private MediaPlayer? _mediaPlayer;
    private readonly object _lock = new();

    public string ResolveSoundPath(string? requestedPath = null, string defaultFileName = "done.mp3")
    {
        string fileName = string.IsNullOrWhiteSpace(requestedPath) ? defaultFileName : requestedPath.Trim();

        // 1. Direct path check
        if (Path.IsPathRooted(fileName) && File.Exists(fileName))
        {
            return fileName;
        }

        // 2. Relative to App Base Directory
        string appBasePath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(appBasePath))
        {
            return appBasePath;
        }

        // 3. Check source project directory
        string devPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\", fileName);
        if (File.Exists(devPath))
        {
            return Path.GetFullPath(devPath);
        }

        // 4. Current working directory
        string cwdPath = Path.Combine(Environment.CurrentDirectory, fileName);
        if (File.Exists(cwdPath))
        {
            return cwdPath;
        }

        return appBasePath;
    }

    /// <summary>
    /// Plays an audio sound with fallback path resolution.
    /// </summary>
    public bool PlaySound(string? customPath, string defaultFileName, int volumePercent = 100)
    {
        string fullPath = ResolveSoundPath(customPath, defaultFileName);

        if (!File.Exists(fullPath))
        {
            System.Diagnostics.Debug.WriteLine($"Sound file not found: {fullPath}");
            return false;
        }

        try
        {
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    PlayInternal(fullPath, volumePercent);
                }
                else
                {
                    Application.Current.Dispatcher.BeginInvoke(() => PlayInternal(fullPath, volumePercent));
                }
                return true;
            }
            else
            {
                PlayInternal(fullPath, volumePercent);
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error playing sound {fullPath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Plays the completion notification sound (done.mp3).
    /// </summary>
    public bool PlayDone(string? customPath = null, int volumePercent = 100)
    {
        return PlaySound(customPath, "done.mp3", volumePercent);
    }

    /// <summary>
    /// Plays the submit approval sound (submit.mp3).
    /// </summary>
    public bool PlaySubmit(string? customPath = null, int volumePercent = 100)
    {
        return PlaySound(customPath, "submit.mp3", volumePercent);
    }

    /// <summary>
    /// Plays the accept / accept all sound (accept_all.mp3).
    /// </summary>
    public bool PlayAccept(string? customPath = null, int volumePercent = 100)
    {
        return PlaySound(customPath, "accept_all.mp3", volumePercent);
    }

    /// <summary>
    /// Plays the implementation plan notification sound (plan.mp3).
    /// </summary>
    public bool PlayPlan(string? customPath = null, int volumePercent = 100)
    {
        return PlaySound(customPath, "plan.mp3", volumePercent);
    }

    private void PlayInternal(string filePath, int volumePercent)
    {
        lock (_lock)
        {
            try
            {
                _mediaPlayer?.Stop();
                _mediaPlayer?.Close();

                _mediaPlayer = new MediaPlayer();
                _mediaPlayer.Volume = Math.Clamp(volumePercent, 0, 100) / 100.0;
                _mediaPlayer.Open(new Uri(filePath, UriKind.Absolute));
                _mediaPlayer.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to execute MediaPlayer.Play: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            try
            {
                _mediaPlayer?.Stop();
                _mediaPlayer?.Close();
            }
            catch { }
        }
    }
}
