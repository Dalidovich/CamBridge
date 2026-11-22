using System.Collections;
using System.Text;
using QRCoder;

namespace CamBridge.Terminal;

public static class QrConsoleRenderer
{
    private const char FullBlock = '█';
    private const char UpperHalfBlock = '▀';
    private const char LowerHalfBlock = '▄';
    private const char Empty = ' ';

    public static IReadOnlyList<string> Render(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.L);
        var matrix = data.ModuleMatrix;
        var size = matrix.Count;
        var lines = new List<string>((size + 1) / 2);

        for (var row = 0; row < size; row += 2)
        {
            var upper = matrix[row];
            var lower = row + 1 < size ? matrix[row + 1] : null;
            var line = new StringBuilder(size);
            for (var column = 0; column < size; column++)
            {
                line.Append(ToHalfBlock(IsLight(upper, column), lower is null || IsLight(lower, column)));
            }

            lines.Add(line.ToString());
        }

        return lines;
    }

    private static bool IsLight(BitArray row, int column) => !row[column];

    private static char ToHalfBlock(bool upperLight, bool lowerLight) => (upperLight, lowerLight) switch
    {
        (true, true) => FullBlock,
        (true, false) => UpperHalfBlock,
        (false, true) => LowerHalfBlock,
        _ => Empty
    };
}
