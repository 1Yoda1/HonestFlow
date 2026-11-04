using System;
using System.IO;
using System.Text;

namespace HonestFlow.Infrastructure;

/// <summary>Bounded, incremental UTF-8 session-log view. Does not retain an open file handle.</summary>
public sealed class LogFileTailReader
{
    public const int DefaultMaxCharacters = 128 * 1024;
    private readonly int _maxCharacters;
    private readonly byte[] _bytes = new byte[4096];
    private readonly char[] _characters = new char[Encoding.UTF8.GetMaxCharCount(4096)];
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _tail = new();
    private string _path;
    private long _position;
    private DateTime _lastWriteUtc;
    private bool _truncated;

    public LogFileTailReader(int maxCharacters = DefaultMaxCharacters)
    {
        if (maxCharacters < 2) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        _maxCharacters = maxCharacters;
    }

    // Null means that the caller can keep its existing TextBox content.
    public string ReadChanges(string path, bool force = false)
    {
        var file = new FileInfo(path);
        long length = file.Length;
        DateTime lastWriteUtc = file.LastWriteTimeUtc;
        bool reset = !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase) ||
                     length < _position || (length == _position && lastWriteUtc != _lastWriteUtc);
        if (!reset && length == _position)
            return force ? GetText() : null;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        length = stream.Length;
        if (reset || length < _position)
        {
            Reset();
            _path = path;
        }

        // A busy file can grow faster than the view is refreshed. Never scan its full history.
        long maxReadBytes = (long)_maxCharacters * 4;
        if (length - _position > maxReadBytes)
        {
            _tail.Clear();
            _decoder.Reset();
            _position = length - maxReadBytes;
            _truncated = true;
            stream.Position = _position;
            // Skip an incomplete UTF-8 sequence at the beginning of the selected tail.
            for (int i = 0; i < 3; i++)
            {
                int value = stream.ReadByte();
                if (value < 0 || (value & 0xc0) != 0x80) break;
                _position++;
            }
        }

        stream.Position = _position;
        while (_position < length)
        {
            int read = stream.Read(_bytes, 0, (int)Math.Min(_bytes.Length, length - _position));
            if (read == 0) break;
            int count = _decoder.GetChars(_bytes, 0, read, _characters, 0, flush: false);
            int start = _position == 0 && count > 0 && _characters[0] == '\ufeff' ? 1 : 0;
            _tail.Append(_characters, start, count - start);
            _position += read;
            if (_tail.Length > _maxCharacters)
            {
                int remove = _tail.Length - _maxCharacters;
                if (char.IsLowSurrogate(_tail[remove])) remove++;
                _tail.Remove(0, remove);
                _truncated = true;
            }
        }
        _lastWriteUtc = lastWriteUtc;
        return GetText();
    }

    public void Reset()
    {
        _path = null;
        _position = 0;
        _lastWriteUtc = default;
        _truncated = false;
        _tail.Clear();
        _decoder.Reset();
    }

    private string GetText() => _truncated
        ? "Показана последняя часть журнала. Полная история сохранена в файле журнала." + Environment.NewLine + _tail
        : _tail.ToString();
}
