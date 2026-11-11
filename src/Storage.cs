using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
namespace Toma.Windows;
public sealed class SavedSong
{
    public string Raw { get; set; } = ""; public string Source { get; set; } = ""; public int Offset
    {
        get; set;
    }
    public string Title { get; set; } = ""; public string Artist { get; set; } = "";
}
public sealed class Settings
{
    public bool ShowLatin { get; set; } = true;
    public IslandAppearance Appearance { get; set; } = new(); public bool Island { get; set; } = true; public bool Spectrum { get; set; } = true; public string Monitor { get; set; } = ""; public Dictionary<string, SavedSong> Songs { get; set; } = new();
}
public static class Storage
{
    public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TomaTomaWindows");
    public static Settings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(DirectoryPath, "settings.json"))) ?? new();
            settings.Appearance ??= new();
            settings.Appearance.Compact ??= new();
            settings.Appearance.Expanded ??= new();
            return settings;
        }
        catch { return new(); }
    }
    public static void Save(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string path = Path.Combine(DirectoryPath, "settings.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception e) { FileLog.Write(e.Message); }
    }
}
public static class FileLog
{
    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Storage.DirectoryPath);
            string file = Path.Combine(Storage.DirectoryPath, "app.log");
            if (File.Exists(file) && new FileInfo(file).Length > 500000)
                File.WriteAllText(file, "");
            File.AppendAllText(file, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
        }
        catch { }
    }
}
