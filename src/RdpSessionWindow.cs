using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MultiRdp
{
    [ComImport, Guid("B3378D90-0728-45C7-8ED7-B6159FB92219")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IRdpNonScriptable
    {
        // Skip the inherited methods preceding these properties in IMsRdpClientNonScriptable3.
        void _VtblGap1_14();
        void SetShowRedirectionWarningDialog([MarshalAs(UnmanagedType.VariantBool)] bool show);
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetShowRedirectionWarningDialog();
        void SetPromptForCredentials([MarshalAs(UnmanagedType.VariantBool)] bool prompt);
    }

    internal sealed class RdpControl : AxHost
    {
        public RdpControl() : base("A0C63C30-F08D-4AB4-907C-34905D770C7D")
        {
            Dock = DockStyle.Fill;
        }

        public object CreateClient()
        {
            if (Handle == IntPtr.Zero)
                throw new InvalidOperationException("Не удалось создать окно клиента RDP.");
            return GetOcx();
        }
    }

    internal sealed class RdpSessionWindow : Form
    {
        private static readonly Guid EventsInterface = new Guid("336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6");
        private const int LoginCompleteEvent = 3;
        private const int DisconnectedEvent = 4;
        private const int AutoReconnectedEvent = 33;
        private const int AutoReconnectingEvent = 34;
        private readonly RdpControl control;
        private readonly Action<SessionState> reportState;
        private bool closing;

        public RdpSessionWindow(Profile profile, Icon icon, Action<SessionState> reportState)
        {
            string server;
            ushort port;
            if (!RdpClient.TryParseAddress(profile.Address, out server, out port) ||
                string.IsNullOrWhiteSpace(profile.UserName))
                throw new InvalidDataException("Проверьте адрес сервера и имя пользователя в профиле.");

            this.reportState = reportState;
            Text = profile.Name + " — MultiRDP";
            Icon = icon;
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            ClientSize = new Size(1024, 720);
            AutoScaleMode = AutoScaleMode.Dpi;

            control = new RdpControl();
            try
            {
                Controls.Add(control);
                dynamic client = control.CreateClient();
                dynamic advanced = client.AdvancedSettings9;
                var native = (IRdpNonScriptable)client;

                int domainSeparator = profile.UserName.IndexOf('\\');
                client.Domain = domainSeparator < 0 ? "" : profile.UserName.Substring(0, domainSeparator);
                client.UserName = profile.UserName.Substring(domainSeparator + 1);
                client.Server = server;
                advanced.RDPPort = (int)port;
                advanced.EnableCredSspSupport = true;
                advanced.AuthenticationLevel = 2u;
                advanced.SmartSizing = true;
                advanced.RedirectSmartCards = profile.SmartCards;
                advanced.RedirectDrives = profile.Drives;
                advanced.RedirectClipboard = profile.Clipboard;
                advanced.RedirectPrinters = profile.Printers;
                advanced.RedirectPorts = false;
                advanced.RedirectDevices = false;
                advanced.RedirectPOSDevices = false;
                advanced.AudioCaptureRedirectionMode = false;
                if (profile.WebAuthn)
                    advanced.PluginDlls = Path.Combine(Environment.SystemDirectory, "webauthn.dll");

                bool prompt = string.IsNullOrEmpty(profile.ProtectedPassword);
                native.SetPromptForCredentials(prompt);
                if (!prompt)
                    advanced.ClearTextPassword = profile.ReadPassword();

                // These flags were selected in the profile. Server certificate validation is independent.
                native.SetShowRedirectionWarningDialog(false);
                object eventSource = client;
                ComEventsHelper.Combine(eventSource, EventsInterface, LoginCompleteEvent,
                    new Action(() => reportState(SessionState.Connected)));
                ComEventsHelper.Combine(eventSource, EventsInterface, AutoReconnectedEvent,
                    new Action(() => reportState(SessionState.Connected)));
                ComEventsHelper.Combine(eventSource, EventsInterface, AutoReconnectingEvent,
                    new Action<int, bool, int, int>((reason, network, attempt, limit) => reportState(SessionState.Reconnecting)));
                ComEventsHelper.Combine(eventSource, EventsInterface, DisconnectedEvent,
                    new Action<int>(OnDisconnected));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                dynamic client = control.GetOcx();
                client.DesktopWidth = control.ClientSize.Width;
                client.DesktopHeight = control.ClientSize.Height;
                client.Connect();
            }
            catch (Exception error)
            {
                reportState(SessionState.Disconnected);
                Ui.Error(this, "Не удалось открыть подключение. " + error.Message);
                Close();
            }
        }

        private void OnDisconnected(int reason)
        {
            if (closing || !IsHandleCreated)
                return;

            reportState(SessionState.Disconnected);
            BeginInvoke(new Action(delegate
            {
                if (closing)
                    return;
                if (reason > 3)
                {
                    dynamic client = control.GetOcx();
                    string message = client.GetErrorDescription((uint)reason, (uint)client.ExtendedDisconnectReason);
                    if (!string.IsNullOrWhiteSpace(message))
                        Ui.Error(this, message);
                }
                Close();
            }));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            closing = !e.Cancel;
        }
    }
}
