using System.Text;

namespace OddSnap.Helpers;

internal sealed class LimitedTextBuffer(int maxChars)
{
    private readonly int _maxChars = Math.Max(256, maxChars);
    private readonly StringBuilder _buffer = new();

    public void AppendLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return;

        if (_buffer.Length > 0)
            _buffer.AppendLine();
        _buffer.Append(line);

        if (_buffer.Length > _maxChars)
            _buffer.Remove(0, _buffer.Length - _maxChars);
    }

    public override string ToString() => _buffer.ToString();
}
