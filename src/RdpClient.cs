using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace MultiRdp
{
    internal enum SessionState { Disconnected, Connecting, Connected, Reconnecting }

    internal sealed class RdpClient : IDisposable
    {
        private static readonly XmlSerializer SessionProfile = new XmlSerializer(typeof(Profile));
        public const string SessionArgument = "--rdp-session";
        private const uint CloseMessage = 0x0010;
        private delegate bool WindowVisitor(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(WindowVisitor visitor, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll", EntryPoint = "PostMessageW")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        private readonly Process process;
        private int state = (int)SessionState.Connecting;
        private int stateChanged;

        private RdpClient(Process process)
        {
            this.process = process;
            process.OutputDataReceived += ReceiveState;
            process.BeginOutputReadLine();
        }

        public bool HasExited { get { return process.HasExited; } }
        public SessionState State { get { return (SessionState)Volatile.Read(ref state); } }

        private void ReceiveState(object sender, DataReceivedEventArgs e)
        {
            SessionState next;
            if (Enum.TryParse(e.Data, out next) && Enum.IsDefined(typeof(SessionState), next)
                && Interlocked.Exchange(ref state, (int)next) != (int)next)
                Interlocked.Exchange(ref stateChanged, 1);
        }

        public bool TakeStateChange()
        {
            return Interlocked.Exchange(ref stateChanged, 0) != 0;
        }

        public static bool ValidAddress(string address)
        {
            string host;
            ushort port;
            return TryParseAddress(address, out host, out port);
        }

        internal static bool TryParseAddress(string address, out string host, out ushort port)
        {
            host = address;
            port = 3389;
            if (string.IsNullOrEmpty(address) || address.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
                return false;

            string portText = null;
            if (address.StartsWith("[", StringComparison.Ordinal))
            {
                var end = address.IndexOf(']');
                if (end < 0)
                    return false;
                host = address.Substring(1, end - 1);
                IPAddress ip;
                if (!IPAddress.TryParse(host, out ip) || ip.AddressFamily != AddressFamily.InterNetworkV6)
                    return false;
                if (end + 1 < address.Length)
                {
                    if (address[end + 1] != ':')
                        return false;
                    portText = address.Substring(end + 2);
                }
            }
            else
            {
                var separator = address.IndexOf(':');
                if (separator >= 0)
                {
                    host = address.Substring(0, separator);
                    portText = address.Substring(separator + 1);
                }

                var type = Uri.CheckHostName(host);
                if (type != UriHostNameType.Dns && type != UriHostNameType.IPv4)
                    return false;
            }

            return portText == null || (portText.All(c => c >= '0' && c <= '9')
                && ushort.TryParse(portText, out port) && port > 0);
        }

        public static void RunSession()
        {
            Profile profile;
            using (var input = Console.OpenStandardInput())
            using (var reader = System.Xml.XmlReader.Create(input, new System.Xml.XmlReaderSettings
            {
                DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                MaxCharactersInDocument = 65536
            }))
            {
                profile = (Profile)SessionProfile.Deserialize(reader);
            }

            using (var output = Console.OpenStandardOutput())
            {
                try
                {
                    using (var icon = Ui.LoadIcon(SystemInformation.IconSize))
                    using (var window = new RdpSessionWindow(profile, icon, next => ReportState(output, next)))
                        Application.Run(window);
                }
                catch
                {
                    ReportState(output, SessionState.Disconnected);
                    throw;
                }
            }
        }

        private static void ReportState(Stream output, SessionState next)
        {
            var message = Encoding.ASCII.GetBytes(next + "\n");
            try { output.Write(message, 0, message.Length); }
            catch (IOException) { /* Session windows can outlive the main window. */ }
        }

        public static RdpClient Start(Profile profile)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = SessionArgument,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true
            });
            if (process == null)
                throw new InvalidOperationException("Не удалось запустить клиент удалённого рабочего стола.");

            try
            {
                using (var input = process.StandardInput)
                    SessionProfile.Serialize(input.BaseStream, profile);
                return new RdpClient(process);
            }
            catch
            {
                process.Dispose();
                throw;
            }
        }

        public bool Disconnect()
        {
            if (process.HasExited)
                return true;

            try
            {
                process.Refresh();
                if (process.CloseMainWindow() || process.HasExited)
                    return true;

                // The session window may be disabled while a modal dialog is open.
                int targetId = process.Id;
                bool sent = false;
                EnumWindows(delegate(IntPtr window, IntPtr parameter)
                {
                    int ownerId;
                    GetWindowThreadProcessId(window, out ownerId);
                    if (ownerId == targetId && IsWindowVisible(window) && IsWindowEnabled(window))
                        sent = PostMessage(window, CloseMessage, IntPtr.Zero, IntPtr.Zero);
                    return !sent;
                }, IntPtr.Zero);
                return sent || process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return process.HasExited;
            }
        }

        public void Dispose()
        {
            process.Dispose();
        }
    }
}
