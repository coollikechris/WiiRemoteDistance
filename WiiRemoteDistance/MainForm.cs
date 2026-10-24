using System.Drawing.Drawing2D;
using System.Globalization;
using System.Media;
using WiimoteLib;
using RemoteButtonState = WiimoteLib.ButtonState;
using Point = System.Drawing.Point;

namespace WiiRemoteDistance;

internal sealed class MainForm : Form
{
    private const double CameraFieldOfViewDegrees = 33.0;
    private const double CameraHorizontalResolution = 1024.0;

    private readonly Button _connectButton = new();
    private readonly Label _connectionLabel = new();
    private readonly Label _distanceLabel = new();
    private readonly Label _feetLabel = new();
    private readonly Label _readingStatusLabel = new();
    private readonly Label _sensorCountLabel = new();
    private readonly Label _aimingGuideLabel = new();
    private readonly Label _calibrationLabel = new();
    private readonly Label _recordingLabel = new();
    private readonly Label _gestureLabel = new();
    private readonly NumericUpDown _spacingInput = new();
    private readonly NumericUpDown _calibrationInput = new();
    private readonly NumericUpDown _alertDistanceInput = new();
    private readonly CheckBox _alertsEnabled = new();
    private readonly Panel _sensorView = new();
    private readonly Panel _trailView = new();
    private readonly Button _calibrateButton = new();
    private readonly Button _recordButton = new();
    private readonly System.Windows.Forms.Timer _displayTimer = new() { Interval = 50 };
    private readonly object _readingLock = new();
    private readonly Queue<System.Drawing.PointF> _positionTrail = new();

    private readonly List<Wiimote> _wiimotes = new();
    private SensorReading? _latestReading;
    private StreamWriter? _sessionWriter;
    private string _sessionPath = "";
    private DateTime _lastTrailReport = DateTime.MinValue;
    private DateTime _lastCsvReport = DateTime.MinValue;
    private DateTime _lastGestureTime = DateTime.MinValue;
    private DateTime _lastAlertTime = DateTime.MinValue;
    private DateTime _previousAccelTime = DateTime.MinValue;
    private Point3F _previousAccel;
    private string _lastGesture = "Waiting for movement";
    private float _calibrationFactor = 1F;
    private bool _insideAlertDistance;
    private bool _closing;

    public MainForm()
    {
        Text = "Wii Remote Distance";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 620);
        MinimumSize = new Size(860, 600);
        BackColor = Color.FromArgb(12, 17, 30);
        ForeColor = Color.FromArgb(235, 240, 250);
        Font = new Font("Segoe UI", 10F);

        BuildLayout();
        _displayTimer.Tick += (_, _) => RefreshReading();
        FormClosing += (_, _) =>
        {
            _closing = true;
            StopRecording();
            Disconnect();
        };
    }

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(28, 18, 28, 12) };
        var title = new Label
        {
            Text = "Wii Remote Distance",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = Color.White,
            Location = new Point(0, 12)
        };
        var subtitle = new Label
        {
            Text = "Live estimate from the Dolphin Bar's infrared lights",
            AutoSize = true,
            ForeColor = Color.FromArgb(151, 163, 184),
            Location = new Point(2, 51)
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 8, 24, 0),
            ColumnCount = 2,
            RowCount = 3,
            BackColor = BackColor
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 270F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));

        var distanceCard = CreateCard();
        distanceCard.Margin = new Padding(0, 0, 10, 12);
        content.Controls.Add(distanceCard, 0, 0);

        var distanceCaption = CreateLabel("ESTIMATED DISTANCE", 13F, Color.FromArgb(151, 163, 184));
        distanceCaption.Location = new Point(22, 20);
        distanceCard.Controls.Add(distanceCaption);

        _distanceLabel.Text = "--";
        _distanceLabel.Font = new Font("Segoe UI Semibold", 54F);
        _distanceLabel.ForeColor = Color.FromArgb(126, 231, 200);
        _distanceLabel.AutoSize = true;
        _distanceLabel.Location = new Point(18, 47);
        distanceCard.Controls.Add(_distanceLabel);

        var meters = CreateLabel("meters", 15F, Color.FromArgb(151, 163, 184));
        meters.Location = new Point(26, 136);
        distanceCard.Controls.Add(meters);

        _feetLabel.Text = "";
        _feetLabel.AutoSize = true;
        _feetLabel.ForeColor = Color.FromArgb(151, 163, 184);
        _feetLabel.Location = new Point(26, 164);
        distanceCard.Controls.Add(_feetLabel);

        var spacingLabel = CreateLabel("LED spacing", 9F, Color.FromArgb(151, 163, 184));
        spacingLabel.Location = new Point(22, 183);
        distanceCard.Controls.Add(spacingLabel);

        _spacingInput.Minimum = 50;
        _spacingInput.Maximum = 1000;
        _spacingInput.Value = 200;
        _spacingInput.Width = 72;
        _spacingInput.Font = new Font("Segoe UI", 9F);
        _spacingInput.Location = new Point(112, 178);
        _spacingInput.BackColor = Color.FromArgb(28, 36, 53);
        _spacingInput.ForeColor = Color.White;
        distanceCard.Controls.Add(_spacingInput);

        var mmLabel = CreateLabel("mm", 9F, Color.FromArgb(151, 163, 184));
        mmLabel.Location = new Point(190, 183);
        distanceCard.Controls.Add(mmLabel);

        var calibrationCaption = CreateLabel("Calibrate at a known distance", 9F, Color.FromArgb(151, 163, 184));
        calibrationCaption.Location = new Point(22, 207);
        distanceCard.Controls.Add(calibrationCaption);

        _calibrationInput.Minimum = 0.1M;
        _calibrationInput.Maximum = 20M;
        _calibrationInput.DecimalPlaces = 2;
        _calibrationInput.Increment = 0.1M;
        _calibrationInput.Value = 1M;
        _calibrationInput.Width = 78;
        _calibrationInput.Location = new Point(22, 231);
        _calibrationInput.BackColor = Color.FromArgb(28, 36, 53);
        _calibrationInput.ForeColor = Color.White;
        distanceCard.Controls.Add(_calibrationInput);

        var calibrationMeters = CreateLabel("meters", 9F, Color.FromArgb(151, 163, 184));
        calibrationMeters.Location = new Point(106, 236);
        distanceCard.Controls.Add(calibrationMeters);

        ConfigureButton(_calibrateButton, "Calibrate", new Point(168, 227), new Size(105, 34));
        _calibrateButton.Enabled = false;
        _calibrateButton.Click += CalibrateButton_Click;
        distanceCard.Controls.Add(_calibrateButton);

        var resetCalibrationButton = new Button();
        ConfigureButton(resetCalibrationButton, "Reset", new Point(281, 227), new Size(72, 34));
        resetCalibrationButton.BackColor = Color.FromArgb(44, 54, 73);
        resetCalibrationButton.Click += (_, _) =>
        {
            _calibrationFactor = 1F;
            _calibrationLabel.Text = "Default camera estimate";
            RefreshReading();
        };
        distanceCard.Controls.Add(resetCalibrationButton);

        _calibrationLabel.Text = "Default camera estimate";
        _calibrationLabel.AutoSize = false;
        _calibrationLabel.Size = new Size(350, 20);
        _calibrationLabel.Location = new Point(22, 257);
        _calibrationLabel.Font = new Font("Segoe UI", 8F);
        _calibrationLabel.ForeColor = Color.FromArgb(151, 163, 184);
        distanceCard.Controls.Add(_calibrationLabel);

        var connectionCard = CreateCard();
        connectionCard.Margin = new Padding(10, 0, 0, 12);
        content.Controls.Add(connectionCard, 1, 0);

        _connectionLabel.Text = "Not connected";
        _connectionLabel.AutoSize = false;
        _connectionLabel.Size = new Size(220, 30);
        _connectionLabel.Location = new Point(20, 10);
        _connectionLabel.ForeColor = Color.FromArgb(200, 208, 222);
        connectionCard.Controls.Add(_connectionLabel);

        _connectButton.Text = "Connect remote";
        ConfigureButton(_connectButton, "Connect remote", new Point(20, 40), new Size(220, 34));
        _connectButton.Click += ConnectButton_Click;
        connectionCard.Controls.Add(_connectButton);

        _sensorCountLabel.Text = "Waiting for a remote";
        _sensorCountLabel.AutoSize = false;
        _sensorCountLabel.Size = new Size(230, 32);
        _sensorCountLabel.Location = new Point(20, 77);
        _sensorCountLabel.ForeColor = Color.FromArgb(151, 163, 184);
        connectionCard.Controls.Add(_sensorCountLabel);

        _alertsEnabled.Text = "Alert when closer than";
        _alertsEnabled.Checked = true;
        _alertsEnabled.AutoSize = true;
        _alertsEnabled.Location = new Point(20, 109);
        _alertsEnabled.ForeColor = Color.FromArgb(200, 208, 222);
        connectionCard.Controls.Add(_alertsEnabled);

        _alertDistanceInput.Minimum = 0.1M;
        _alertDistanceInput.Maximum = 10M;
        _alertDistanceInput.DecimalPlaces = 1;
        _alertDistanceInput.Increment = 0.1M;
        _alertDistanceInput.Value = 1M;
        _alertDistanceInput.Width = 70;
        _alertDistanceInput.Location = new Point(20, 135);
        _alertDistanceInput.BackColor = Color.FromArgb(28, 36, 53);
        _alertDistanceInput.ForeColor = Color.White;
        connectionCard.Controls.Add(_alertDistanceInput);

        var alertMeters = CreateLabel("meters", 9F, Color.FromArgb(151, 163, 184));
        alertMeters.Location = new Point(96, 140);
        connectionCard.Controls.Add(alertMeters);

        ConfigureButton(_recordButton, "Start recording", new Point(20, 164), new Size(220, 32));
        _recordButton.BackColor = Color.FromArgb(44, 54, 73);
        _recordButton.Click += RecordButton_Click;
        connectionCard.Controls.Add(_recordButton);

        _recordingLabel.Text = "Recording off";
        _recordingLabel.AutoSize = false;
        _recordingLabel.Size = new Size(230, 18);
        _recordingLabel.Location = new Point(20, 198);
        _recordingLabel.ForeColor = Color.FromArgb(151, 163, 184);
        _recordingLabel.Font = new Font("Segoe UI", 8F);
        connectionCard.Controls.Add(_recordingLabel);

        _gestureLabel.Text = "Gesture: waiting";
        _gestureLabel.AutoSize = false;
        _gestureLabel.Size = new Size(230, 24);
        _gestureLabel.Location = new Point(20, 220);
        _gestureLabel.ForeColor = Color.FromArgb(126, 231, 200);
        _gestureLabel.Font = new Font("Segoe UI Semibold", 9F);
        connectionCard.Controls.Add(_gestureLabel);

        var viewCard = CreateCard();
        viewCard.Margin = new Padding(0, 0, 0, 12);
        content.Controls.Add(viewCard, 0, 1);
        content.SetColumnSpan(viewCard, 2);

        var viewTitle = CreateLabel("IR CAMERA  ·  CENTER BOTH LIGHTS", 10F, Color.FromArgb(151, 163, 184));
        viewTitle.Location = new Point(22, 16);
        viewCard.Controls.Add(viewTitle);

        _sensorView.Location = new Point(22, 43);
        _sensorView.Size = new Size(390, 125);
        _sensorView.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _sensorView.BackColor = Color.FromArgb(16, 23, 38);
        _sensorView.Paint += PaintSensorView;
        viewCard.Controls.Add(_sensorView);

        _aimingGuideLabel.Text = "Aim both lights into the camera view.";
        _aimingGuideLabel.AutoSize = false;
        _aimingGuideLabel.Size = new Size(790, 23);
        _aimingGuideLabel.Location = new Point(22, 173);
        _aimingGuideLabel.ForeColor = Color.FromArgb(126, 231, 200);
        _aimingGuideLabel.Font = new Font("Segoe UI Semibold", 9F);
        viewCard.Controls.Add(_aimingGuideLabel);

        var trailTitle = CreateLabel("AIMING TRAIL  ·  LAST 300 POSITIONS", 10F, Color.FromArgb(151, 163, 184));
        trailTitle.Location = new Point(432, 16);
        viewCard.Controls.Add(trailTitle);

        _trailView.Location = new Point(422, 43);
        _trailView.Size = new Size(390, 125);
        _trailView.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _trailView.BackColor = Color.FromArgb(16, 23, 38);
        _trailView.Paint += PaintTrailView;
        viewCard.Controls.Add(_trailView);

        _readingStatusLabel.Text = "Connect a paired Wii Remote to begin.";
        _readingStatusLabel.AutoSize = false;
        _readingStatusLabel.Dock = DockStyle.Fill;
        _readingStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _readingStatusLabel.Padding = new Padding(2, 0, 0, 8);
        _readingStatusLabel.ForeColor = Color.FromArgb(151, 163, 184);
        content.Controls.Add(_readingStatusLabel, 0, 2);

        var note = CreateLabel("Calibrate at a measured distance for better accuracy.", 9F, Color.FromArgb(125, 138, 160));
        note.Dock = DockStyle.Fill;
        note.TextAlign = ContentAlignment.MiddleRight;
        note.Padding = new Padding(0, 0, 2, 8);
        content.Controls.Add(note, 1, 2);

        Controls.Add(content);
        Controls.Add(header);
    }

    private async void ConnectButton_Click(object? sender, EventArgs e)
    {
        if (_wiimotes.Count > 0)
        {
            Disconnect();
            return;
        }

        _connectButton.Enabled = false;
        _connectionLabel.Text = "Connecting...";
        _readingStatusLabel.Text = "Searching for a paired Wii Remote.";
        _sensorCountLabel.Text = "Waiting for infrared data";
        lock (_readingLock)
        {
            _latestReading = null;
        }

        List<Wiimote>? connectingRemotes = null;
        try
        {
            var result = await Task.Run(() =>
            {
                var collection = new WiimoteCollection();
                collection.FindAllWiimotes();

                var connected = new List<Wiimote>();
                var failures = new List<string>();
                foreach (var remote in collection)
                {
                    try
                    {
                        remote.WiimoteChanged += OnWiimoteChanged;
                        remote.Connect();
                        remote.SetReportType(InputReport.IRAccel, true);
                        connected.Add(remote);
                    }
                    catch (Exception exception)
                    {
                        remote.WiimoteChanged -= OnWiimoteChanged;
                        remote.Disconnect();
                        failures.Add(exception.Message);
                    }
                }

                return new ConnectionResult(collection.Count, connected, failures);
            });
            connectingRemotes = result.Connected;

            if (_closing || IsDisposed)
            {
                foreach (var remote in result.Connected)
                {
                    remote.WiimoteChanged -= OnWiimoteChanged;
                    remote.Disconnect();
                }
                return;
            }

            if (result.Connected.Count == 0)
            {
                var details = result.Failures.Count > 0
                    ? string.Join(" | ", result.Failures.Distinct())
                    : "No Wii Remote HID interfaces were found.";
                throw new InvalidOperationException(
                    $"Found {result.FoundCount} Wii Remote interface(s), but could not connect. {details}");
            }

            _wiimotes.AddRange(result.Connected);
            connectingRemotes = null;
            _connectButton.Text = "Disconnect";
            _connectButton.Enabled = true;
            _connectionLabel.Text = $"Connected ({result.Connected.Count}/{result.FoundCount} interfaces)";
            _connectionLabel.ForeColor = Color.FromArgb(126, 231, 200);
            _displayTimer.Start();
            RefreshReading();
        }
        catch (Exception exception)
        {
            if (connectingRemotes is not null)
            {
                foreach (var remote in connectingRemotes)
                {
                    remote.WiimoteChanged -= OnWiimoteChanged;
                    remote.Disconnect();
                }
            }

            if (_closing || IsDisposed)
            {
                return;
            }

            _connectionLabel.Text = "Could not connect";
            _connectionLabel.ForeColor = Color.FromArgb(255, 145, 145);
            _readingStatusLabel.Text = exception.Message;
            _sensorCountLabel.Text = "No remote data";
            _connectButton.Enabled = true;
        }
    }

    private void OnWiimoteChanged(object? sender, WiimoteChangedEventArgs e)
    {
        var sensors = e.WiimoteState.IRState.IRSensors;
        var visible = sensors
            .Where(sensor => sensor.Found)
            .OrderBy(sensor => sensor.RawPosition.X)
            .ToArray();

        var now = DateTime.UtcNow;
        var accelerometer = e.WiimoteState.AccelState.Values;
        int? leftX = visible.Length >= 2 ? visible[0].RawPosition.X : null;
        int? rightX = visible.Length >= 2 ? visible[visible.Length - 1].RawPosition.X : null;
        int? leftY = visible.Length >= 2 ? visible[0].RawPosition.Y : null;
        int? rightY = visible.Length >= 2 ? visible[visible.Length - 1].RawPosition.Y : null;
        float? aimX = visible.Length > 0 ? visible.Average(sensor => sensor.Position.X) : null;
        float? aimY = visible.Length > 0 ? visible.Average(sensor => sensor.Position.Y) : null;

        lock (_readingLock)
        {
            var elapsed = now - _previousAccelTime;
            if (_previousAccelTime != DateTime.MinValue &&
                elapsed < TimeSpan.FromMilliseconds(200) &&
                now - _lastGestureTime > TimeSpan.FromMilliseconds(900))
            {
                var deltaX = accelerometer.X - _previousAccel.X;
                var deltaY = accelerometer.Y - _previousAccel.Y;
                var deltaZ = accelerometer.Z - _previousAccel.Z;
                var largestDelta = Math.Max(Math.Abs(deltaX), Math.Max(Math.Abs(deltaY), Math.Abs(deltaZ)));
                if (largestDelta >= 0.85F)
                {
                    var axis = Math.Abs(deltaX) >= Math.Abs(deltaY) && Math.Abs(deltaX) >= Math.Abs(deltaZ)
                        ? ("X", deltaX)
                        : Math.Abs(deltaY) >= Math.Abs(deltaZ)
                            ? ("Y", deltaY)
                            : ("Z", deltaZ);
                    _lastGesture = $"Quick flick {axis.Item1} {(axis.Item2 >= 0 ? "+" : "-")}";
                    _lastGestureTime = now;
                }
            }

            _previousAccel = accelerometer;
            _previousAccelTime = now;
            _latestReading = new SensorReading(
                visible.Length,
                leftX,
                rightX,
                leftY,
                rightY,
                aimX,
                aimY,
                now,
                e.WiimoteState.Battery,
                e.WiimoteState.ButtonState,
                accelerometer,
                _lastGesture,
                _lastGestureTime);
        }
    }

    private void RefreshReading()
    {
        SensorReading? reading;
        lock (_readingLock)
        {
            reading = _latestReading;
        }

        if (reading is null)
        {
            return;
        }

        if (DateTime.UtcNow - reading.Value.LastReport > TimeSpan.FromSeconds(2))
        {
            _readingStatusLabel.Text = "Connected, but no live reports are arriving.";
        }
        else
        {
            var buttons = reading.Value.Buttons;
            var pressed = new List<string>();
            if (buttons.A) pressed.Add("A");
            if (buttons.B) pressed.Add("B");
            if (buttons.One) pressed.Add("1");
            if (buttons.Two) pressed.Add("2");
            if (buttons.Home) pressed.Add("Home");
            if (buttons.Plus) pressed.Add("+");
            if (buttons.Minus) pressed.Add("-");
            _readingStatusLabel.Text =
                $"Live • Battery {reading.Value.Battery:0}% • Accel {reading.Value.Accelerometer.X:0.00}, {reading.Value.Accelerometer.Y:0.00}, {reading.Value.Accelerometer.Z:0.00} • {(pressed.Count > 0 ? string.Join(", ", pressed) : "no buttons")}";
        }
        _gestureLabel.Text = DateTime.UtcNow - reading.Value.GestureTime < TimeSpan.FromSeconds(2)
            ? $"Gesture: {reading.Value.Gesture}"
            : "Gesture: ready — try a quick flick";

        _sensorCountLabel.Text = reading.Value.SensorCount switch
        {
            0 => "No infrared lights detected",
            1 => "1 infrared light detected",
            _ => $"{reading.Value.SensorCount} infrared lights detected"
        };

        if (reading.Value.LeftX is int leftX && reading.Value.RightX is int rightX)
        {
            var rawDistance = CalculateRawDistance(leftX, rightX);
            var distanceMeters = rawDistance * _calibrationFactor;

            _distanceLabel.Text = distanceMeters.ToString("0.00");
            _feetLabel.Text = $"{distanceMeters * 3.28084:0.0} ft";
            _sensorCountLabel.Text += " • distance available";
            _calibrateButton.Enabled = rawDistance > 0 && DateTime.UtcNow - reading.Value.LastReport < TimeSpan.FromSeconds(1);
            _aimingGuideLabel.Text = reading.Value.AimX is float aimX
                ? aimX < 0.42F ? "Both lights visible · move the camera view slightly right"
                : aimX > 0.58F ? "Both lights visible · move the camera view slightly left"
                : "Both lights visible · nicely centered"
                : "Both lights visible · aim at the sensor bar";
            UpdateDistanceAlert(distanceMeters);
        }
        else
        {
            _distanceLabel.Text = "--";
            _feetLabel.Text = "";
            _distanceLabel.ForeColor = Color.FromArgb(126, 231, 200);
            _calibrateButton.Enabled = false;
            _insideAlertDistance = false;
            _sensorCountLabel.Text += reading.Value.SensorCount switch
            {
                0 => " • point at the sensor bar",
                1 => " • waiting for the second light",
                _ => ""
            };
            _aimingGuideLabel.Text = reading.Value.SensorCount switch
            {
                0 => "No lights yet · point the remote toward the Dolphin Bar",
                1 => "One light found · sweep slowly until both lights appear",
                _ => "Aim both lights into the camera view"
            };
        }

        if (reading.Value.AimX is float trailX && reading.Value.AimY is float trailY &&
            reading.Value.LastReport > _lastTrailReport)
        {
            _positionTrail.Enqueue(new System.Drawing.PointF(trailX, trailY));
            while (_positionTrail.Count > 300)
            {
                _positionTrail.Dequeue();
            }
            _lastTrailReport = reading.Value.LastReport;
            _trailView.Invalidate();
        }

        WriteRecordingSample(reading.Value);
        _sensorView.Invalidate();
    }

    private double CalculateRawDistance(int leftX, int rightX)
    {
        var focalLengthPixels = CameraHorizontalResolution /
            (2.0 * Math.Tan(CameraFieldOfViewDegrees * Math.PI / 360.0));
        var imageCenter = CameraHorizontalResolution / 2.0;
        var leftAngle = Math.Atan((leftX - imageCenter) / focalLengthPixels);
        var rightAngle = Math.Atan((rightX - imageCenter) / focalLengthPixels);
        var halfAngleRadians = (rightAngle - leftAngle) / 2.0;
        return (double)_spacingInput.Value / 1000.0 / (2.0 * Math.Tan(halfAngleRadians));
    }

    private void CalibrateButton_Click(object? sender, EventArgs e)
    {
        SensorReading? reading;
        lock (_readingLock)
        {
            reading = _latestReading;
        }

        if (reading?.LeftX is not int leftX || reading.Value.RightX is not int rightX)
        {
            _calibrationLabel.Text = "Calibration needs both IR lights in view.";
            return;
        }

        if (DateTime.UtcNow - reading.Value.LastReport >= TimeSpan.FromSeconds(1))
        {
            _calibrationLabel.Text = "Wait for live sensor data before calibrating.";
            return;
        }

        var rawDistance = CalculateRawDistance(leftX, rightX);
        if (rawDistance <= 0 || double.IsInfinity(rawDistance) || double.IsNaN(rawDistance))
        {
            _calibrationLabel.Text = "Calibration failed; adjust your position and try again.";
            return;
        }

        _calibrationFactor = (float)((double)_calibrationInput.Value / rawDistance);
        _calibrationLabel.Text = $"Calibrated at {_calibrationInput.Value:0.00} m";
        RefreshReading();
    }

    private void UpdateDistanceAlert(double distanceMeters)
    {
        var isInside = _alertsEnabled.Checked && distanceMeters < (double)_alertDistanceInput.Value;
        _distanceLabel.ForeColor = isInside
            ? Color.FromArgb(255, 145, 145)
            : Color.FromArgb(126, 231, 200);

        if (isInside && !_insideAlertDistance && DateTime.UtcNow - _lastAlertTime > TimeSpan.FromSeconds(2))
        {
            SystemSounds.Exclamation.Play();
            _lastAlertTime = DateTime.UtcNow;
        }

        if (isInside)
        {
            _readingStatusLabel.ForeColor = Color.FromArgb(255, 145, 145);
            _readingStatusLabel.Text = $"TOO CLOSE — under {_alertDistanceInput.Value:0.0} m";
        }
        else
        {
            _readingStatusLabel.ForeColor = Color.FromArgb(151, 163, 184);
        }

        _insideAlertDistance = isInside;
    }

    private void RecordButton_Click(object? sender, EventArgs e)
    {
        if (_sessionWriter is not null)
        {
            StopRecording();
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Save Wii Remote session",
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = "csv",
            AddExtension = true,
            FileName = $"wiimote-session-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _sessionPath = dialog.FileName;
            _sessionWriter = new StreamWriter(_sessionPath, false, new System.Text.UTF8Encoding(true))
            {
                AutoFlush = true
            };
            _sessionWriter.WriteLine("timestamp_utc,distance_m,distance_ft,ir_count,aim_x,aim_y,accel_x,accel_y,accel_z,battery_percent,buttons,gesture");
            _lastCsvReport = DateTime.MinValue;
            _recordButton.Text = "Stop recording";
            _recordButton.BackColor = Color.FromArgb(166, 62, 72);
            _recordingLabel.Text = "Recording to selected CSV";
            _recordingLabel.ForeColor = Color.FromArgb(255, 145, 145);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _sessionWriter?.Dispose();
            _sessionWriter = null;
            MessageBox.Show(this, exception.Message, "Could not start recording", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void WriteRecordingSample(SensorReading reading)
    {
        if (_sessionWriter is null || reading.LastReport <= _lastCsvReport)
        {
            return;
        }

        try
        {
            var distance = reading.LeftX is int left && reading.RightX is int right
                ? (CalculateRawDistance(left, right) * _calibrationFactor).ToString("0.000", CultureInfo.InvariantCulture)
                : "";
            var buttons = GetPressedButtons(reading.Buttons);
            var columns = new[]
            {
                reading.LastReport.ToString("O", CultureInfo.InvariantCulture),
                distance,
                reading.LeftX is int leftX && reading.RightX is int rightX
                    ? (CalculateRawDistance(leftX, rightX) * _calibrationFactor * 3.28084).ToString("0.000", CultureInfo.InvariantCulture)
                    : "",
                reading.SensorCount.ToString(CultureInfo.InvariantCulture),
                reading.AimX?.ToString("0.000", CultureInfo.InvariantCulture) ?? "",
                reading.AimY?.ToString("0.000", CultureInfo.InvariantCulture) ?? "",
                reading.Accelerometer.X.ToString("0.000", CultureInfo.InvariantCulture),
                reading.Accelerometer.Y.ToString("0.000", CultureInfo.InvariantCulture),
                reading.Accelerometer.Z.ToString("0.000", CultureInfo.InvariantCulture),
                reading.Battery.ToString("0.0", CultureInfo.InvariantCulture),
                QuoteCsv(buttons),
                QuoteCsv(reading.Gesture)
            };
            _sessionWriter.WriteLine(string.Join(",", columns));
            _lastCsvReport = reading.LastReport;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            StopRecording();
            MessageBox.Show(this, exception.Message, "Recording stopped", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopRecording()
    {
        if (_sessionWriter is null)
        {
            return;
        }

        _sessionWriter.Dispose();
        _sessionWriter = null;
        _recordButton.Text = "Start recording";
        _recordButton.BackColor = Color.FromArgb(44, 54, 73);
        _recordingLabel.Text = $"Saved: {Path.GetFileName(_sessionPath)}";
        _recordingLabel.ForeColor = Color.FromArgb(126, 231, 200);
    }

    private static string GetPressedButtons(RemoteButtonState buttons)
    {
        var pressed = new List<string>();
        if (buttons.A) pressed.Add("A");
        if (buttons.B) pressed.Add("B");
        if (buttons.One) pressed.Add("1");
        if (buttons.Two) pressed.Add("2");
        if (buttons.Home) pressed.Add("Home");
        if (buttons.Plus) pressed.Add("Plus");
        if (buttons.Minus) pressed.Add("Minus");
        if (buttons.Up) pressed.Add("Up");
        if (buttons.Down) pressed.Add("Down");
        if (buttons.Left) pressed.Add("Left");
        if (buttons.Right) pressed.Add("Right");
        return string.Join("+", pressed);
    }

    private static string QuoteCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private void PaintSensorView(object? sender, PaintEventArgs e)
    {
        var bounds = _sensorView.ClientRectangle;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var axisPen = new Pen(Color.FromArgb(52, 65, 88), 1F);
        using var axisBrush = new SolidBrush(Color.FromArgb(125, 138, 160));
        using var lightBrush = new SolidBrush(Color.FromArgb(126, 231, 200));
        using var glowBrush = new SolidBrush(Color.FromArgb(38, 126, 231, 200));
        using var targetPen = new Pen(Color.FromArgb(98, 91, 245), 1F);

        var axisY = bounds.Height / 2;
        e.Graphics.DrawLine(axisPen, 24, axisY, bounds.Width - 24, axisY);
        e.Graphics.DrawString("L", Font, axisBrush, 10, axisY - 9);
        e.Graphics.DrawString("R", Font, axisBrush, bounds.Width - 20, axisY - 9);
        var targetX = bounds.Width / 2F;
        var targetY = bounds.Height / 2F;
        e.Graphics.DrawEllipse(targetPen, targetX - 18, targetY - 18, 36, 36);
        e.Graphics.DrawLine(targetPen, targetX - 25, targetY, targetX + 25, targetY);
        e.Graphics.DrawLine(targetPen, targetX, targetY - 25, targetX, targetY + 25);

        SensorReading? reading;
        lock (_readingLock)
        {
            reading = _latestReading;
        }

        if (reading?.LeftX is not int leftX || reading.Value.RightX is not int rightX ||
            reading.Value.LeftY is not int leftY || reading.Value.RightY is not int rightY)
        {
            e.Graphics.DrawString(
                reading is null ? "Waiting for remote reports" : "No infrared lights detected",
                Font,
                axisBrush,
                24,
                axisY + 18);
            return;
        }

        var drawableWidth = Math.Max(1, bounds.Width - 48);
        var drawableHeight = Math.Max(1, bounds.Height - 24);
        var left = new System.Drawing.PointF(24 + leftX / 1023F * drawableWidth, 12 + leftY / 767F * drawableHeight);
        var right = new System.Drawing.PointF(24 + rightX / 1023F * drawableWidth, 12 + rightY / 767F * drawableHeight);
        e.Graphics.DrawLine(axisPen, left, right);
        foreach (var point in new[] { left, right })
        {
            e.Graphics.FillEllipse(glowBrush, point.X - 14, point.Y - 14, 28, 28);
            e.Graphics.FillEllipse(lightBrush, point.X - 6, point.Y - 6, 12, 12);
        }
    }

    private void PaintTrailView(object? sender, PaintEventArgs e)
    {
        var bounds = _trailView.ClientRectangle;
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var guidePen = new Pen(Color.FromArgb(43, 55, 75), 1F);
        using var trailPen = new Pen(Color.FromArgb(98, 91, 245), 2.5F);
        using var dotBrush = new SolidBrush(Color.FromArgb(126, 231, 200));
        using var guideBrush = new SolidBrush(Color.FromArgb(125, 138, 160));
        var centerX = bounds.Width / 2;
        var centerY = bounds.Height / 2;
        e.Graphics.DrawLine(guidePen, centerX, 12, centerX, bounds.Height - 12);
        e.Graphics.DrawLine(guidePen, 12, centerY, bounds.Width - 12, centerY);
        e.Graphics.DrawString("left", Font, guideBrush, 12, bounds.Height - 23);
        e.Graphics.DrawString("right", Font, guideBrush, bounds.Width - 50, bounds.Height - 23);

        var points = _positionTrail
            .Select(position => new System.Drawing.PointF(position.X * bounds.Width, position.Y * bounds.Height))
            .ToArray();
        if (points.Length > 1)
        {
            e.Graphics.DrawLines(trailPen, points);
        }
        if (points.Length > 0)
        {
            var current = points[points.Length - 1];
            e.Graphics.FillEllipse(dotBrush, current.X - 5, current.Y - 5, 10, 10);
        }
        else
        {
            e.Graphics.DrawString("Move the remote to draw its aiming trail", Font, guideBrush, 16, centerY - 9);
        }
    }

    private void Disconnect()
    {
        _displayTimer.Stop();

        foreach (var remote in _wiimotes)
        {
            remote.WiimoteChanged -= OnWiimoteChanged;
            remote.Disconnect();
        }

        _wiimotes.Clear();
        _connectButton.Text = "Connect remote";
        _connectionLabel.Text = "Not connected";
        _connectionLabel.ForeColor = Color.FromArgb(200, 208, 222);
        _sensorCountLabel.Text = "Waiting for a remote";
        _readingStatusLabel.Text = "Connect a paired Wii Remote to begin.";
        _aimingGuideLabel.Text = "Aim both lights into the camera view.";
        _gestureLabel.Text = "Gesture: waiting";
        _distanceLabel.Text = "--";
        _feetLabel.Text = "";
        _calibrateButton.Enabled = false;
        _positionTrail.Clear();
        _lastTrailReport = DateTime.MinValue;
        lock (_readingLock)
        {
            _latestReading = null;
        }
        _sensorView.Invalidate();
        _trailView.Invalidate();
    }

    private sealed class ConnectionResult
    {
        public ConnectionResult(int foundCount, List<Wiimote> connected, List<string> failures)
        {
            FoundCount = foundCount;
            Connected = connected;
            Failures = failures;
        }

        public int FoundCount { get; }
        public List<Wiimote> Connected { get; }
        public List<string> Failures { get; }
    }

    private static Panel CreateCard() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(23, 31, 47),
        Padding = new Padding(18)
    };

    private static void ConfigureButton(Button button, string text, Point location, Size size)
    {
        button.Text = text;
        button.Size = size;
        button.Location = location;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Color.FromArgb(98, 91, 245);
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI Semibold", 9F);
        button.Cursor = Cursors.Hand;
    }

    private static Label CreateLabel(string text, float size, Color color) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", size),
        ForeColor = color
    };

    private readonly struct SensorReading
    {
        public SensorReading(
            int sensorCount,
            int? leftX,
            int? rightX,
            int? leftY,
            int? rightY,
            float? aimX,
            float? aimY,
            DateTime lastReport,
            float battery,
            RemoteButtonState buttons,
            Point3F accelerometer,
            string gesture,
            DateTime gestureTime)
        {
            SensorCount = sensorCount;
            LeftX = leftX;
            RightX = rightX;
            LeftY = leftY;
            RightY = rightY;
            AimX = aimX;
            AimY = aimY;
            LastReport = lastReport;
            Battery = battery;
            Buttons = buttons;
            Accelerometer = accelerometer;
            Gesture = gesture;
            GestureTime = gestureTime;
        }

        public int SensorCount { get; }
        public int? LeftX { get; }
        public int? RightX { get; }
        public int? LeftY { get; }
        public int? RightY { get; }
        public float? AimX { get; }
        public float? AimY { get; }
        public DateTime LastReport { get; }
        public float Battery { get; }
        public RemoteButtonState Buttons { get; }
        public Point3F Accelerometer { get; }
        public string Gesture { get; }
        public DateTime GestureTime { get; }
    }
}
