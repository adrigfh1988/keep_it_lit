using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// F118: where the campaign save physically lives. SaveSystem owns the format and the migration; a store only moves
/// text. Plain classes, no MonoBehaviour, no static state.
/// </summary>
public interface ISaveStore
{
    /// <summary>The main save text, or "" when there is none (or only an untrusted half-written temp file).</summary>
    string Read();
    /// <summary>Replaces the main save; the previous one becomes the backup. Throws on failure (SaveSystem logs).</summary>
    void Write(string json);
    /// <summary>Removes the main save AND the backup (a Win / START OVER must not be resurrected). Keeps an "unreadable" copy.</summary>
    void Delete();
    bool TryReadBackup(out string json);
    /// <summary>Moves the unreadable main save aside (never deletes it) so the next read finds no save.</summary>
    void SetAside(string tag);
    /// <summary>Moves an unreadable backup aside as well, so it is not re-reported.</summary>
    void SetAsideBackup();
    /// <summary>Where the data is, for logs.</summary>
    string Describe();
    /// <summary>True when the main file and the backup are both absent (used by the legacy PlayerPrefs move).</summary>
    bool IsEmpty();
}

/// <summary>PlayerPrefs, used on WebGL only (IndexedDB). A backup key is written before each overwrite.</summary>
public sealed class SavePrefsStore : ISaveStore
{
    private readonly string _key;
    public SavePrefsStore(string key) { _key = key; }

    public string Read() => PlayerPrefs.HasKey(_key) ? PlayerPrefs.GetString(_key, "") : "";

    public void Write(string json)
    {
        if (PlayerPrefs.HasKey(_key))
            PlayerPrefs.SetString(_key + ".bak", PlayerPrefs.GetString(_key, ""));
        PlayerPrefs.SetString(_key, json);
        PlayerPrefs.Save();
    }

    public void Delete()
    {
        PlayerPrefs.DeleteKey(_key);
        PlayerPrefs.DeleteKey(_key + ".bak");
        PlayerPrefs.Save();
    }

    public bool TryReadBackup(out string json)
    {
        json = PlayerPrefs.HasKey(_key + ".bak") ? PlayerPrefs.GetString(_key + ".bak", "") : "";
        return !string.IsNullOrEmpty(json);
    }

    public void SetAside(string tag)
    {
        if (PlayerPrefs.HasKey(_key)) PlayerPrefs.SetString(_key + ".unreadable", PlayerPrefs.GetString(_key, ""));
        PlayerPrefs.DeleteKey(_key);
        PlayerPrefs.Save();
        Debug.LogWarning($"SaveSystem: set the save aside under PlayerPrefs key {_key}.unreadable ({tag}).");
    }

    public void SetAsideBackup()
    {
        if (PlayerPrefs.HasKey(_key + ".bak")) PlayerPrefs.SetString(_key + ".bak.unreadable", PlayerPrefs.GetString(_key + ".bak", ""));
        PlayerPrefs.DeleteKey(_key + ".bak");
        PlayerPrefs.Save();
    }

    public bool IsEmpty() => !PlayerPrefs.HasKey(_key) && !PlayerPrefs.HasKey(_key + ".bak");

    public string Describe() => "PlayerPrefs key " + _key;
}

/// <summary>
/// A file in a directory: write to .tmp, flush to disk, then swap it in so a crash leaves either the old or the new save,
/// and keep the previous one as .bak.
/// </summary>
public sealed class SaveFileStore : ISaveStore
{
    private readonly string _dir, _main, _tmp, _bak, _unreadable;

    public SaveFileStore(string directory, string fileName)
    {
        _dir = directory;
        _main = Path.Combine(directory, fileName);
        _tmp = _main + ".tmp";
        _bak = _main + ".bak";
        _unreadable = Path.Combine(directory, Path.GetFileNameWithoutExtension(fileName) + ".unreadable" + Path.GetExtension(fileName));
    }

    public string MainPath => _main;
    public string BackupPath => _bak;
    public string TempPath => _tmp;
    public string UnreadablePath => _unreadable;

    public string Read() => File.Exists(_main) ? File.ReadAllText(_main, Encoding.UTF8) : "";

    public void Write(string json)
    {
        Directory.CreateDirectory(_dir);
        using (FileStream fs = new FileStream(_tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);
        }

        if (File.Exists(_main))
        {
            try { File.Replace(_tmp, _main, _bak); }
            catch (Exception e) when (e is PlatformNotSupportedException || e is IOException || e is UnauthorizedAccessException)
            {
                // A sync tool / antivirus may hold the file, or the platform lacks Replace: the slower three-step swap.
                File.Copy(_main, _bak, true);
                File.Delete(_main);
                File.Move(_tmp, _main);
            }
        }
        else
        {
            File.Move(_tmp, _main);
        }
    }

    public void Delete()
    {
        if (File.Exists(_main)) File.Delete(_main);
        if (File.Exists(_bak)) File.Delete(_bak);
        if (File.Exists(_tmp)) File.Delete(_tmp);
    }

    public bool TryReadBackup(out string json)
    {
        json = File.Exists(_bak) ? File.ReadAllText(_bak, Encoding.UTF8) : "";
        return !string.IsNullOrEmpty(json);
    }

    public void SetAside(string tag)
    {
        if (!File.Exists(_main)) return;
        if (File.Exists(_unreadable)) File.Delete(_unreadable);
        File.Move(_main, _unreadable);
        Debug.LogWarning($"SaveSystem: set the save aside as {_unreadable} ({tag}). It is kept, not deleted.");
    }

    public void SetAsideBackup()
    {
        if (!File.Exists(_bak)) return;
        string target = _unreadable + ".bak";
        if (File.Exists(target)) File.Delete(target);
        File.Move(_bak, target);
    }

    public bool IsEmpty() => !File.Exists(_main) && !File.Exists(_bak);

    public string Describe() => _main;
}
