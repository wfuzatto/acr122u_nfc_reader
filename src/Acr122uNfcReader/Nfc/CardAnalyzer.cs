using System.Globalization;
using System.Text;

namespace Acr122uNfcReader.Nfc;

public static class CardAnalyzer
{
    public static readonly string[] CommonClassicKeys =
    [
        "FFFFFFFFFFFF",
        "A0A1A2A3A4A5",
        "D3F7D3F7D3F7",
        "000000000000",
        "B0B1B2B3B4B5",
        "4D3A99C351DD",
        "1A982C7E459A",
        "AABBCCDDEEFF"
    ];

    public static string ToHex(IEnumerable<byte> data, bool spaces = true)
        => string.Join(spaces ? " " : "", data.Select(b => b.ToString("X2")));

    public static byte[] ParseHex(string text)
    {
        string cleaned = new(text.Where(Uri.IsHexDigit).ToArray());

        if (cleaned.Length == 0 || cleaned.Length % 2 != 0)
            throw new FormatException("HEX inválido. Informe pares de dígitos hexadecimais.");

        var result = new byte[cleaned.Length / 2];

        for (int i = 0; i < result.Length; i++)
            result[i] = byte.Parse(cleaned.Substring(i * 2, 2), NumberStyles.HexNumber);

        return result;
    }

    public static string ToAscii(IEnumerable<byte> data)
    {
        var sb = new StringBuilder();
        foreach (byte b in data)
            sb.Append(b is >= 32 and <= 126 ? (char)b : '.');
        return sb.ToString();
    }

    public static string ProtocolName(uint protocol) => protocol switch
    {
        0x0001 => "T=0",
        0x0002 => "T=1",
        _ => $"0x{protocol:X8}"
    };

    public static string GuessCardType(byte[] atr)
    {
        string h = ToHex(atr, false);

        if (h.Contains("A000000306030001", StringComparison.OrdinalIgnoreCase))
            return "MIFARE Classic 1K (heurística ATR)";
        if (h.Contains("A000000306030002", StringComparison.OrdinalIgnoreCase))
            return "MIFARE Classic 4K (heurística ATR)";
        if (h.Contains("A000000306030003", StringComparison.OrdinalIgnoreCase))
            return "MIFARE Ultralight / Type 2 (heurística ATR)";
        if (h.Contains("A000000306", StringComparison.OrdinalIgnoreCase))
            return "PICC / cartão de memória PC/SC (tipo não determinado)";
        if (atr.Length > 0)
            return "ISO 14443 / cartão contactless (tipo não determinado)";

        return "Desconhecido";
    }

    public static string UidAsDecimal(byte[] uid)
    {
        if (uid.Length is 0 or > 8)
            return "n/a";

        ulong value = 0;
        foreach (byte b in uid)
            value = (value << 8) | b;

        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static int SectorForBlock(int block)
    {
        if (block < 0 || block > 255)
            return -1;

        return block < 128 ? block / 4 : 32 + ((block - 128) / 16);
    }

    public static int FirstBlockOfSector(int sector)
    {
        if (sector < 0 || sector > 39)
            throw new ArgumentOutOfRangeException(nameof(sector));

        return sector < 32 ? sector * 4 : 128 + ((sector - 32) * 16);
    }

    public static int BlocksInSector(int sector)
    {
        if (sector < 0 || sector > 39)
            throw new ArgumentOutOfRangeException(nameof(sector));

        return sector < 32 ? 4 : 16;
    }

    public static bool IsSectorTrailer(int block)
    {
        int sector = SectorForBlock(block);
        if (sector < 0)
            return false;

        return block == FirstBlockOfSector(sector) + BlocksInSector(sector) - 1;
    }

    public static string BlockKind(int block)
    {
        if (block == 0)
            return "Fabricante / UID";
        if (IsSectorTrailer(block))
            return "Sector Trailer";
        return "Dados";
    }

    public static string DecodeAccessBits(byte[] block)
    {
        if (block.Length < 10)
            return "";

        byte b6 = block[6];
        byte b7 = block[7];
        byte b8 = block[8];

        var parts = new List<string>();
        bool redundancyOk = true;

        for (int i = 0; i < 4; i++)
        {
            int c1 = (b7 >> (4 + i)) & 1;
            int c2 = (b8 >> i) & 1;
            int c3 = (b8 >> (4 + i)) & 1;

            int nc1 = (b6 >> i) & 1;
            int nc2 = (b6 >> (4 + i)) & 1;
            int nc3 = (b7 >> i) & 1;

            if (nc1 != (c1 ^ 1) || nc2 != (c2 ^ 1) || nc3 != (c3 ^ 1))
                redundancyOk = false;

            string label = i == 3 ? "trailer" : $"grupo{i}";
            parts.Add($"{label}={c1}{c2}{c3}");
        }

        return $"{string.Join(" ", parts)}; redundância={(redundancyOk ? "OK" : "INVÁLIDA")}; GPB={block[9]:X2}";
    }
}
