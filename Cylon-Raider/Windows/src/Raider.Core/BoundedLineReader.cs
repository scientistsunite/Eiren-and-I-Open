using System.Text;
namespace Raider.Core;

// Retains at most maxCharacters plus one small read buffer, including a hostile or damaged frame.
public sealed class BoundedLineReader(TextReader reader, int maxCharacters)
{
    readonly char[] buffer = new char[4096];
    int position, count;

    public async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        if (maxCharacters < 1) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        var text = new StringBuilder(Math.Min(maxCharacters, 4096));
        while (true)
        {
            if (position == count)
            {
                count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                position = 0;
                if (count == 0)
                {
                    if (text.Length != 0) throw new IOException("Truncated collector frame");
                    return null;
                }
            }
            char value = buffer[position++];
            if (value == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length >= maxCharacters) throw new IOException("Collector frame exceeds size limit");
            text.Append(value);
        }
    }
}
