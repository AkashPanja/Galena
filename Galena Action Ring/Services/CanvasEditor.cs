using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;

namespace GalenaActionRing.Services
{
    public sealed class CanvasEditorDependencies
    {
        public Canvas RingCanvas { get; init; } = null!;
        public TextBlock NodeEditDefaultText { get; init; } = null!;
        public UIElement NodeEditPanel { get; init; } = null!;
        public FontIcon PropGlyphPreview { get; init; } = null!;
        public TextBlock PropLabelDisplay { get; init; } = null!;
        public TextBlock PropActionDisplay { get; init; } = null!;
        public TextBox PropLabelBox { get; init; } = null!;
        public TextBox PropUrlBox { get; init; } = null!;
        public TextBox PropAppPathBox { get; init; } = null!;
        public UIElement PropAppPathSection { get; init; } = null!;
        public ComboBox PropActionTypeBox { get; init; } = null!;
        public Button EditSubmenuBtn { get; init; } = null!;
        public Button CanvasBackBtn { get; init; } = null!;
        public TextBlock CanvasTitle { get; init; } = null!;
        public UIElement IconSection { get; init; } = null!;
        public Selector IconGallery { get; init; } = null!;
        public TextBox IconSearchBox { get; init; } = null!;
        public FrameworkElement ColorBody { get; init; } = null!;
        public FontIcon ColorChevron { get; init; } = null!;
        public ColorPicker PrimaryColorPicker { get; init; } = null!;
        public UIElement SecondaryPreview { get; init; } = null!;
        public Grid AppRoot { get; init; } = null!;
    }

    public sealed class CanvasEditor
    {
        private readonly CanvasEditorDependencies _deps;
        private readonly List<Grid> _canvasElements = new();
        private readonly Stack<(List<RingNode> Nodes, string Title, List<RingNode> ParentNodes)> _canvasStack = new();
        private List<RingNode> _canvasNodes = new();
        private List<RingNode> _activeNodes = new();
        private int _selectedCanvasIndex = -1;
        private bool _isDragging;
        private int _dragIndex = -1;
        private double _dragStartAngle;

        private const double ActualOsdSize = 400;
        private const double CanvasViewport = 400;
        private double Scale => CanvasViewport / ActualOsdSize;

        private SolidColorBrush InactiveFill = new(Color.FromArgb(128, 255, 255, 255));
        private SolidColorBrush InactiveStroke = new(Color.FromArgb(255, 102, 102, 102));
        private SolidColorBrush InactiveForeground = new(Color.FromArgb(255, 0, 0, 0));
        private SolidColorBrush ActiveFill = new(Color.FromArgb(255, 0, 0, 0));
        private SolidColorBrush ActiveStroke = new(Color.FromArgb(255, 255, 255, 255));
        private SolidColorBrush ActiveForeground = new(Color.FromArgb(255, 255, 255, 255));
        private static readonly SolidColorBrush CenterFill = new(Color.FromArgb(128, 255, 255, 255));

        private bool _colorsExpanded;
        private ActionTypeItem? _lastActionTypeItem;
        private bool _iconGalleryLoaded;
        private bool _suppressColorChange;
        private bool _suppressActionTypeChange;

        public ProfileManager? ProfileManager { get; set; }

        public CanvasEditor(CanvasEditorDependencies deps)
        {
            _deps = deps;
        }

        public void LoadProfile(List<RingNode> editingCopyNodes, RingProfile profile)
        {
            _canvasStack.Clear();
            _activeNodes = editingCopyNodes;
            _canvasNodes = _activeNodes;
            _deps.CanvasTitle.Text = profile.Name;
            _deps.CanvasBackBtn.Visibility = Visibility.Collapsed;
            _selectedCanvasIndex = -1;
            SelectProfileColors();
            RenderCanvas();
        }

        public void ResetCanvas()
        {
            _selectedCanvasIndex = -1;
            HideNodeProperties();
            _canvasStack.Clear();
            _canvasNodes = _activeNodes;
            _deps.CanvasBackBtn.Visibility = Visibility.Collapsed;
            RenderCanvas();
        }

        public void RenderCanvas()
        {
            _deps.RingCanvas.Children.Clear();
            _canvasElements.Clear();
            var count = _canvasNodes.Count;

            var radius = 120.0 * Scale;
            var circleSize = 64.0 * Scale;
            var fontSize = 28.0 * Scale;
            var cx = CanvasViewport / 2;
            var cy = CanvasViewport / 2;

            if (count > 0)
            {
                var step = 360.0 / count;

                for (int i = 0; i < count; i++)
                {
                    var angle = i * step * Math.PI / 180;
                    var x = cx + radius * Math.Sin(angle);
                    var y = cy - radius * Math.Cos(angle);

                    var grid = new Grid { Width = circleSize, Height = circleSize };
                    Canvas.SetLeft(grid, x - circleSize / 2);
                    Canvas.SetTop(grid, y - circleSize / 2);

                    var isSelected = i == _selectedCanvasIndex;
                    grid.Children.Add(new Ellipse
                    {
                        Width = circleSize,
                        Height = circleSize,
                        Fill = isSelected ? ActiveFill : InactiveFill,
                        Stroke = isSelected ? ActiveStroke : null,
                        StrokeThickness = isSelected ? 2 : 0,
                    });

                    grid.Children.Add(new FontIcon
                    {
                        FontFamily = new FontFamily(MaterialIcons.FontFamilyName),
                        Glyph = _canvasNodes[i].Glyph,
                        FontSize = fontSize,
                        Foreground = isSelected ? ActiveForeground : InactiveForeground,
                    });
                    _deps.RingCanvas.Children.Add(grid);
                    _canvasElements.Add(grid);
                }
            }

            var ccs = 24.0 * Scale;
            var cg = new Grid { Width = ccs, Height = ccs };
            Canvas.SetLeft(cg, cx - ccs / 2);
            Canvas.SetTop(cg, cy - ccs / 2);
            cg.Children.Add(new Ellipse { Width = ccs, Height = ccs, Fill = CenterFill, Stroke = InactiveStroke, StrokeThickness = 1 });
            cg.Children.Add(new FontIcon
            {
                FontFamily = new FontFamily(MaterialIcons.FontFamilyName),
                Glyph = _canvasStack.Count > 0 ? "\uE5C4" : "\uE5CD",
                FontSize = 10,
                Foreground = InactiveForeground,
            });
            _deps.RingCanvas.Children.Add(cg);
        }

        public void PointerPressed(Windows.Foundation.Point position, PointerRoutedEventArgs e)
        {
            var hitIndex = HitTestNode(position);

            if (hitIndex >= 0)
            {
                _selectedCanvasIndex = hitIndex;
                _isDragging = true;
                _dragIndex = hitIndex;
                var cx = CanvasViewport / 2;
                var cy = CanvasViewport / 2;
                _dragStartAngle = Math.Atan2(position.X - cx, cy - position.Y);
                RenderCanvas();
                ShowNodeProperties(_canvasNodes[hitIndex]);
            }
            else if (_canvasStack.Count > 0 && IsHitCenterButton(position))
            {
                Back();
                return;
            }
            else
            {
                _selectedCanvasIndex = -1;
                HideNodeProperties();
                RenderCanvas();
            }
            _deps.RingCanvas.CapturePointer(e.Pointer);
        }

        public void PointerMoved(Windows.Foundation.Point position)
        {
            if (!_isDragging || _dragIndex < 0 || _canvasNodes.Count < 2) return;
            var cx = CanvasViewport / 2;
            var cy = CanvasViewport / 2;
            var currentAngle = Math.Atan2(position.X - cx, cy - position.Y);

            var count = _canvasNodes.Count;
            var step = 360.0 / count;
            var angleDeg = ((currentAngle * 180 / Math.PI) + 360) % 360;
            var nearestIndex = (int)Math.Round(angleDeg / step) % count;

            if (nearestIndex != _dragIndex)
            {
                var temp = _canvasNodes[_dragIndex];
                _canvasNodes.RemoveAt(_dragIndex);
                _canvasNodes.Insert(nearestIndex, temp);
                _dragIndex = nearestIndex;
                _selectedCanvasIndex = nearestIndex;
                RenderCanvas();
                ShowNodeProperties(_canvasNodes[nearestIndex]);
            }
        }

        public void PointerReleased(PointerRoutedEventArgs e)
        {
            _isDragging = false;
            _dragIndex = -1;
            _deps.RingCanvas.ReleasePointerCapture(e.Pointer);
        }

        public void PointerCanceled()
        {
            _isDragging = false;
            _dragIndex = -1;
        }

        public void ShowNodeProperties(RingNode node)
        {
            _deps.NodeEditDefaultText.Visibility = Visibility.Collapsed;
            _deps.NodeEditPanel.Visibility = Visibility.Visible;
            CollapseColors();

            _deps.PropGlyphPreview.FontFamily = new FontFamily(MaterialIcons.FontFamilyName);
            _deps.PropGlyphPreview.Glyph = node.Glyph;
            _deps.PropLabelDisplay.Text = node.Label;
            _deps.PropActionDisplay.Text = FormatActionTypeName(node.ActionType);
            _deps.PropLabelBox.Text = node.Label;

            _deps.PropUrlBox.Visibility = node.ActionType == ActionType.OpenUrl ? Visibility.Visible : Visibility.Collapsed;
            if (node.ActionType == ActionType.OpenUrl)
                _deps.PropUrlBox.Text = node.ActionData;

            _deps.PropAppPathSection.Visibility = node.ActionType == ActionType.LaunchApp ? Visibility.Visible : Visibility.Collapsed;
            if (node.ActionType == ActionType.LaunchApp)
                _deps.PropAppPathBox.Text = node.ActionData;

            _deps.EditSubmenuBtn.Visibility = node.ActionType == ActionType.Folder
                ? Visibility.Visible : Visibility.Collapsed;

            var showIconGallery = !IsToggleType(node.ActionType);
            _deps.IconSection.Visibility = showIconGallery ? Visibility.Visible : Visibility.Collapsed;
            if (showIconGallery) EnsureIconGalleryLoaded();

            _suppressActionTypeChange = true;
            foreach (ActionTypeItem ati in _deps.PropActionTypeBox.Items)
            {
                if (ati.Type == node.ActionType)
                {
                    _deps.PropActionTypeBox.SelectedItem = ati;
                    _lastActionTypeItem = ati;
                    break;
                }
            }
            _suppressActionTypeChange = false;
        }

        public void HideNodeProperties()
        {
            _deps.NodeEditPanel.Visibility = Visibility.Collapsed;
            _deps.NodeEditDefaultText.Visibility = Visibility.Visible;
            _selectedCanvasIndex = -1;
            RenderCanvas();
        }

        public void PropLabelChanged()
        {
            if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
            _canvasNodes[_selectedCanvasIndex].Label = _deps.PropLabelBox.Text;
            _deps.PropLabelDisplay.Text = _deps.PropLabelBox.Text;
            RenderCanvas();
        }

        public void PropActionTypeChanged()
        {
            try
            {
                if (_suppressActionTypeChange) return;
                if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
                if (_deps.PropActionTypeBox.SelectedItem is not ActionTypeItem selected) return;
                if (selected.IsSeparator)
                {
                    if (_lastActionTypeItem != null)
                        _deps.PropActionTypeBox.SelectedItem = _lastActionTypeItem;
                    return;
                }
                _lastActionTypeItem = selected;
                var newType = selected.Type ?? ActionType.None;

                var node = _canvasNodes[_selectedCanvasIndex];
                node.ActionType = newType;
                node.Category = GetCategoryForType(newType);

                var (defaultGlyph, defaultLabel) = GetDefaultsForAction(newType);
                node.Glyph = defaultGlyph;
                node.Label = defaultLabel;
                _deps.PropLabelBox.Text = defaultLabel;
                _deps.PropLabelDisplay.Text = defaultLabel;
                _deps.PropGlyphPreview.Glyph = defaultGlyph;
                node.ActionData = newType switch
                {
                    ActionType.LaunchApp => "calc",
                    ActionType.OpenUrl => "",
                    ActionType.TextExpansion => "",
                    _ => ""
                };
                _deps.PropActionDisplay.Text = FormatActionTypeName(newType);

                _deps.PropUrlBox.Visibility = newType == ActionType.OpenUrl ? Visibility.Visible : Visibility.Collapsed;
                if (newType == ActionType.OpenUrl)
                    _deps.PropUrlBox.Text = node.ActionData;

                _deps.PropAppPathSection.Visibility = newType == ActionType.LaunchApp ? Visibility.Visible : Visibility.Collapsed;
                if (newType == ActionType.LaunchApp)
                    _deps.PropAppPathBox.Text = node.ActionData;

                var showIcon = !IsToggleType(newType);
                _deps.IconSection.Visibility = showIcon ? Visibility.Visible : Visibility.Collapsed;
                if (showIcon) EnsureIconGalleryLoaded();

                _deps.EditSubmenuBtn.Visibility = newType == ActionType.Folder
                    ? Visibility.Visible : Visibility.Collapsed;
                RenderCanvas();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CanvasEditor] PropActionTypeBox: {ex.Message}"); }
        }

        public void PropUrlChanged()
        {
            if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
            _canvasNodes[_selectedCanvasIndex].ActionData = _deps.PropUrlBox.Text;
        }

        public void PropAppPathChanged()
        {
            if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
            _canvasNodes[_selectedCanvasIndex].ActionData = _deps.PropAppPathBox.Text;
        }

        public void AddNode(string glyph, ActionType type, string label)
        {
            var (defaultGlyph, defaultLabel) = GetDefaultsForAction(type);
            var newNode = new RingNode
            {
                Glyph = !string.IsNullOrEmpty(glyph) ? glyph : defaultGlyph,
                Label = !string.IsNullOrEmpty(label) ? label : defaultLabel,
                ActionType = type,
                Category = GetCategoryForType(type),
                ActionData = type switch
                {
                    ActionType.OpenUrl => "",
                    ActionType.Folder => "",
                    _ => ""
                },
            };
            if (type == ActionType.Folder)
                newNode.Children = new List<RingNode>();

            _activeNodes.Add(newNode);
            _selectedCanvasIndex = _activeNodes.Count - 1;
            RenderCanvas();
            ShowNodeProperties(newNode);
        }

        public void EnterSubmenu()
        {
            if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
            var node = _canvasNodes[_selectedCanvasIndex];
            if (node.ActionType != ActionType.Folder) return;
            if (node.Children == null) node.Children = new List<RingNode>();

            _canvasStack.Push((_canvasNodes, _deps.CanvasTitle.Text, _activeNodes));
            _activeNodes = node.Children;
            _canvasNodes = _activeNodes;
            _deps.CanvasTitle.Text = node.Label;
            _deps.CanvasBackBtn.Visibility = Visibility.Visible;
            _selectedCanvasIndex = -1;
            HideNodeProperties();
            RenderCanvas();
        }

        public void Back()
        {
            if (_canvasStack.Count == 0) return;
            var (nodes, title, parentNodes) = _canvasStack.Pop();
            _activeNodes = parentNodes;
            _canvasNodes = _activeNodes;
            _deps.CanvasTitle.Text = title;
            _deps.CanvasBackBtn.Visibility = _canvasStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _selectedCanvasIndex = -1;
            HideNodeProperties();
            RenderCanvas();
        }

        public void PrimaryColorChanged(Color newColor, RingProfile? profile)
        {
            if (_suppressColorChange || profile == null) return;
            var primary = newColor;
            var secondary = ComplementaryColor(primary);

            ((UIElement)_deps.SecondaryPreview).SetValue(Control.BackgroundProperty, new SolidColorBrush(secondary));

            profile.PrimaryColor = ColorToHex(primary);
            profile.SecondaryColor = ColorToHex(secondary);
            ProfileManager?.Save();
            SyncCanvasColors(primary, secondary);
            RenderCanvas();
        }

        public void PopulateActionTypeBox(DataTemplate itemTemplate)
        {
            _deps.PropActionTypeBox.ItemTemplate = itemTemplate;
            var items = new List<ActionTypeItem>();

            void AddItem(ActionType at, string name, string glyph) =>
                items.Add(new ActionTypeItem { Type = at, Name = name, Glyph = glyph });

            void AddSeparator() =>
                items.Add(new ActionTypeItem { IsSeparator = true });

            AddSeparator();
            AddItem(ActionType.VolumeControl, "Volume Control", "\uE050");
            AddItem(ActionType.BrightnessControl, "Brightness Control", "\uE3AB");

            AddSeparator();
            AddItem(ActionType.Folder, "Playback Control", "\uF6B5");

            AddSeparator();
            AddItem(ActionType.MediaPlayPause, "Play / Pause", "\uE037");
            AddItem(ActionType.MediaNext, "Next Track", "\uE044");
            AddItem(ActionType.MediaPrevious, "Previous Track", "\uE045");
            AddItem(ActionType.MediaSeekForward, "Seek Forward", "\uEAC9");
            AddItem(ActionType.MediaSeekBackward, "Seek Backward", "\uEAC3");

            AddSeparator();
            AddItem(ActionType.VolumeUp, "Volume Up", "\uE050");
            AddItem(ActionType.VolumeDown, "Volume Down", "\uE04D");
            AddItem(ActionType.MuteToggle, "Mute Toggle", "\uE710");

            AddSeparator();
            AddItem(ActionType.BrightnessUp, "Brightness Up", "\uE3AC");
            AddItem(ActionType.BrightnessDown, "Brightness Down", "\uE3FA");

            AddSeparator();
            AddItem(ActionType.LaunchApp, "Launch App", "\uEF40");
            AddItem(ActionType.OpenUrl, "Open URL", "\uE250");

            AddSeparator();
            AddItem(ActionType.CloseOsd, "Close OSD", "\uE5CD");
            AddItem(ActionType.ToggleNightLight, "Night Light Toggle", "\uF159");
            AddItem(ActionType.TextExpansion, "Text Expansion", "\uE14F");

            AddSeparator();
            AddItem(ActionType.None, "None", "\uF08C");

            foreach (var item in items)
                _deps.PropActionTypeBox.Items.Add(item);
        }

        public void PopulateIconGallery()
        {
            _iconGalleryLoaded = false;
            _deps.IconGallery.ItemsSource = null;
        }

        public void SearchIcons()
        {
            var search = _deps.IconSearchBox.Text?.Trim().ToLowerInvariant() ?? "";
            _deps.IconGallery.ItemsSource = string.IsNullOrEmpty(search)
                ? MaterialIcons.AllIcons.Take(200).ToList()
                : MaterialIcons.AllIcons
                    .Where(icon => icon.DisplayName.ToLowerInvariant().Contains(search) ||
                                  icon.Name.ToLowerInvariant().Contains(search))
                    .Take(500).ToList();
        }

        public void IconSearchChanged()
        {
            if (!_iconGalleryLoaded)
            {
                if (_deps.IconSection.Visibility != Visibility.Visible) return;
                _iconGalleryLoaded = true;
            }
            SearchIcons();
        }

        public void IconSelected()
        {
            if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
            if (_deps.IconGallery.SelectedItem is not MaterialIconInfo icon) return;
            var node = _canvasNodes[_selectedCanvasIndex];
            node.Glyph = icon.Glyph;
            _deps.PropGlyphPreview.Glyph = icon.Glyph;
            RenderCanvas();
        }

        public bool IsOnCanvasNode => _selectedCanvasIndex >= 0 && _selectedCanvasIndex < _canvasNodes.Count;
        public RingNode? SelectedNode => IsOnCanvasNode ? _canvasNodes[_selectedCanvasIndex] : null;

        public List<RingNode> GetActiveNodes() => _activeNodes;
        public void SetAppPathOnSelected(string path)
        {
            if (_selectedCanvasIndex >= 0 && _selectedCanvasIndex < _canvasNodes.Count)
                _canvasNodes[_selectedCanvasIndex].ActionData = path;
        }

        // --- Private helpers ---

        private int HitTestNode(Windows.Foundation.Point position)
        {
            for (int i = 0; i < _canvasElements.Count; i++)
            {
                var el = _canvasElements[i];
                var left = Canvas.GetLeft(el);
                var top = Canvas.GetTop(el);
                if (position.X >= left && position.X <= left + el.Width &&
                    position.Y >= top && position.Y <= top + el.Height)
                    return i;
            }
            return -1;
        }

        private bool IsHitCenterButton(Windows.Foundation.Point position)
        {
            var cx = CanvasViewport / 2;
            var cy = CanvasViewport / 2;
            var halfSize = 12.0 * Scale;
            return position.X >= cx - halfSize && position.X <= cx + halfSize &&
                   position.Y >= cy - halfSize && position.Y <= cy + halfSize;
        }

        private void EnsureIconGalleryLoaded()
        {
            if (_iconGalleryLoaded) return;
            _iconGalleryLoaded = true;
            SearchIcons();
        }

        private void SelectProfileColors()
        {
            var profile = ProfileManager?.Profiles.ElementAtOrDefault(ProfileManager?.SelectedIndex ?? -1);
            if (profile == null) return;
            _suppressColorChange = true;
            if (TryParseColor(profile.PrimaryColor, out var primary))
            {
                _deps.PrimaryColorPicker.Color = primary;
            }
            if (TryParseColor(profile.SecondaryColor, out var secondary))
                ((UIElement)_deps.SecondaryPreview).SetValue(Control.BackgroundProperty, new SolidColorBrush(secondary));
            if (TryParseColor(profile.PrimaryColor, out var primColor) &&
                TryParseColor(profile.SecondaryColor, out var secColor))
                SyncCanvasColors(primColor, secColor);
            _suppressColorChange = false;
        }

        private void SyncCanvasColors(Color primary, Color secondary)
        {
            ActiveFill = new SolidColorBrush(primary);
            ActiveForeground = (byte)((primary.R * 299 + primary.G * 587 + primary.B * 114) / 1000) > 128
                ? new SolidColorBrush(Color.FromArgb(255, 0, 0, 0))
                : new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            ActiveStroke = new SolidColorBrush(Color.FromArgb(255,
                (byte)(255 - primary.R), (byte)(255 - primary.G), (byte)(255 - primary.B)));

            InactiveFill = new SolidColorBrush(secondary);
            var bgLum = (byte)((secondary.R * 299 + secondary.G * 587 + secondary.B * 114) / 1000);
            InactiveForeground = bgLum > 128
                ? new SolidColorBrush(Color.FromArgb(255, 0, 0, 0))
                : new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            InactiveStroke = new SolidColorBrush(Color.FromArgb(255, 102, 102, 102));
        }

        private void CollapseColors(bool animate = false)
        {
            if (!_colorsExpanded) return;
            if (animate)
            {
                var currentHeight = _deps.ColorBody.ActualHeight;
                if (currentHeight <= 0) currentHeight = 300;
                _deps.ColorBody.Height = currentHeight;
                var sb = new Storyboard();
                var da = new DoubleAnimation
                {
                    From = currentHeight,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EnableDependentAnimation = true
                };
                da.Completed += (_, _) => _deps.ColorBody.Height = 0;
                Storyboard.SetTarget(da, _deps.ColorBody);
                Storyboard.SetTargetProperty(da, "Height");
                sb.Children.Add(da);
                sb.Begin();
            }
            else
            {
                _deps.ColorBody.Height = 0;
            }
            _deps.ColorChevron.Glyph = "\uE316";
            _colorsExpanded = false;
        }

        public void ExpandColors()
        {
            if (_colorsExpanded) return;
            _deps.ColorBody.Height = double.NaN;
            _deps.ColorBody.UpdateLayout();
            var targetHeight = _deps.ColorBody.ActualHeight;
            if (targetHeight <= 0) targetHeight = 300;
            _deps.ColorBody.Height = 0;
            var sb = new Storyboard();
            var da = new DoubleAnimation
            {
                From = 0,
                To = targetHeight,
                Duration = TimeSpan.FromMilliseconds(250),
                EnableDependentAnimation = true
            };
            da.Completed += (_, _) => _deps.ColorBody.Height = double.NaN;
            Storyboard.SetTarget(da, _deps.ColorBody);
            Storyboard.SetTargetProperty(da, "Height");
            sb.Children.Add(da);
            sb.Begin();
            _deps.ColorChevron.Glyph = "\uE313";
            _colorsExpanded = true;
        }

        public void ToggleColors()
        {
            if (_colorsExpanded)
                CollapseColors(animate: true);
            else
                ExpandColors();
        }

        private static string FormatActionTypeName(ActionType at) => at switch
        {
            ActionType.Folder => "Playback Control",
            ActionType.LaunchApp => "Launch App",
            ActionType.OpenUrl => "Open URL",
            ActionType.VolumeUp => "Volume Up",
            ActionType.VolumeDown => "Volume Down",
            ActionType.MuteToggle => "Mute Toggle",
            ActionType.BrightnessUp => "Brightness Up",
            ActionType.BrightnessDown => "Brightness Down",
            ActionType.MediaPlayPause => "Play / Pause",
            ActionType.MediaNext => "Next Track",
            ActionType.MediaPrevious => "Previous Track",
            ActionType.MediaSeekForward => "Seek Forward",
            ActionType.MediaSeekBackward => "Seek Backward",
            ActionType.TextExpansion => "Text Expansion",
            ActionType.CloseOsd => "Close OSD",
            ActionType.ToggleNightLight => "Night Light Toggle",
            ActionType.VolumeControl => "Volume Control",
            ActionType.BrightnessControl => "Brightness Control",
            ActionType.None => "None",
            _ => at.ToString()
        };

        private static bool IsToggleType(ActionType type) => type switch
        {
            ActionType.MuteToggle => true,
            ActionType.MediaPlayPause => true,
            ActionType.ToggleNightLight => true,
            _ => false
        };

        private static ActionCategory GetCategoryForType(ActionType type) => type switch
        {
            ActionType.VolumeControl or ActionType.BrightnessControl => ActionCategory.Group,
            ActionType.Folder => ActionCategory.Folder,
            _ => ActionCategory.Individual
        };

        private static (string glyph, string label) GetDefaultsForAction(ActionType type) => type switch
        {
            ActionType.VolumeUp => ("\uE050", "Volume Up"),
            ActionType.VolumeDown => ("\uE04D", "Volume Down"),
            ActionType.MuteToggle => ("\uE710", "Mute"),
            ActionType.VolumeControl => ("\uE050", "Volume"),
            ActionType.BrightnessUp => ("\uE1AC", "Brightness Up"),
            ActionType.BrightnessDown => ("\uE1AD", "Brightness Down"),
            ActionType.BrightnessControl => ("\uE1AC", "Brightness"),
            ActionType.MediaPlayPause => ("\uEF6A", "Play"),
            ActionType.MediaNext => ("\uE044", "Next"),
            ActionType.MediaPrevious => ("\uE045", "Prev"),
            ActionType.MediaSeekForward => ("\uEAC9", "Seek"),
            ActionType.MediaSeekBackward => ("\uE020", "Rewind"),
            ActionType.Folder => ("\uF6B5", "Playback Control"),
            ActionType.LaunchApp => ("\uEA5F", "App"),
            ActionType.OpenUrl => ("\uE250", "Website"),
            ActionType.TextExpansion => ("\uE86F", "Type"),
            ActionType.ToggleNightLight => ("\uF03D", "Night Light"),
            ActionType.CloseOsd => ("\uE5CD", "Close"),
            _ => ("\uE8B8", "Action"),
        };

        private static string ColorToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

        private static Color ComplementaryColor(Color c)
        {
            var r = (byte)(255 - c.R);
            var g = (byte)(255 - c.G);
            var b = (byte)(255 - c.B);
            return Color.FromArgb(255,
                (byte)((r + 255) / 2),
                (byte)((g + 255) / 2),
                (byte)((b + 255) / 2));
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            color = Color.FromArgb(0, 0, 0, 0);
            try
            {
                if (hex.Length >= 7 && hex[0] == '#')
                {
                    var a = hex.Length >= 9 ? System.Convert.ToByte(hex.Substring(1, 2), 16) : (byte)255;
                    var r = System.Convert.ToByte(hex.Substring(hex.Length - 6, 2), 16);
                    var g = System.Convert.ToByte(hex.Substring(hex.Length - 4, 2), 16);
                    var b = System.Convert.ToByte(hex.Substring(hex.Length - 2, 2), 16);
                    color = Color.FromArgb(a, r, g, b);
                    return true;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CanvasEditor] TryParseColor: {ex.Message}"); }
            return false;
        }
    }
}
