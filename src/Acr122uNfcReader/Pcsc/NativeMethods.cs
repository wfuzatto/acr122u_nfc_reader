using System.Runtime.InteropServices;
using System.Text;

namespace Acr122uNfcReader.Pcsc;

internal static class NativeMethods
{
    public const uint SCARD_SCOPE_USER = 0x0000;
    public const uint SCARD_SHARE_SHARED = 0x0002;
    public const uint SCARD_PROTOCOL_T0 = 0x0001;
    public const uint SCARD_PROTOCOL_T1 = 0x0002;
    public const uint SCARD_LEAVE_CARD = 0x0000;

    public const int SCARD_S_SUCCESS = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SCARD_IO_REQUEST
    {
        public uint dwProtocol;
        public uint cbPciLength;
    }

    [DllImport("winscard.dll")]
    internal static extern int SCardEstablishContext(
        uint dwScope,
        IntPtr pvReserved1,
        IntPtr pvReserved2,
        out IntPtr phContext);

    [DllImport("winscard.dll")]
    internal static extern int SCardReleaseContext(IntPtr hContext);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode, EntryPoint = "SCardListReadersW")]
    internal static extern int SCardListReaders(
        IntPtr hContext,
        string? mszGroups,
        [Out] char[]? mszReaders,
        ref uint pcchReaders);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode, EntryPoint = "SCardConnectW")]
    internal static extern int SCardConnect(
        IntPtr hContext,
        string szReader,
        uint dwShareMode,
        uint dwPreferredProtocols,
        out IntPtr phCard,
        out uint pdwActiveProtocol);

    [DllImport("winscard.dll")]
    internal static extern int SCardDisconnect(IntPtr hCard, uint dwDisposition);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode, EntryPoint = "SCardStatusW")]
    internal static extern int SCardStatus(
        IntPtr hCard,
        StringBuilder szReaderName,
        ref uint pcchReaderLen,
        out uint pdwState,
        out uint pdwProtocol,
        [Out] byte[] pbAtr,
        ref uint pcbAtrLen);

    [DllImport("winscard.dll")]
    internal static extern int SCardTransmit(
        IntPtr hCard,
        ref SCARD_IO_REQUEST pioSendPci,
        byte[] pbSendBuffer,
        uint cbSendLength,
        IntPtr pioRecvPci,
        [Out] byte[] pbRecvBuffer,
        ref uint pcbRecvLength);
}
