using System.Text;
using Acr122uNfcReader.Pcsc;

namespace Acr122uNfcReader.Nfc;

public enum MifareKeyType : byte
{
    KeyA = 0x60,
    KeyB = 0x61
}

public sealed record ApduResult(byte[] Raw)
{
    public bool HasStatus => Raw.Length >= 2;
    public byte Sw1 => HasStatus ? Raw[^2] : (byte)0;
    public byte Sw2 => HasStatus ? Raw[^1] : (byte)0;
    public bool Success => HasStatus && Sw1 == 0x90 && Sw2 == 0x00;
    public byte[] Data => HasStatus ? Raw[..^2] : Raw;
    public string StatusText => HasStatus ? $"{Sw1:X2} {Sw2:X2}" : "sem SW1/SW2";
}

public sealed class CardCommands(PcscReader reader)
{
    private readonly PcscReader _reader = reader;

    public ApduResult Send(params byte[] apdu)
        => new(_reader.Transmit(apdu));

    public ApduResult GetUid()
        => Send(0xFF, 0xCA, 0x00, 0x00, 0x00);

    public ApduResult GetFirmware()
        => Send(0xFF, 0x00, 0x48, 0x00, 0x00);

    public string GetFirmwareText()
    {
        ApduResult r = GetFirmware();
        if (!r.Success)
            return $"Indisponível (SW={r.StatusText})";

        string text = Encoding.ASCII.GetString(r.Data).Trim('\0', ' ', '\r', '\n');
        return string.IsNullOrWhiteSpace(text) ? CardAnalyzer.ToHex(r.Data) : text;
    }

    public ApduResult LoadKey(byte[] key, byte slot = 0)
    {
        if (key.Length != 6)
            throw new ArgumentException("A chave MIFARE precisa ter 6 bytes.");

        return Send(
            0xFF, 0x82, 0x00, slot, 0x06,
            key[0], key[1], key[2], key[3], key[4], key[5]);
    }

    public ApduResult Authenticate(int block, MifareKeyType keyType, byte slot = 0)
    {
        if (block is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(block));

        return Send(
            0xFF, 0x86, 0x00, 0x00, 0x05,
            0x01, 0x00, (byte)block, (byte)keyType, slot);
    }

    public ApduResult ReadBinary(int blockOrPage, int length)
    {
        if (blockOrPage is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(blockOrPage));
        if (length is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(length));

        return Send(0xFF, 0xB0, 0x00, (byte)blockOrPage, (byte)length);
    }

    public ApduResult UpdateBinary(int blockOrPage, byte[] data)
    {
        if (blockOrPage is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(blockOrPage));
        if (data.Length is not (4 or 16))
            throw new ArgumentException("A gravação deve conter 4 bytes (Ultralight) ou 16 bytes (Classic).");

        var apdu = new byte[5 + data.Length];
        apdu[0] = 0xFF;
        apdu[1] = 0xD6;
        apdu[2] = 0x00;
        apdu[3] = (byte)blockOrPage;
        apdu[4] = (byte)data.Length;
        Buffer.BlockCopy(data, 0, apdu, 5, data.Length);

        return new ApduResult(_reader.Transmit(apdu));
    }
}
