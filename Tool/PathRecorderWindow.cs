using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace Tool
{
    internal sealed class PathRecorderReadResult
    {
        public bool Success { get; private set; }
        public string Error { get; private set; }
        public PathRecorderSnapshot Snapshot { get; private set; }

        public static PathRecorderReadResult Ok(PathRecorderSnapshot snapshot)
        {
            return new PathRecorderReadResult
            {
                Success = true,
                Snapshot = snapshot
            };
        }

        public static PathRecorderReadResult Fail(string error)
        {
            return new PathRecorderReadResult
            {
                Success = false,
                Error = string.IsNullOrWhiteSpace(error) ? "read failed" : error
            };
        }
    }

    internal struct PathRecorderSnapshot
    {
        public DateTime ReadTime;
        public ushort EntityId;
        public double X;
        public double Y;
        public double Z;
        public bool HasTransform;
        public double ActorYaw;
        public double CameraPitch;
        public double CameraYaw;
    }

    internal sealed class PathRecordPoint
    {
        public int Index { get; set; }
        public string TimeText { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double SegmentDistance { get; set; }
        public double TotalDistance { get; set; }
        public double ActorYaw { get; set; }
    }

    internal sealed class PathRecorderWindow : Window
    {
        private readonly Func<PathRecorderReadResult> _reader;
        private readonly ObservableCollection<PathRecordPoint> _points;
        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _recordTimer;

        private TextBlock _statusText;
        private TextBlock _positionText;
        private TextBlock _countText;
        private TextBox _intervalBox;
        private TextBox _minDistanceBox;
        private TextBox _previewBox;
        private DataGrid _grid;
        private Button _autoButton;
        private bool _isRecording;

        public PathRecorderWindow(Func<PathRecorderReadResult> reader)
        {
            _reader = reader;
            _points = new ObservableCollection<PathRecordPoint>();
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

            Title = "AION 路径录制测试";
            Width = 980;
            Height = 680;
            MinWidth = 820;
            MinHeight = 540;
            Background = BrushFromRgb(244, 252, 247);
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI");
            FontSize = 13;

            Content = BuildLayout();

            _refreshTimer.Tick += (sender, args) => RefreshPosition();
            _recordTimer.Tick += (sender, args) => AutoRecordPoint();
            Loaded += (sender, args) =>
            {
                RefreshPosition();
                _refreshTimer.Start();
            };
            Closed += (sender, args) =>
            {
                _refreshTimer.Stop();
                _recordTimer.Stop();
            };
        }

        private UIElement BuildLayout()
        {
            var root = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(14)
            };

            var header = BuildHeader();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var controls = BuildControls();
            DockPanel.SetDock(controls, Dock.Top);
            root.Children.Add(controls);

            _previewBox = new TextBox
            {
                Height = 120,
                Margin = new Thickness(0, 10, 0, 0),
                IsReadOnly = true,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = BrushFromRgb(235, 249, 240),
                BorderBrush = BrushFromRgb(38, 150, 84),
                Foreground = BrushFromRgb(0, 69, 33),
                TextWrapping = TextWrapping.NoWrap
            };
            DockPanel.SetDock(_previewBox, Dock.Bottom);
            root.Children.Add(_previewBox);

            _grid = BuildGrid();
            root.Children.Add(_grid);

            return root;
        }

        private UIElement BuildHeader()
        {
            var panel = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Vertical };
            left.Children.Add(new TextBlock
            {
                Text = "路径录制",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = BrushFromRgb(0, 112, 55)
            });

            _statusText = new TextBlock
            {
                Text = "等待读取坐标...",
                Margin = new Thickness(0, 5, 0, 0),
                Foreground = BrushFromRgb(36, 83, 58)
            };
            _positionText = new TextBlock
            {
                Text = "X=-- Y=-- Z=--",
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = BrushFromRgb(0, 69, 33)
            };
            left.Children.Add(_statusText);
            left.Children.Add(_positionText);

            _countText = new TextBlock
            {
                Text = "点数 0 | 总距 0.00",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = BrushFromRgb(0, 112, 55),
                FontWeight = FontWeights.Bold
            };

            Grid.SetColumn(left, 0);
            Grid.SetColumn(_countText, 1);
            panel.Children.Add(left);
            panel.Children.Add(_countText);

            return WrapPanel(panel, new Thickness(14), BrushFromRgb(232, 249, 239));
        }

        private UIElement BuildControls()
        {
            var panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(CreateButton("手动录点", (sender, args) => AddManualPoint()));
            _autoButton = CreateButton("开始自动录制", (sender, args) => ToggleRecording());
            panel.Children.Add(_autoButton);
            panel.Children.Add(CreateButton("删除选中", (sender, args) => DeleteSelectedPoint()));
            panel.Children.Add(CreateButton("清空", (sender, args) => ClearPoints()));
            panel.Children.Add(CreateButton("复制路径", (sender, args) => CopyPathText()));

            panel.Children.Add(CreateLabel("间隔ms"));
            _intervalBox = CreateSmallTextBox("250", 58);
            panel.Children.Add(_intervalBox);

            panel.Children.Add(CreateLabel("最小距离"));
            _minDistanceBox = CreateSmallTextBox("1.0", 58);
            panel.Children.Add(_minDistanceBox);

            return WrapPanel(panel, new Thickness(10), BrushFromRgb(238, 253, 244));
        }

        private DataGrid BuildGrid()
        {
            var grid = new DataGrid
            {
                ItemsSource = _points,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Background = Brushes.White,
                BorderBrush = BrushFromRgb(38, 150, 84),
                RowBackground = BrushFromRgb(249, 255, 251),
                AlternatingRowBackground = BrushFromRgb(235, 249, 240),
                HorizontalGridLinesBrush = BrushFromRgb(201, 231, 212),
                Margin = new Thickness(0, 10, 0, 0)
            };

            grid.Columns.Add(TextColumn("#", "Index", 58));
            grid.Columns.Add(TextColumn("X", "X", 110, "F3"));
            grid.Columns.Add(TextColumn("Y", "Y", 110, "F3"));
            grid.Columns.Add(TextColumn("Z", "Z", 110, "F3"));
            grid.Columns.Add(TextColumn("段距", "SegmentDistance", 95, "F2"));
            grid.Columns.Add(TextColumn("总距", "TotalDistance", 95, "F2"));
            grid.Columns.Add(TextColumn("朝向", "ActorYaw", 95, "F2"));
            grid.Columns.Add(TextColumn("时间", "TimeText", 130));

            return grid;
        }

        private static DataGridTextColumn TextColumn(string header, string binding, double width)
        {
            return TextColumn(header, binding, width, null);
        }

        private static DataGridTextColumn TextColumn(string header, string binding, double width, string format)
        {
            var column = new DataGridTextColumn
            {
                Header = header,
                Width = new DataGridLength(width),
                Binding = new Binding(binding)
            };

            if (!string.IsNullOrWhiteSpace(format))
            {
                ((Binding)column.Binding).StringFormat = "{0:" + format + "}";
            }

            return column;
        }

        private static Button CreateButton(string text, RoutedEventHandler onClick)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 92,
                Height = 30,
                Margin = new Thickness(4),
                Padding = new Thickness(10, 0, 10, 0),
                Background = BrushFromRgb(19, 151, 79),
                Foreground = Brushes.White,
                BorderBrush = BrushFromRgb(0, 110, 53),
                FontWeight = FontWeights.Bold
            };
            button.Click += onClick;
            return button;
        }

        private static TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Margin = new Thickness(12, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = BrushFromRgb(0, 69, 33)
            };
        }

        private static TextBox CreateSmallTextBox(string text, double width)
        {
            return new TextBox
            {
                Text = text,
                Width = width,
                Height = 28,
                Margin = new Thickness(4),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = BrushFromRgb(235, 249, 240),
                BorderBrush = BrushFromRgb(38, 150, 84),
                Foreground = BrushFromRgb(0, 69, 33)
            };
        }

        private static Border WrapPanel(UIElement child, Thickness padding, Brush background)
        {
            return new Border
            {
                Padding = padding,
                Background = background,
                BorderBrush = BrushFromRgb(174, 223, 194),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = child
            };
        }

        private void RefreshPosition()
        {
            var result = _reader();
            if (!result.Success)
            {
                _statusText.Text = "读取失败: " + result.Error;
                _statusText.Foreground = BrushFromRgb(166, 40, 40);
                return;
            }

            var s = result.Snapshot;
            _statusText.Text = "读取正常 EntityId=" + s.EntityId + " " + s.ReadTime.ToString("HH:mm:ss.fff");
            _statusText.Foreground = BrushFromRgb(36, 83, 58);
            _positionText.Text =
                "X=" + FormatDouble(s.X, 3) +
                "  Y=" + FormatDouble(s.Y, 3) +
                "  Z=" + FormatDouble(s.Z, 3) +
                "  角色朝向=" + (s.HasTransform ? FormatDouble(s.ActorYaw, 2) : "n/a") +
                "  Camera(P/Y)=" + FormatDouble(s.CameraPitch, 2) + "/" + FormatDouble(s.CameraYaw, 2);
        }

        private void AddManualPoint()
        {
            var result = _reader();
            if (!result.Success)
            {
                SetStatus("手动录点失败: " + result.Error, true);
                return;
            }

            AddPoint(result.Snapshot, "手动录点");
        }

        private void ToggleRecording()
        {
            if (_isRecording)
            {
                _isRecording = false;
                _recordTimer.Stop();
                _autoButton.Content = "开始自动录制";
                SetStatus("自动录制已停止", false);
                return;
            }

            _recordTimer.Interval = TimeSpan.FromMilliseconds(ReadIntervalMs());
            _isRecording = true;
            _autoButton.Content = "停止自动录制";
            _recordTimer.Start();
            SetStatus("自动录制中", false);
        }

        private void AutoRecordPoint()
        {
            var result = _reader();
            if (!result.Success)
            {
                SetStatus("自动录制读取失败: " + result.Error, true);
                return;
            }

            if (_points.Count == 0)
            {
                AddPoint(result.Snapshot, "自动录制首点");
                return;
            }

            double minDistance = ReadMinDistance();
            double distance = Distance(_points[_points.Count - 1], result.Snapshot);
            if (distance >= minDistance)
            {
                AddPoint(result.Snapshot, "自动录点 距离=" + FormatDouble(distance, 2));
            }
        }

        private void AddPoint(PathRecorderSnapshot snapshot, string reason)
        {
            double segment = 0.0;
            double total = 0.0;
            if (_points.Count > 0)
            {
                PathRecordPoint previous = _points[_points.Count - 1];
                segment = Distance(previous, snapshot);
                total = previous.TotalDistance + segment;
            }

            _points.Add(new PathRecordPoint
            {
                Index = _points.Count + 1,
                TimeText = snapshot.ReadTime.ToString("HH:mm:ss.fff"),
                X = snapshot.X,
                Y = snapshot.Y,
                Z = snapshot.Z,
                SegmentDistance = segment,
                TotalDistance = total,
                ActorYaw = snapshot.HasTransform ? snapshot.ActorYaw : 0.0
            });

            UpdatePreview();
            SetStatus(reason + "，当前点数=" + _points.Count, false);
            _grid.ScrollIntoView(_points[_points.Count - 1]);
        }

        private void DeleteSelectedPoint()
        {
            var selected = _grid.SelectedItem as PathRecordPoint;
            if (selected == null)
            {
                SetStatus("没有选中路径点", true);
                return;
            }

            _points.Remove(selected);
            RecalculatePoints();
            UpdatePreview();
            SetStatus("已删除选中路径点", false);
        }

        private void ClearPoints()
        {
            _points.Clear();
            UpdatePreview();
            SetStatus("路径已清空", false);
        }

        private void CopyPathText()
        {
            string text = BuildPathText();
            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("没有可复制的路径点", true);
                return;
            }

            Clipboard.SetText(text);
            SetStatus("路径文本已复制", false);
        }

        private void RecalculatePoints()
        {
            double total = 0.0;
            for (int i = 0; i < _points.Count; i++)
            {
                PathRecordPoint point = _points[i];
                point.Index = i + 1;
                if (i == 0)
                {
                    point.SegmentDistance = 0.0;
                    point.TotalDistance = 0.0;
                    continue;
                }

                point.SegmentDistance = Distance(_points[i - 1], point);
                total += point.SegmentDistance;
                point.TotalDistance = total;
            }

            _grid.Items.Refresh();
            UpdateCountText();
        }

        private void UpdatePreview()
        {
            _previewBox.Text = BuildPathText();
            UpdateCountText();
        }

        private void UpdateCountText()
        {
            double total = _points.Count == 0 ? 0.0 : _points[_points.Count - 1].TotalDistance;
            _countText.Text = "点数 " + _points.Count + " | 总距 " + FormatDouble(total, 2);
        }

        private string BuildPathText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _points.Count; i++)
            {
                PathRecordPoint point = _points[i];
                sb.Append(FormatDouble(point.X, 3));
                sb.Append(", ");
                sb.Append(FormatDouble(point.Y, 3));
                sb.Append(", ");
                sb.Append(FormatDouble(point.Z, 3));
                if (i + 1 < _points.Count)
                {
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private void SetStatus(string text, bool isError)
        {
            _statusText.Text = text;
            _statusText.Foreground = isError ? BrushFromRgb(166, 40, 40) : BrushFromRgb(36, 83, 58);
        }

        private int ReadIntervalMs()
        {
            int interval;
            if (!int.TryParse(_intervalBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out interval))
            {
                interval = 250;
            }

            if (interval < 50)
            {
                interval = 50;
            }

            if (interval > 5000)
            {
                interval = 5000;
            }

            _intervalBox.Text = interval.ToString(CultureInfo.InvariantCulture);
            return interval;
        }

        private double ReadMinDistance()
        {
            double distance;
            if (!double.TryParse(_minDistanceBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out distance) &&
                !double.TryParse(_minDistanceBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out distance))
            {
                distance = 1.0;
            }

            if (distance < 0.0)
            {
                distance = 0.0;
            }

            if (distance > 100.0)
            {
                distance = 100.0;
            }

            return distance;
        }

        private static double Distance(PathRecordPoint point, PathRecorderSnapshot snapshot)
        {
            double dx = snapshot.X - point.X;
            double dy = snapshot.Y - point.Y;
            double dz = snapshot.Z - point.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static double Distance(PathRecordPoint a, PathRecordPoint b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double dz = b.Z - a.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static string FormatDouble(double value, int decimals)
        {
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        private static SolidColorBrush BrushFromRgb(byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }
    }
}
