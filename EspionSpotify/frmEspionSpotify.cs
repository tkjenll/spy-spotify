using EspionSpotify.AudioSessions;
using EspionSpotify.Controls;
using EspionSpotify.Drivers;
using EspionSpotify.Enums;
using EspionSpotify.Extensions;
using EspionSpotify.MediaTags;
using EspionSpotify.Models;
using EspionSpotify.Properties;
using MetroFramework;
using MetroFramework.Forms;
using NAudio.Lame;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EspionSpotify
{
    public sealed partial class FrmEspionSpotify : MetroForm, IFrmEspionSpotify
    {
        private readonly IMainAudioSession _audioSession;
        private Watcher _watcher;
        private readonly UserSettings _userSettings;
        private readonly Analytics _analytics;
        private bool _toggleStopRecordingDelayed;
        private FrmSpotifyAPICredentials _frmSpotifyApiCredentials;

        private readonly TranslationKeys[] _recorderStatusTranslationKeys = new[] {
                I18nKeys.LogRecording,
                I18nKeys.LogRecorded,
                I18nKeys.LogDeleting,
                I18nKeys.LogTrackExists,
            };

        public ResourceManager Rm { get; private set; }
        public static FrmEspionSpotify Instance { get; private set; }

        private string LogDate { get => $@"[{DateTime.Now:HH:mm:ss}] "; }

        public FrmEspionSpotify()
        {
            InitializeComponent();
            SuspendLayout();

            _audioSession = new MainAudioSession(Settings.Default.AudioEndPointDeviceID);

            _userSettings = new UserSettings();

            if (string.IsNullOrEmpty(Settings.Default.Directory))
            {
                Settings.Default.Directory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                Settings.Default.Save();
            }

            if (string.IsNullOrEmpty(Settings.Default.AnalyticsCID))
            {
                Settings.Default.AnalyticsCID = Analytics.GenerateCID();
                Settings.Default.Save();
            }

            _analytics = new Analytics(Settings.Default.AnalyticsCID, Assembly.GetExecutingAssembly().GetName().Version.ToString());

            Instance = this;
            ResumeLayout();

            Init();

            Task.Run(async () =>
            {
                await _analytics.LogAction("launch");
                await GitHub.GetVersion();
            });
        }

        public void SetSoundVolume(int volume)
        {
            tbVolumeWin.SetPropertyThreadSafe(() => tbVolumeWin.Value = volume);
        }

        public void Init()
        {
            tcMenu.SelectedIndex = Settings.Default.TabNo;

            chkRecordDuplicateRecordings.Enabled = Settings.Default.RecordOverRecordingsEnabled;
            chkRecordAds.Enabled = Settings.Default.RecordEverythingEnabled;

            rbMp3.Checked = Settings.Default.MediaFormat == (int)MediaFormat.Mp3;
            rbWav.Checked = Settings.Default.MediaFormat == (int)MediaFormat.Wav;
            tbMinTime.Value = Settings.Default.MinimumRecordedLengthSeconds / 5;
            tgEndingSongDelay.Checked = Settings.Default.EndingSongDelayEnabled;
            tgAddSeparators.Checked = Settings.Default.TrackTitleSeparatorEnabled;
            tgNumTracks.Checked = Settings.Default.OrderNumberInMediaTagEnabled;
            tgNumFiles.Checked = Settings.Default.OrderNumberInfrontOfFileEnabled;
            tgAddFolders.Checked = Settings.Default.GroupByFoldersEnabled;
            txtPath.Text = Settings.Default.Directory;
            tgMuteAds.Checked = Settings.Default.MuteAdsEnabled;
            tgRecordOverRecordings.Checked = Settings.Default.RecordOverRecordingsEnabled;
            tgExtraTitleToSubtitle.Checked = Settings.Default.ExtraTitleToSubtitleEnabled;
            chkRecordDuplicateRecordings.Checked = Settings.Default.RecordDuplicateRecordingsEnabled;
            tgRecordEverything.Checked = Settings.Default.RecordEverythingEnabled;
            chkRecordAds.Checked = Settings.Default.RecordAdsEnabled;
            folderBrowserDialog.SelectedPath = Settings.Default.Directory;
            txtRecordingNum.Mask = Settings.Default.OrderNumberMask;

            SetSpotifyAPIOption();

            rbLastFMAPI.Checked = Settings.Default.MediaTagsAPI == (int)MediaTagsAPI.LastFM || !_userSettings.IsSpotifyAPISet;
            rbSpotifyAPI.Checked = Settings.Default.MediaTagsAPI == (int)MediaTagsAPI.Spotify && _userSettings.IsSpotifyAPISet;
            if (rbSpotifyAPI.Checked)
            {
                SetMediaTagsAPI(MediaTagsAPI.Spotify, _userSettings.IsSpotifyAPISet);
            }

#if DEBUG
            Style = MetroColorStyle.Orange;
#endif

            SetLanguageDropDown();  // do it before setting the language
            SetLanguage(); // creates Rm and trigger fields event which requires audioSession

            UpdateAudioEndPointFields(_audioSession.AudioDeviceVolume, _audioSession.AudioMMDevicesManager.AudioEndPointDeviceName);
            SetAudioEndPointDevicesDropDown(); // affects data source which requires Rm and audioSession
            UpdateAudioVirtualCableDriverImage();

            _userSettings.AudioEndPointDeviceID = _audioSession.AudioMMDevicesManager.AudioEndPointDeviceID;
            _userSettings.Bitrate = cbBitRate.SelectedItem.ToKeyValuePair<LAMEPreset, string>().Key;
            _userSettings.RecordRecordingsStatus = Settings.Default.GetRecordRecordingsStatus();
            _userSettings.EndingTrackDelayEnabled = Settings.Default.EndingSongDelayEnabled;
            _userSettings.GroupByFoldersEnabled = Settings.Default.GroupByFoldersEnabled;
            _userSettings.MediaFormat = (MediaFormat)Settings.Default.MediaFormat;
            _userSettings.MinimumRecordedLengthSeconds = Settings.Default.MinimumRecordedLengthSeconds;
            _userSettings.OrderNumberInfrontOfFileEnabled = Settings.Default.OrderNumberInfrontOfFileEnabled;
            _userSettings.OrderNumberInMediaTagEnabled = Settings.Default.OrderNumberInMediaTagEnabled;
            _userSettings.OutputPath = Settings.Default.Directory;
            _userSettings.RecordEverythingEnabled = Settings.Default.RecordEverythingEnabled;
            _userSettings.RecordAdsEnabled = Settings.Default.RecordAdsEnabled;
            _userSettings.MuteAdsEnabled = Settings.Default.MuteAdsEnabled;
            _userSettings.TrackTitleSeparator = Settings.Default.TrackTitleSeparatorEnabled ? "_" : " ";
            _userSettings.OrderNumberMask = Settings.Default.OrderNumberMask;
            _userSettings.ExtraTitleToSubtitleEnabled = Settings.Default.ExtraTitleToSubtitleEnabled;

            txtRecordingNum.Text = _userSettings.InternalOrderNumber.ToString(_userSettings.OrderNumberMask);

            var _logs = Settings.Default.Logs.Split(';').Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            WritePreviousLogsIntoConsole(_logs);

            var lastVersionPrompted = Settings.Default.LastVersionPrompted.ToVersion();
            lnkRelease.Visible = lastVersionPrompted != null && lastVersionPrompted > Assembly.GetExecutingAssembly().GetName().Version;
        }

        private void SetSpotifyAPIOption()
        {
            _userSettings.SpotifyAPIClientId = Settings.Default.SpotifyAPIClientId?.Trim();
            _userSettings.SpotifyAPISecretId = Settings.Default.SpotifyAPISecretId?.Trim();
            rbSpotifyAPI.Enabled = _userSettings.IsSpotifyAPISet;
        }

        private void SetMediaTagsAPI(MediaTagsAPI api, bool isSpotifyAPISet)
        {
            switch (api)
            {
                case MediaTagsAPI.Spotify:
                    if (isSpotifyAPISet)
                    {
                        ExternalAPI.Instance = new MediaTags.SpotifyAPI(_userSettings.SpotifyAPIClientId, _userSettings.SpotifyAPISecretId);
                    }
                    break;
                case MediaTagsAPI.LastFM:
                default:
                    ExternalAPI.Instance = new MediaTags.LastFMAPI();
                    break;
            }
        }

        private void UpdateAudioEndPointFields(int volume, string friendlyName)
        {
            var isSet = friendlyName != null;

            tbVolumeWin.Enabled = isSet;
            tbVolumeWin.Visible = isSet;
            iconVolume.Visible = isSet;
            lblVolume.Visible = isSet;

            lblSoundCard.Text = friendlyName;
            lblVolume.Text = volume.ToString() + @"%";
            tbVolumeWin.Value = volume;
        }

        public void SetAudioEndPointDevicesDropDown()
        {
            var selectedID = _audioSession.IsAudioEndPointDeviceIndexAvailable
                ? _audioSession.AudioMMDevicesManager.AudioEndPointDeviceID
                : _audioSession.AudioMMDevicesManager.DefaultAudioEndPointDeviceID;

            var isAudioDeviceEmpty = _audioSession.AudioMMDevicesManager.AudioEndPointDeviceNames.Count == 0;

            cbAudioDevices.Enabled = _audioSession.AudioMMDevicesManager.AudioEndPointDeviceNames.Count > 1;

            if (isAudioDeviceEmpty)
            {
                cbAudioDevices.DataSource = null;
                cbAudioDevices.SelectedIndex = -1;
            }
            else
            {
                cbAudioDevices.DataSource = new BindingSource(
                    _audioSession.AudioMMDevicesManager.AudioEndPointDeviceNames,
                    null);
                cbAudioDevices.DisplayMember = "Value";
                cbAudioDevices.ValueMember = "Key";
                cbAudioDevices.SelectedValue = selectedID;
            }
        }

        private void SetLanguageDropDown()
        {
            var selectedId = Settings.Default.Language;

            cbLanguage.DataSource = new BindingSource(Translations.Languages.dropdownListValues, null);
            cbLanguage.DisplayMember = "Value";
            cbLanguage.ValueMember = "Key";
            cbLanguage.SelectedIndex = selectedId;
        }

        private void SetLanguage()
        {
            var indexLanguage = Settings.Default.Language;
            var indexBitRate = Settings.Default.Bitrate;

            var languageType = (LanguageType)indexLanguage;

            var rmLanguage = Translations.Languages.getResourcesManagerLanguageType(languageType);
            Rm = new ResourceManager(rmLanguage ?? typeof(Translations.en));

            tabRecord.Text = Rm.GetString(I18nKeys.TabRecord);
            tabSettings.Text = Rm.GetString(I18nKeys.TabSettings);
            tabAdvanced.Text = Rm.GetString(I18nKeys.TabAdvanced);

            folderBrowserDialog.Description = Rm.GetString(I18nKeys.MsgFolderDialog);

            lblPath.Text = Rm.GetString(I18nKeys.LblPath);
            lblAudioDevice.Text = Rm.GetString(I18nKeys.LblAudioDevice);
            lblBitRate.Text = Rm.GetString(I18nKeys.LblBitRate);
            lblFormat.Text = Rm.GetString(I18nKeys.LblFormat);
            lblMinLength.Text = Rm.GetString(I18nKeys.LblMinLength);
            lblLanguage.Text = Rm.GetString(I18nKeys.LblLanguage);
            lblAddFolders.Text = Rm.GetString(I18nKeys.LblAddFolders);
            lblAddSeparators.Text = Rm.GetString(I18nKeys.LblAddSeparators);
            lblNumFiles.Text = Rm.GetString(I18nKeys.LblNumFiles);
            lblNumTracks.Text = Rm.GetString(I18nKeys.LblNumTracks);
            lblEndingSongDelay.Text = Rm.GetString(I18nKeys.LblEndingSongDelay);
            lblRecordingNum.Text = Rm.GetString(I18nKeys.LblRecordingNum);
            lblAds.Text = Rm.GetString(I18nKeys.LblAds);
            lblMuteAds.Text = Rm.GetString(I18nKeys.LblMuteAds);
            lblSpy.Text = Rm.GetString(I18nKeys.LblSpy);
            lblRecorder.Text = Rm.GetString(I18nKeys.LblRecorder);
            lblRecordEverything.Text = Rm.GetString(I18nKeys.LblRecordEverything);
            chkRecordAds.Text = Rm.GetString(I18nKeys.LblRecordAds);
            lblRecordOverRecordings.Text = Rm.GetString(I18nKeys.LblRecordOverRecordings);
            chkRecordDuplicateRecordings.Text = Rm.GetString(I18nKeys.LblDuplicate);
            lblRecordingTimer.Text = Rm.GetString(I18nKeys.LblRecordingTimer);
            lblExtraTitleToSubtitle.Text = Rm.GetString(I18nKeys.LblExtraTitleToSubtitle);
            lblID3.Text = Rm.GetString(I18nKeys.LblID3);

            tip.SetToolTip(lnkClear, Rm.GetString(I18nKeys.TipClear));
            tip.SetToolTip(lnkSpy, Rm.GetString(I18nKeys.TipStartSpying));
            tip.SetToolTip(lnkDirectory, Rm.GetString(I18nKeys.TipDirectory));
            tip.SetToolTip(lnkPath, Rm.GetString(I18nKeys.TipPath));
            tip.SetToolTip(lnkAudioVirtualCable, Rm.GetString(I18nKeys.TipInstallVirtualCableDriver));
            tip.SetToolTip(lnkRelease, Rm.GetString(I18nKeys.TipRelease));
            tip.SetToolTip(lnkDonate, Rm.GetString(I18nKeys.TipDonate));
            tip.SetToolTip(lnkFAQ, Rm.GetString(I18nKeys.TipFAQ));
            tip.SetToolTip(lnkNumPlus, Rm.GetString(I18nKeys.TipNumModifierHold));
            tip.SetToolTip(lnkNumMinus, Rm.GetString(I18nKeys.TipNumModifierHold));
            tip.SetToolTip(lnkSpotifyCredentials, Rm.GetString(I18nKeys.TipSpotifyAPICredentials));

            var bitrates = new Dictionary<LAMEPreset, string>
            {
                {LAMEPreset.ABR_128, Rm.GetString(I18nKeys.CbOptBitRate128)},
                {LAMEPreset.ABR_160, string.Format(Rm.GetString(I18nKeys.CbOptBitRateSpotifyFree) ?? "{0}", Rm.GetString(I18nKeys.CbOptBitRate160))},
                {LAMEPreset.ABR_256, Rm.GetString(I18nKeys.CbOptBitRate256)},
                {LAMEPreset.ABR_320, string.Format(Rm.GetString(I18nKeys.CbOptBitRateSpotifyPremium) ?? "{0}", Rm.GetString(I18nKeys.CbOptBitRate320))}
            };

            cbBitRate.DataSource = new BindingSource(bitrates, null);
            cbBitRate.DisplayMember = "Value";
            cbBitRate.ValueMember = "Key";

            cbBitRate.SelectedIndex = indexBitRate;

            cbLanguage.SelectedIndex = indexLanguage;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                this.Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void UpdateNum(int num)
        {
            txtRecordingNum.SetPropertyThreadSafe(() => txtRecordingNum.Text = num.ToString(txtRecordingNum.Mask));
        }

        public void UpdateNumDown()
        {
            if (!_userSettings.HasOrderNumberEnabled) return;

            _userSettings.InternalOrderNumber--;
            UpdateNum(_userSettings.InternalOrderNumber);
        }

        public void UpdateNumUp()
        {
            if (!_userSettings.HasOrderNumberEnabled) return;

            _userSettings.InternalOrderNumber++;
            UpdateNum(_userSettings.InternalOrderNumber);
        }

        public void UpdateStartButton()
        {
            lnkSpy.SetPropertyThreadSafe(() =>
            {
                tip.SetToolTip(lnkSpy, Rm.GetString(I18nKeys.TipStartSpying));
                lnkSpy.Image = Resources.on;
                lnkSpy.Focus();
            });
        }

        public void UpdateIconSpotify(bool isSpotifyPlaying, bool isRecording = false)
        {
            iconSpotify.SetPropertyThreadSafe(() =>
            {
                if (isRecording)
                {
                    iconSpotify.BackgroundImage = Resources.record;
                    lblPlayingTitle.ForeColor = lblPlayingTitle.ForeColor.SpotifyPrimaryText();
                    Task.Run(async () => await _analytics.LogAction("record"));
                }
                else if (isSpotifyPlaying)
                {
                    iconSpotify.BackgroundImage = Resources.play;
                    lblPlayingTitle.ForeColor = lblPlayingTitle.ForeColor.SpotifySecondaryText();
                    Task.Run(async () => await _analytics.LogAction("play"));
                }
                else
                {
                    iconSpotify.BackgroundImage = Resources.pause;
                    lblPlayingTitle.ForeColor = lblPlayingTitle.ForeColor.SpotifySecondaryText();
                    Task.Run(async () => await _analytics.LogAction("pause"));
                }
            });
        }

        public void UpdatePlayingTitle(string text)
        {
            lblPlayingTitle.SetPropertyThreadSafe(() => lblPlayingTitle.Text = text);
        }

        public void UpdateRecordedTime(int? time)
        {
            lblRecordedTime.SetPropertyThreadSafe(() => lblRecordedTime.Text = time.HasValue ? TimeSpan.FromSeconds(time.Value).ToString(@"mm\:ss") : "");
        }

        public void UpdateMediaTagsAPIToggle(MediaTagsAPI value)
        {
            rbLastFMAPI.SetPropertyThreadSafe(() => rbLastFMAPI.Checked = MediaTagsAPI.LastFM == value);
        }

        public void ShowFailedToUseSpotifyAPIMessage()
        {
            MetroMessageBox.Show(Instance,
               Rm.GetString(I18nKeys.MsgBodyFailedToUseSpotifyAPI),
               Rm.GetString(I18nKeys.MsgTitleFailedToUseSpotifyAPI),
               MessageBoxButtons.OK,
               MessageBoxIcon.Question);
        }

        private string WriteRtbLine(RichTextBox rtbLog, TranslationKeys resource, params object[] args)
        {
            var text = string.Format(Rm.GetString(resource), args);

            if (text == null) return "";

            var timeStr = LogDate;
            var indexOfColon = text.IndexOf(": ");

            rtbLog.AppendText(timeStr);

            if (_recorderStatusTranslationKeys.Contains(resource))
            {
                var type = text.Substring(0, indexOfColon);
                var msg = text.Substring(indexOfColon, text.Length - indexOfColon);

                // set message type
                rtbLog.AppendText(type);
                rtbLog.Select(rtbLog.TextLength - type.Length, type.Length + 1);
                rtbLog.SelectionColor = resource.Equals(I18nKeys.LogRecording)
                    ? rtbLog.SelectionColor.SpotifyPrimaryText()
                    : rtbLog.SelectionColor.SpotifySecondaryText();
                rtbLog.SelectionFont = GetDefaultSelectionFont(FontStyle.Bold);

                // set message msg
                rtbLog.AppendText(msg + Environment.NewLine);
                rtbLog.Select(rtbLog.TextLength - msg.Length, msg.Length);
                rtbLog.SelectionColor = rtbLog.SelectionColor = resource.Equals(I18nKeys.LogDeleting)
                    ? rtbLog.SelectionColor.SpotifySecondaryTextAlternate()
                    : rtbLog.SelectionColor.SpotifySecondaryText();
                rtbLog.SelectionFont = GetDefaultSelectionFont(FontStyle.Regular);
            }
            else
            {
                rtbLog.AppendText(text + Environment.NewLine);
            }

            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.ScrollToCaret();

            return $";{timeStr}{text}";
        }

        public void WriteIntoConsole(TranslationKeys resource, params object[] args)
        {
            rtbLog.SetPropertyThreadSafe(() =>
            {
                var log = WriteRtbLine(rtbLog, resource, args);

                if (!string.IsNullOrEmpty(log))
                {
                    Settings.Default.Logs += $";{log}";
                    Settings.Default.Save();
                }
            });
        }

        private void WritePreviousLogsIntoConsole(string[] logs)
        {
            if (logs.Length == 0) return;

            foreach (var log in logs)
            {
                rtbLog.AppendText(log + Environment.NewLine);
            }

            rtbLog.AppendText(LogDate + Rm.GetString(I18nKeys.LogPreviousLogs) + Environment.NewLine + Environment.NewLine);

            rtbLog.Select(0, rtbLog.TextLength);
            rtbLog.SelectionFont = GetDefaultSelectionFont(FontStyle.Regular);

            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.ScrollToCaret();
        }

        private void StartRecording()
        {
            _watcher = new Watcher(this, _audioSession, _userSettings);

            Task.Run(_watcher.Run);

            tip.SetToolTip(lnkSpy, Rm.GetString(I18nKeys.TipStopSying));
            tlSettings.Enabled = false;
            tlAdvanced.Enabled = false;
            timer1.Start();
        }

        public void StopRecording()
        {
            if (tlSettings.IsInvokeRequired() || tlAdvanced.IsInvokeRequired())
            {
                BeginInvoke(new Action(StopRecording));
                return;
            }

            Watcher.Running = false;
            _toggleStopRecordingDelayed = false;
            timer1.Stop();
            tlSettings.Enabled = true;
            tlAdvanced.Enabled = true;
        }

        private bool DirExists()
        {
            if (Directory.Exists(_userSettings.OutputPath)) return true;

            MetroMessageBox.Show(this,
                Rm.GetString(I18nKeys.MsgBodyPathNotFound),
                Rm.GetString(I18nKeys.MsgTitlePathNotFound),
                MessageBoxButtons.OK,
                MessageBoxIcon.Question);

            return false;
        }

        private void LnkSpy_Click(object sender, EventArgs e)
        {
            if (!Watcher.Running)
            {
                if (!DirExists()) return;

                tcMenu.SelectedIndex = 0;
                StartRecording();
                UpdateLinkImage(lnkSpy, Resources.off);
                Task.Run(async () => await _analytics.LogAction("recording-session?status=started"));
            }
            else if (_watcher.RecorderUpAndRunning && !_toggleStopRecordingDelayed)
            {
                _toggleStopRecordingDelayed = true;
                Watcher.ToggleStopRecordingDelayed = _toggleStopRecordingDelayed;
            }
            else
            {
                StopRecording();
                UpdateLinkImage(lnkSpy, Resources.on);
                Task.Run(async () => await _analytics.LogAction("recording-session?status=ended"));
            }
        }

        private void UpdateLinkImage(MetroFramework.Controls.MetroLink icon, Bitmap bmp)
        {
            if (icon.Image == bmp) return;
            icon.Image.Dispose();
            icon.Image = bmp;
            icon.Refresh();
        }

        private Font GetDefaultSelectionFont(FontStyle style)
        {
            return new Font(
                new FontFamily("Courier New"),
                8.25f,
                style
            );
        }

        private bool UpdateAudioVirtualCableDriverImage()
        {
            // requires audioSession and Rm
            lnkAudioVirtualCable.Visible = lnkAudioVirtualCable.Enabled = AudioVirtualCableDriver.IsFound;
            var isDriverInstalled = AudioVirtualCableDriver.ExistsInAudioEndPointDevices(_audioSession.AudioMMDevicesManager.AudioEndPointDeviceNames);
            var bmp = isDriverInstalled ? Resources.remove_device : Resources.add_device;
            UpdateLinkImage(lnkAudioVirtualCable, bmp);
            var msgTooltip = isDriverInstalled ? I18nKeys.TipUninstallVirtualCableDriver : I18nKeys.TipInstallVirtualCableDriver;
            tip.SetToolTip(lnkAudioVirtualCable, Rm.GetString(msgTooltip));
            return isDriverInstalled;
        }

        private void LnkClear_Click(object sender, EventArgs e)
        {
            rtbLog.Text = "";
            Task.Run(async () => await _analytics.LogAction("clear-console"));
        }

        private void Timer1_Tick(object sender, EventArgs e)
        {
            if (_watcher == null) return;
            _watcher.CountSeconds++;

            if (!Watcher.Running && !Watcher.Ready)
            {
                StopRecording();
            }
        }

        private void RbFormat_CheckedChanged(object sender, EventArgs e)
        {
            var rb = sender as RadioButton;
            var mediaFormatIndex = (int)(rbMp3.Checked ? MediaFormat.Mp3 : MediaFormat.Wav);
            if (Settings.Default.MediaFormat == mediaFormatIndex || !rb.Checked) return;

            var mediaFormat = rb?.Tag?.ToString().ToMediaFormat() ?? MediaFormat.Mp3;
            _userSettings.MediaFormat = mediaFormat;
            tlpAPI.Visible = mediaFormat == MediaFormat.Mp3;
            Settings.Default.MediaFormat = mediaFormatIndex;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction($"media-format?type={mediaFormat.ToString()}"));
        }

        private void RbMediaTagsAPI_CheckedChanged(object sender, EventArgs e)
        {
            var rb = sender as RadioButton;
            var mediaTagsAPI = rbLastFMAPI.Checked ? MediaTagsAPI.LastFM : MediaTagsAPI.Spotify;

            if (Settings.Default.MediaTagsAPI == (int)mediaTagsAPI || !rb.Checked) return;

            var api = rb?.Tag?.ToString().ToMediaTagsAPI() ?? MediaTagsAPI.LastFM;
            SetMediaTagsAPI(api, _userSettings.IsSpotifyAPISet);
            Settings.Default.MediaTagsAPI = (int)mediaTagsAPI;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction($"media-tags-api?type={api.ToString()}"));
        }

        private void TgRecordEverything_CheckedChanged(object sender, EventArgs e)
        {
            if (Settings.Default.RecordEverythingEnabled == tgRecordEverything.Checked) return;

            chkRecordAds.Enabled = tgRecordEverything.Checked;

            _userSettings.RecordEverythingEnabled = tgRecordEverything.Checked;
            Settings.Default.RecordEverythingEnabled = tgRecordEverything.Checked;
            Settings.Default.Save();

            if (tgRecordEverything.Checked)
            {
                tgMuteAds.Checked = false;
            }

            Task.Run(async () => await _analytics.LogAction(
                $"record-everything?enabled={tgRecordEverything.GetPropertyThreadSafe(c => c.Checked)}&including-ads={_userSettings.RecordAdsEnabled}"));
        }

        private void ChkRecordAds_CheckedChanged(object sender, EventArgs e)
        {
            if (Settings.Default.RecordAdsEnabled == chkRecordAds.Checked) return;

            _userSettings.RecordAdsEnabled = chkRecordAds.Checked;
            Settings.Default.RecordAdsEnabled = chkRecordAds.Checked;
            Settings.Default.Save();

            Task.Run(async () => await _analytics.LogAction(
                $"record-everything?enabled={_userSettings.RecordEverythingEnabled}&including-ads={chkRecordAds.GetPropertyThreadSafe(c => c.Checked)}"));
        }

        private void TgMuteAds_CheckedChanged(object sender, EventArgs e)
        {
            if (Settings.Default.MuteAdsEnabled == tgMuteAds.Checked) return;

            _userSettings.MuteAdsEnabled = tgMuteAds.Checked;
            Settings.Default.MuteAdsEnabled = tgMuteAds.Checked;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction($"mute-ads?enabled={tgMuteAds.GetPropertyThreadSafe(c => c.Checked)}"));
        }

        private void TgEndingSongDelay_CheckedChanged(object sender, EventArgs e)
        {
            if (Settings.Default.EndingSongDelayEnabled == tgEndingSongDelay.Checked) return;

            _userSettings.EndingTrackDelayEnabled = tgEndingSongDelay.Checked;
            Settings.Default.EndingSongDelayEnabled = tgEndingSongDelay.Checked;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction($"delay-on-ending-song?enabled={tgEndingSongDelay.GetPropertyThreadSafe(c => c.Checked)}"));
        }

        private void TgAddFolders_CheckedChanged(object sender, EventArgs e)
        {
            if (Settings.Default.GroupByFoldersEnabled == tgAddFolders.Checked) return;

            _userSettings.GroupByFoldersEnabled = tgAddFolders.Checked;
            Settings.Default.GroupByFoldersEnabled = tgAddFolders.Checked;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction($"group-by-folders?enabled={tgAddFolders.GetPropertyThreadSafe(c => c.Checked)}"));
        }

        private void TgAddSeparators_CheckedChanged(object sender, EventArgs e)
        {
            if (Settings.Default.TrackTitleSeparatorEnabled == tgAddSeparators.Checked) return;

            _userSettings.TrackTitleSeparator = tgAddSeparators.Checked ? "_" : " ";
            Settings.Default.TrackTitleSeparatorEnabled = tgAddSeparators.Checked;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction($"track-title-separator?enabled={tgAddSeparators.GetPropertyThreadSafe(c => c.Checked)}"));
        }

        private void TgNumFiles_CheckedChanged(object sender, EventArgs e)
        {
            if (Settings.Default.OrderNumberInfrontOfFileEnabled == tgNumFiles.Checked) return;

            _userSettings.OrderNumberInfrontOfFileEnabled = tgNumFiles.Checked;
            Settings.Default.OrderNumberInfrontOfFileEnabled = tgNumFiles.Checked;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction($"order-number-in-front-of-files?enabled={tgNumFiles.GetPropertyThreadSafe(c => c.Checked)}"));
        }

        private void TgNumTracks_CheckedChanged(object sender, EventArgs e)
        {
            if (Settings.Default.OrderNumberInMediaTagEnabled == tgNumTracks.Checked) return;

            _userSettings.OrderNumberInMediaTagEnabled = tgNumTracks.Checked;
            Settings.Default.OrderNumberInMediaTagEnabled = tgNumTracks.Checked;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction($"order-number-in-media-tags?enabled={tgNumTracks.GetPropertyThreadSafe(c => c.Checked)}"));
        }

        private void TgRecordOverRecordings_CheckedChanged(object sender, EventArgs e)
        {
            if (Settings.Default.RecordOverRecordingsEnabled == tgRecordOverRecordings.Checked) return;

            chkRecordDuplicateRecordings.Enabled = tgRecordOverRecordings.Checked;

            Settings.Default.RecordDuplicateRecordingsEnabled = chkRecordDuplicateRecordings.Checked;
            Settings.Default.RecordOverRecordingsEnabled = tgRecordOverRecordings.Checked;
            Settings.Default.Save();

            _userSettings.RecordRecordingsStatus = Settings.Default.GetRecordRecordingsStatus();

            Task.Run(async () => await _analytics.LogAction($"record-recordings-status?status={_userSettings.RecordRecordingsStatus}&overwrite={tgRecordOverRecordings.GetPropertyThreadSafe(c => c.Checked)}"));
        }

        private void ChkRecordDuplicateRecordings_CheckedChanged(object sender, EventArgs e)
        {
            if (Settings.Default.RecordDuplicateRecordingsEnabled == chkRecordDuplicateRecordings.Checked) return;

            Settings.Default.RecordDuplicateRecordingsEnabled = chkRecordDuplicateRecordings.Checked;
            Settings.Default.Save();

            _userSettings.RecordRecordingsStatus = Settings.Default.GetRecordRecordingsStatus();

            Task.Run(async () => await _analytics.LogAction($"record-recordings-status?status={_userSettings.RecordRecordingsStatus}&duplicate={chkRecordDuplicateRecordings.GetPropertyThreadSafe(c => c.Checked)}"));
        }

        private void FrmEspionSpotify_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (Watcher.Ready || !Watcher.Running) return;

            if (MetroMessageBox.Show(this,
                    Rm.GetString(I18nKeys.MsgBodyCantQuit),
                    Rm.GetString(I18nKeys.MsgTitleCantQuit),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            };

            Watcher.Running = false;
            _watcher.Dispose();
            _audioSession.Dispose();

            Task.Run(async () => await _analytics.LogAction("exit"));

#if !DEBUG
            Environment.Exit(0);
#endif
        }

        private void LnkPath_Click(object sender, EventArgs e)
        {
            folderBrowserDialog.SelectedPath = string.IsNullOrEmpty(txtPath.Text)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                : Path.GetDirectoryName(txtPath.Text);

            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                txtPath.Text = folderBrowserDialog.SelectedPath;
            }
        }

        private void TxtPath_TextChanged(object sender, EventArgs e)
        {
            if (Settings.Default.Directory == txtPath.Text) return;

            _userSettings.OutputPath = txtPath.Text;
            Settings.Default.Directory = txtPath.Text;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction("set-output-folder"));
        }

        private void CbBitRate_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (Settings.Default.Bitrate == cbBitRate.SelectedIndex) return;

            _userSettings.Bitrate = cbBitRate.SelectedItem.ToKeyValuePair<LAMEPreset, string>().Key;
            Settings.Default.Bitrate = cbBitRate.SelectedIndex;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction($"bitrate?selected={cbBitRate.GetPropertyThreadSafe(c => c.SelectedValue)}"));
        }

        private void CbAudioDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selectedDeviceID = cbAudioDevices.SelectedItem.ToKeyValuePair<string, string>().Key;

            if (selectedDeviceID == null || Settings.Default.AudioEndPointDeviceID == selectedDeviceID) return;

            _userSettings.AudioEndPointDeviceID = selectedDeviceID;
            _audioSession.AudioMMDevicesManager.RefreshSelectedDevice(selectedDeviceID);
            UpdateAudioEndPointFields(_audioSession.AudioDeviceVolume, _audioSession.AudioMMDevicesManager.AudioEndPointDeviceName);
            Settings.Default.AudioEndPointDeviceID = selectedDeviceID;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction($"audioEndPointDevice?selected={cbAudioDevices.GetPropertyThreadSafe(c => c.SelectedValue)}"));
        }

        private void LnkNumMinus_Click(object sender, EventArgs e)
        {
            if (ModifierKeys == Keys.Control && txtRecordingNum.Mask.Length > 1)
            {
                txtRecordingNum.Mask = txtRecordingNum.Mask.Substring(1);
                _userSettings.OrderNumberMask = txtRecordingNum.Mask;
                Settings.Default.OrderNumberMask = txtRecordingNum.Mask;
                Settings.Default.Save();
            }
            else if (_userSettings.InternalOrderNumber - 1 >= 0)
            {
                _userSettings.InternalOrderNumber--;
            }

            txtRecordingNum.Text = _userSettings.InternalOrderNumber.ToString(txtRecordingNum.Mask);
        }

        private void LnkNumPlus_Click(object sender, EventArgs e)
        {
            if (ModifierKeys == Keys.Control && txtRecordingNum.Mask.Length < 6)
            {
                txtRecordingNum.Mask = $"{txtRecordingNum.Mask}0";
                _userSettings.OrderNumberMask = txtRecordingNum.Mask;
                Settings.Default.OrderNumberMask = txtRecordingNum.Mask;
                Settings.Default.Save();
            }
            else if (_userSettings.InternalOrderNumber + 1 <= _userSettings.OrderNumberMax)
            {
                _userSettings.InternalOrderNumber++;
            }

            txtRecordingNum.Text = _userSettings.InternalOrderNumber.ToString(txtRecordingNum.Mask);
        }

        private void LnkDirectory_Click(object sender, EventArgs e)
        {
            if (DirExists())
            {
                System.Diagnostics.Process.Start("explorer.exe", txtPath.Text);
            }
            Task.Run(async () => await _analytics.LogAction("open-output-folder"));
        }

        private void TbMinTime_ValueChanged(object sender, EventArgs e)
        {
            var value = tbMinTime.Value * 5;
            _userSettings.MinimumRecordedLengthSeconds = value;

            var min = (_userSettings.MinimumRecordedLengthSeconds / 60);
            var sec = (_userSettings.MinimumRecordedLengthSeconds % 60);
            lblMinTime.Text = min + @":" + sec.ToString("00");

            if (Settings.Default.MinimumRecordedLengthSeconds == value) return;

            Settings.Default.MinimumRecordedLengthSeconds = value;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction($"minimum-media-time?value={value}"));
        }

        private void TbVolumeWin_ValueChanged(object sender, EventArgs e)
        {
            if (_audioSession.AudioMMDevicesManager.AudioEndPointDeviceMute ?? false)
            {
                _audioSession.AudioMMDevicesManager.AudioEndPointDevice.AudioEndpointVolume.Mute = false;
            }

            var volume = tbVolumeWin.Value + tbVolumeWin.Value % 2;
            _audioSession.SetAudioDeviceVolume(volume);
            lblVolume.Text = volume + @"%";

            if (volume == 0)
            {
                if (iconVolume.BackgroundImage != Resources.volmute) iconVolume.BackgroundImage = Resources.volmute;
            }
            else if (volume > 0 && volume < 30)
            {
                if (iconVolume.BackgroundImage != Resources.voldown) iconVolume.BackgroundImage = Resources.voldown;
            }
            else
            {
                if (iconVolume.BackgroundImage != Resources.volup) iconVolume.BackgroundImage = Resources.volup;
            }
        }

        private void Focus_Hover(object sender, EventArgs e)
        {
            var ctrl = (Control)sender;
            ctrl.Focus();
        }

        private void CbLanguage_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbLanguage.SelectedIndex == Settings.Default.Language) return;

            Settings.Default.Language = cbLanguage.SelectedIndex;
            Settings.Default.Save();
            SetLanguage();
            Task.Run(async () => await _analytics.LogAction($"language?selected={cbLanguage.GetPropertyThreadSafe(c => c.SelectedValue)}"));
        }

        private void TcMenu_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (Settings.Default.TabNo == tcMenu.SelectedIndex) return;

            Settings.Default.TabNo = tcMenu.SelectedIndex;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction($"tab?selected={tcMenu.GetPropertyThreadSafe(c => c.SelectedTab.Text)}"));
        }

        private void LnkRelease_Click(object sender, EventArgs e)
        {
            Process.Start(GitHub.REPO_LATEST_RELEASE_URL);
        }

        private void TxtRecordingTimer_Leave(object sender, EventArgs e)
        {
            _userSettings.RecordingTimer = txtRecordingTimer.Text;
        }

        private void TxtRecordingNum_Leave(object sender, EventArgs e)
        {
            _userSettings.InternalOrderNumber = int.Parse(txtRecordingNum.Text);
        }

        private void LnkVAD_Click(object sender, EventArgs e)
        {
            lnkAudioVirtualCable.Enabled = false;
            if (!(lnkAudioVirtualCable.Visible = AudioVirtualCableDriver.IsFound)) return;
            if (!AudioVirtualCableDriver.SetupDriver())
            {
                MetroMessageBox.Show(this,
                    Rm.GetString(I18nKeys.MsgBodyDriverInstallationFailed),
                    "Audio Virtual Driver",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Question);
            }
            lnkAudioVirtualCable.Enabled = true;
        }

        public void UpdateAudioDevicesDataSource()
        {
            cbAudioDevices.SetPropertyThreadSafe(() => SetAudioEndPointDevicesDropDown());
        }

        private void CbAudioDevices_DataSourceChanged(object sender, EventArgs e)
        {
            var isDriverInstalled = UpdateAudioVirtualCableDriverImage();
            UpdateAudioEndPointFields(_audioSession.AudioDeviceVolume, _audioSession.AudioMMDevicesManager.AudioEndPointDeviceName);
            Task.Run(async () => await _analytics.LogAction($"audio-virtual-cable-driver?status={(isDriverInstalled ? "installed" : "uninstalled")}"));
        }

        private void LnkFAQ_Click(object sender, EventArgs e)
        {
            Process.Start(GitHub.WEBSITE_FAQ_URL);
            Task.Run(async () => await _analytics.LogAction($"faq"));
        }

        private void LnkDonate_Click(object sender, EventArgs e)
        {
            Process.Start(GitHub.WEBSITE_DONATE_URL);
            Task.Run(async () => await _analytics.LogAction($"donate"));
        }

        private void LnkSpotifyCredentials_Click(object sender, EventArgs e)
        {
            if (_frmSpotifyApiCredentials == null || _frmSpotifyApiCredentials.IsDisposed)
            {
                _frmSpotifyApiCredentials = new FrmSpotifyAPICredentials(_analytics);
            }

            var result = _frmSpotifyApiCredentials.ShowDialog(this);

            SetSpotifyAPIOption();

            if (result == DialogResult.No)
            {
                rbLastFMAPI.Checked = true;
            }

            _frmSpotifyApiCredentials.Dispose();
        }

        private void TgExtraTitleToSubtitle_CheckedChanged(object sender, EventArgs e)
        {
            if (Settings.Default.ExtraTitleToSubtitleEnabled == tgExtraTitleToSubtitle.Checked) return;

            _userSettings.ExtraTitleToSubtitleEnabled = tgExtraTitleToSubtitle.Checked;
            Settings.Default.ExtraTitleToSubtitleEnabled = tgExtraTitleToSubtitle.Checked;
            Settings.Default.Save();
            Task.Run(async () => await _analytics.LogAction($"move-extra-title-to-subtitle?enabled={tgExtraTitleToSubtitle.GetPropertyThreadSafe(c => c.Checked)}"));
        }
    }
}
