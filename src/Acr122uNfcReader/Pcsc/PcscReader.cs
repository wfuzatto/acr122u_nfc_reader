using System.Runtime.InteropServices;
using System.Text;

namespace Acr122uNfcReader.Pcsc;

public sealed record PcscCardStatus(string ReaderName, uint State, uint Protocol, byte[] Atr);

public sealed class PcscException : Exception
{
    public int ErrorCode { get; }

    public PcscException(string operation, int errorCode)
        : base($"{operation} falhou. Código PC/SC: 0x{unchecked((uint)errorCode):X8}")
    {
        ErrorCode = errorCode;
    }
}

public sealed class PcscReader : IDisposable
{
    private IntPtr _context;
    private IntPtr _card;
    private uint _activeProtocol;

    public string? ConnectedReader { get; private set; }
    public bool IsConnected => _card != IntPtr.Zero;
    public uint ActiveProtocol => _activeProtocol;

    public PcscReader()
    {
        EnsureContext();
    }

    private void EnsureContext()
    {
        if (_context != IntPtr.Zero)
            return;

        int rc = NativeMethods.SCardEstablishContext(
            NativeMethods.SCARD_SCOPE_USER,
            IntPtr.Zero,
            IntPtr.Zero,
            out _context);

        if (rc != NativeMethods.SCARD_S_SUCCESS)
            throw new PcscException("SCardEstablishContext", rc);
    }

    public IReadOnlyList<string> ListReaders()
    {
        EnsureContext();

        uint length = 0;
        int rc = NativeMethods.SCardListReaders(_context, null, null, ref length);

        if (rc != NativeMethods.SCARD_S_SUCCESS || length == 0)
            return Array.Empty<string>();

        var buffer = new char[length];
        rc = NativeMethods.SCardListReaders(_context, null, buffer, ref length);

        if (rc != NativeMethods.SCARD_S_SUCCESS)
            throw new PcscException("SCardListReaders", rc);

        return new string(buffer)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Connect(string readerName)
    {
        Disconnect();
        EnsureContext();

        int rc = NativeMethods.SCardConnect(
            _context,
            readerName,
            NativeMethods.SCARD_SHARE_SHARED,
            NativeMethods.SCARD_PROTOCOL_T0 | NativeMethods.SCARD_PROTOCOL_T1,
            out _card,
            out _activeProtocol);

        if (rc != NativeMethods.SCARD_S_SUCCESS)
        {
            _card = IntPtr.Zero;
            _activeProtocol = 0;
            throw new PcscException("SCardConnect", rc);
        }

        ConnectedReader = readerName;
    }

    public PcscCardStatus GetStatus()
    {
        EnsureConnected();

        var name = new StringBuilder(512);
        uint nameLength = (uint)name.Capacity;
        var atr = new byte[64];
        uint atrLength = (uint)atr.Length;

        int rc = NativeMethods.SCardStatus(
            _card,
            name,
            ref nameLength,
            out uint state,
            out uint protocol,
            atr,
            ref atrLength);

        if (rc != NativeMethods.SCARD_S_SUCCESS)
            throw new PcscException("SCardStatus", rc);

        return new PcscCardStatus(
            name.ToString(),
            state,
            protocol,
            atr.Take((int)atrLength).ToArray());
    }

    public byte[] Transmit(ReadOnlySpan<byte> apdu)
    {
        EnsureConnected();

        byte[] send = apdu.ToArray();
        byte[] receive = new byte[4096];
        uint receiveLength = (uint)receive.Length;

        var pci = new NativeMethods.SCARD_IO_REQUEST
        {
            dwProtocol = _activeProtocol,
            cbPciLength = (uint)Marshal.SizeOf<NativeMethods.SCARD_IO_REQUEST>()
        };

        int rc = NativeMethods.SCardTransmit(
            _card,
            ref pci,
            send,
            (uint)send.Length,
            IntPtr.Zero,
            receive,
            ref receiveLength);

        if (rc != NativeMethods.SCARD_S_SUCCESS)
            throw new PcscException("SCardTransmit", rc);

        return receive.Take((int)receiveLength).ToArray();
    }

    public void Disconnect()
    {
        if (_card != IntPtr.Zero)
        {
            NativeMethods.SCardDisconnect(_card, NativeMethods.SCARD_LEAVE_CARD);
            _card = IntPtr.Zero;
        }

        _activeProtocol = 0;
        ConnectedReader = null;
    }

    private void EnsureConnected()
    {
        if (_card == IntPtr.Zero)
            throw new InvalidOperationException("Nenhum cartão conectado.");
    }

    public void Dispose()
    {
        Disconnect();

        if (_context != IntPtr.Zero)
        {
            NativeMethods.SCardReleaseContext(_context);
            _context = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }
}
