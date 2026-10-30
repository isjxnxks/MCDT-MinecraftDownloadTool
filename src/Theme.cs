using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MCDT
{
    public interface IThemeAwareControl
    {
        void ApplyThemeColors();
    }

    public sealed class ThemeColorPreset
    {
        public string Name { get; private set; }
        public string Hex { get; private set; }
        public Color Color { get; private set; }

        public ThemeColorPreset(string name, string hex)
        {
            Name = name;
            Hex = hex;
            Color = Theme.ParseHex(hex);
        }
    }

    public static class Theme
    {
        private enum Slot
        {
            Page,
            PageAlt,
            Surface,
            SurfaceAlt,
            AccentSoft,
            SelectedBack,
            SelectedBorder,
            HoverBack,
            ButtonHover,
            ButtonDown,
            TabHover,
            Border,
            BorderStrong,
            InfoBorder,
            Text,
            TextStrong,
            Muted,
            MutedAlt,
            MetaText,
            FileText,
            Accent,
            AccentHover,
            AccentDown,
            Danger,
            Warning,
            InfoTitle,
            InfoSubtitle,
            LogText,
            StageText,
            TitleColor,
            HeaderStart,
            HeaderEnd,
            HeaderLogoBack,
            HeaderSubtitle,
            HeaderBadgeText,
            HeaderBadgeBack,
            Count
        }

        public const string DefaultColorHex = "#3C8527";

        public static readonly ThemeColorPreset[] ColorPresets = new ThemeColorPreset[]
        {
            new ThemeColorPreset("草原绿", Theme.DefaultColorHex),
            new ThemeColorPreset("天空蓝", "#2E6FD8"),
            new ThemeColorPreset("海洋青", "#0E8C8C"),
            new ThemeColorPreset("紫罗兰", "#7A4FCC"),
            new ThemeColorPreset("落日橙", "#CC6A1A"),
            new ThemeColorPreset("樱花粉", "#D65A8C")
        };

        private static bool _dark = AppSettings.DarkTheme;
        private static Color _seedColor = ParseHex(AppSettings.ThemeColor);
        private static readonly Color[] _lightColors = new Color[(int)Slot.Count];
        private static readonly Color[] _darkColors = new Color[(int)Slot.Count];
        private static readonly Dictionary<int, Color> _transition = new Dictionary<int, Color>();

        static Theme()
        {
            BuildPalette(_seedColor, _lightColors, _darkColors);
        }

        public static bool IsDark
        {
            get { return _dark; }
        }

        public static void SetDark(bool dark)
        {
            _dark = dark;
        }

        public static string CurrentColorHex
        {
            get { return ToHex(_seedColor); }
        }

        public static Color CurrentColor
        {
            get { return _seedColor; }
        }

        public static void SetAccent(Color color)
        {
            if (color.ToArgb() == _seedColor.ToArgb())
                return;
            Color[] light = new Color[(int)Slot.Count];
            Color[] dark = new Color[(int)Slot.Count];
            BuildPalette(color, light, dark);

            _transition.Clear();
            for (int i = 0; i < (int)Slot.Count; i++)
            {
                if (_lightColors[i].ToArgb() != light[i].ToArgb())
                    _transition[_lightColors[i].ToArgb()] = light[i];
                if (_darkColors[i].ToArgb() != dark[i].ToArgb())
                    _transition[_darkColors[i].ToArgb()] = dark[i];
            }
            Array.Copy(light, _lightColors, light.Length);
            Array.Copy(dark, _darkColors, dark.Length);
            _seedColor = color;
        }

        public static Color Page { get { return Get(Slot.Page); } }
        public static Color PageAlt { get { return Get(Slot.PageAlt); } }
        public static Color Surface { get { return Get(Slot.Surface); } }
        public static Color SurfaceAlt { get { return Get(Slot.SurfaceAlt); } }
        public static Color AccentSoft { get { return Get(Slot.AccentSoft); } }
        public static Color SelectedBack { get { return Get(Slot.SelectedBack); } }
        public static Color SelectedBorder { get { return Get(Slot.SelectedBorder); } }
        public static Color HoverBack { get { return Get(Slot.HoverBack); } }
        public static Color ButtonHover { get { return Get(Slot.ButtonHover); } }
        public static Color ButtonDown { get { return Get(Slot.ButtonDown); } }
        public static Color TabHover { get { return Get(Slot.TabHover); } }
        public static Color Border { get { return Get(Slot.Border); } }
        public static Color BorderStrong { get { return Get(Slot.BorderStrong); } }
        public static Color InfoBorder { get { return Get(Slot.InfoBorder); } }
        public static Color Text { get { return Get(Slot.Text); } }
        public static Color TextStrong { get { return Get(Slot.TextStrong); } }
        public static Color Muted { get { return Get(Slot.Muted); } }
        public static Color MutedAlt { get { return Get(Slot.MutedAlt); } }
        public static Color MetaText { get { return Get(Slot.MetaText); } }
        public static Color FileText { get { return Get(Slot.FileText); } }
        public static Color Accent { get { return Get(Slot.Accent); } }
        public static Color AccentHover { get { return Get(Slot.AccentHover); } }
        public static Color AccentDown { get { return Get(Slot.AccentDown); } }
        public static Color Danger { get { return Get(Slot.Danger); } }
        public static Color Warning { get { return Get(Slot.Warning); } }
        public static Color InfoTitle { get { return Get(Slot.InfoTitle); } }
        public static Color InfoSubtitle { get { return Get(Slot.InfoSubtitle); } }
        public static Color LogText { get { return Get(Slot.LogText); } }
        public static Color StageText { get { return Get(Slot.StageText); } }
        public static Color TitleColor { get { return Get(Slot.TitleColor); } }
        public static Color HeaderStart { get { return Get(Slot.HeaderStart); } }
        public static Color HeaderEnd { get { return Get(Slot.HeaderEnd); } }
        public static Color HeaderLogoBack { get { return Get(Slot.HeaderLogoBack); } }
        public static Color HeaderSubtitle { get { return Get(Slot.HeaderSubtitle); } }
        public static Color HeaderBadgeText { get { return Get(Slot.HeaderBadgeText); } }
        public static Color HeaderBadgeBack { get { return Get(Slot.HeaderBadgeBack); } }

        public static void EndColorTransition()
        {
            _transition.Clear();
        }

        private static Color Get(Slot slot)
        {
            return _dark ? _darkColors[(int)slot] : _lightColors[(int)slot];
        }

        public static Color ParseHex(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                string text = value.Trim();
                if (text.StartsWith("#", StringComparison.Ordinal))
                    text = text.Substring(1);
                if (text.Length == 6)
                {
                    int r;
                    int g;
                    int b;
                    if (int.TryParse(text.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
                        int.TryParse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
                        int.TryParse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                        return Color.FromArgb(r, g, b);
                }
            }
            return Color.FromArgb(60, 133, 39);
        }

        public static string ToHex(Color color)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}",
                color.R, color.G, color.B);
        }

        public static bool IsLight(Color color)
        {
            return RelativeLuminance(color) > 0.55;
        }

        private static void BuildPalette(Color seed, Color[] light, Color[] dark)
        {
            double h;
            double s;
            double l;
            RgbToHsl(seed, out h, out s, out l);
            if (s < 0.10)
                s = 0;
            else
                s = Clamp(s, 0.42, 0.80);
            l = Clamp(l, 0.28, 0.50);

            Color accentLight = EnsureWhiteContrast(Hsl(h, s, l), 3.0);
            Color accentHoverLight = EnsureWhiteContrast(Hsl(h, s, Math.Min(0.62, l + 0.06)), 2.8);
            Color accentDownLight = Hsl(h, s, Math.Max(0.18, l - 0.07));

            double darkBase = Clamp(l + 0.12, 0.38, 0.52);
            Color accentDark = EnsureWhiteContrast(Hsl(h, Math.Min(s, 0.68), darkBase), 2.3);
            Color accentHoverDark = EnsureWhiteContrast(Hsl(h, Math.Min(s, 0.68), Math.Min(0.62, darkBase + 0.06)), 2.2);
            Color accentDownDark = Hsl(h, Math.Min(s, 0.68), Math.Max(0.28, darkBase - 0.08));

            light[(int)Slot.Page] = Color.FromArgb(244, 246, 248);
            light[(int)Slot.PageAlt] = Color.FromArgb(247, 249, 251);
            light[(int)Slot.Surface] = Color.White;
            light[(int)Slot.SurfaceAlt] = Color.FromArgb(248, 250, 252);
            light[(int)Slot.HoverBack] = Color.FromArgb(245, 247, 249);
            light[(int)Slot.ButtonHover] = Color.FromArgb(238, 242, 245);
            light[(int)Slot.ButtonDown] = Color.FromArgb(225, 232, 237);
            light[(int)Slot.TabHover] = Color.FromArgb(240, 244, 247);

            dark[(int)Slot.Page] = Color.FromArgb(32, 34, 37);
            dark[(int)Slot.PageAlt] = Color.FromArgb(32, 34, 37);
            dark[(int)Slot.Surface] = Color.FromArgb(43, 46, 50);
            dark[(int)Slot.SurfaceAlt] = Color.FromArgb(37, 40, 44);
            dark[(int)Slot.HoverBack] = Color.FromArgb(51, 55, 60);
            dark[(int)Slot.ButtonHover] = Color.FromArgb(48, 53, 58);
            dark[(int)Slot.ButtonDown] = Color.FromArgb(56, 62, 66);
            dark[(int)Slot.TabHover] = Color.FromArgb(48, 52, 57);

            light[(int)Slot.AccentSoft] = Hsl(h, s * 0.38, 0.945);
            dark[(int)Slot.AccentSoft] = Hsl(h, s * 0.28, 0.19);
            light[(int)Slot.SelectedBack] = Hsl(h, s * 0.42, 0.925);
            dark[(int)Slot.SelectedBack] = Hsl(h, s * 0.34, 0.21);
            light[(int)Slot.SelectedBorder] = Hsl(h, s * 0.32, 0.72);
            dark[(int)Slot.SelectedBorder] = Hsl(h, s * 0.24, 0.34);

            light[(int)Slot.Border] = Hsl(h, s * 0.18, 0.87);
            dark[(int)Slot.Border] = Hsl(h, s * 0.08, 0.245);
            light[(int)Slot.BorderStrong] = Hsl(h, s * 0.24, 0.82);
            dark[(int)Slot.BorderStrong] = Hsl(h, s * 0.08, 0.29);
            light[(int)Slot.InfoBorder] = Hsl(h, s * 0.34, 0.84);
            dark[(int)Slot.InfoBorder] = Hsl(h, s * 0.28, 0.285);

            light[(int)Slot.Text] = Hsl(h, s * 0.10, 0.15);
            dark[(int)Slot.Text] = Hsl(h, s * 0.04, 0.915);
            Color textStrongDark = Hsl(h, s * 0.28, 0.83);
            light[(int)Slot.TextStrong] = Hsl(h, s * 0.50, 0.24);
            dark[(int)Slot.TextStrong] = textStrongDark;
            light[(int)Slot.InfoTitle] = Hsl(h, s * 0.50, 0.25);
            dark[(int)Slot.InfoTitle] = textStrongDark;
            light[(int)Slot.InfoSubtitle] = Hsl(h, s * 0.16, 0.41);
            dark[(int)Slot.InfoSubtitle] = Hsl(h, s * 0.12, 0.65);

            Color mutedDark = Hsl(h, 0.04, 0.62);
            light[(int)Slot.Muted] = Hsl(h, s * 0.06, 0.44);
            dark[(int)Slot.Muted] = mutedDark;
            light[(int)Slot.MutedAlt] = Hsl(h, s * 0.07, 0.42);
            dark[(int)Slot.MutedAlt] = mutedDark;
            light[(int)Slot.MetaText] = Hsl(h, s * 0.05, 0.49);
            dark[(int)Slot.MetaText] = mutedDark;
            light[(int)Slot.FileText] = Hsl(h, s * 0.04, 0.51);
            dark[(int)Slot.FileText] = mutedDark;

            light[(int)Slot.Accent] = accentLight;
            dark[(int)Slot.Accent] = accentDark;
            light[(int)Slot.AccentHover] = accentHoverLight;
            dark[(int)Slot.AccentHover] = accentHoverDark;
            light[(int)Slot.AccentDown] = accentDownLight;
            dark[(int)Slot.AccentDown] = accentDownDark;

            light[(int)Slot.Danger] = Color.FromArgb(190, 61, 48);
            dark[(int)Slot.Danger] = Color.FromArgb(224, 96, 82);
            light[(int)Slot.Warning] = Color.FromArgb(154, 105, 20);
            dark[(int)Slot.Warning] = Color.FromArgb(232, 176, 80);

            light[(int)Slot.LogText] = Hsl(h, s * 0.10, 0.23);
            dark[(int)Slot.LogText] = Hsl(h, 0.04, 0.82);
            light[(int)Slot.StageText] = Hsl(h, s * 0.12, 0.33);
            dark[(int)Slot.StageText] = Hsl(h, s * 0.08, 0.68);
            light[(int)Slot.TitleColor] = Hsl(h, s * 0.55, 0.18);
            dark[(int)Slot.TitleColor] = Hsl(h, s * 0.25, 0.85);

            light[(int)Slot.HeaderStart] = EnsureWhiteContrast(Hsl(h, s, 0.22), 4.0);
            light[(int)Slot.HeaderEnd] = EnsureWhiteContrast(Hsl(h, s, 0.38), 3.0);
            dark[(int)Slot.HeaderStart] = EnsureWhiteContrast(Hsl(h, s, 0.16), 4.0);
            dark[(int)Slot.HeaderEnd] = EnsureWhiteContrast(Hsl(h, s, 0.30), 3.0);

            Color logoBack = Hsl(h, 0.30, 0.97);
            light[(int)Slot.HeaderLogoBack] = logoBack;
            dark[(int)Slot.HeaderLogoBack] = logoBack;
            light[(int)Slot.HeaderSubtitle] = Hsl(h, s * 0.30, 0.86);
            dark[(int)Slot.HeaderSubtitle] = Hsl(h, s * 0.30, 0.80);
            light[(int)Slot.HeaderBadgeText] = Hsl(h, s * 0.25, 0.92);
            dark[(int)Slot.HeaderBadgeText] = Hsl(h, s * 0.25, 0.88);
            Color badge = Hsl(h, Math.Min(s, 0.70), 0.40);
            light[(int)Slot.HeaderBadgeBack] = Color.FromArgb(80, badge.R, badge.G, badge.B);
            dark[(int)Slot.HeaderBadgeBack] = Color.FromArgb(80, badge.R, badge.G, badge.B);
        }

        private static Color EnsureWhiteContrast(Color color, double minContrast)
        {
            double h;
            double s;
            double l;
            RgbToHsl(color, out h, out s, out l);
            for (int i = 0; i < 80; i++)
            {
                if (ContrastWithWhite(color) >= minContrast || l <= 0.05)
                    break;
                l = Math.Max(0.05, l - 0.01);
                color = Hsl(h, s, l);
            }
            return color;
        }

        private static double ContrastWithWhite(Color color)
        {
            return 1.05 / (RelativeLuminance(color) + 0.05);
        }

        private static double RelativeLuminance(Color color)
        {
            return 0.2126 * LinearChannel(color.R) +
                   0.7152 * LinearChannel(color.G) +
                   0.0722 * LinearChannel(color.B);
        }

        private static double LinearChannel(byte value)
        {
            double channel = value / 255.0;
            return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Min(max, Math.Max(min, value));
        }

        private static void RgbToHsl(Color color, out double h, out double s, out double l)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;
            if (Math.Abs(max - min) < 0.00001)
            {
                h = 0;
                s = 0;
                return;
            }
            double delta = max - min;
            s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);
            if (max == r)
                h = (g - b) / delta + (g < b ? 6.0 : 0.0);
            else if (max == g)
                h = (b - r) / delta + 2.0;
            else
                h = (r - g) / delta + 4.0;
            h *= 60.0;
        }

        private static Color Hsl(double h, double s, double l)
        {
            h = h % 360.0;
            if (h < 0)
                h += 360.0;
            s = Clamp(s, 0, 1);
            l = Clamp(l, 0, 1);
            double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
            double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
            double m = l - c / 2.0;
            double r = 0;
            double g = 0;
            double b = 0;
            if (h < 60)
            {
                r = c;
                g = x;
            }
            else if (h < 120)
            {
                r = x;
                g = c;
            }
            else if (h < 180)
            {
                g = c;
                b = x;
            }
            else if (h < 240)
            {
                g = x;
                b = c;
            }
            else if (h < 300)
            {
                r = x;
                b = c;
            }
            else
            {
                r = c;
                b = x;
            }
            return Color.FromArgb(
                (int)Math.Round((r + m) * 255.0),
                (int)Math.Round((g + m) * 255.0),
                (int)Math.Round((b + m) * 255.0));
        }

        public static void Apply(Control root)
        {
            if (root == null || root.IsDisposed)
                return;
            ApplyCore(root);
            try { root.Invalidate(true); }
            catch { }
        }

        private static void ApplyCore(Control control)
        {
            if (control == null || control.IsDisposed)
                return;
            IThemeAwareControl themed = control as IThemeAwareControl;
            if (themed != null)
                themed.ApplyThemeColors();
            control.BackColor = MapBack(control.BackColor);
            control.ForeColor = MapFore(control.ForeColor);

            Button button = control as Button;
            if (button != null)
            {
                button.FlatAppearance.BorderColor = MapBack(button.FlatAppearance.BorderColor);
                button.FlatAppearance.MouseOverBackColor = MapBack(button.FlatAppearance.MouseOverBackColor);
                button.FlatAppearance.MouseDownBackColor = MapBack(button.FlatAppearance.MouseDownBackColor);
            }

            LinkLabel link = control as LinkLabel;
            if (link != null)
            {
                link.LinkColor = MapFore(link.LinkColor);
                link.ActiveLinkColor = MapFore(link.ActiveLinkColor);
                link.VisitedLinkColor = MapFore(link.VisitedLinkColor);
            }

            if (control is CheckBox || control is RadioButton)
                control.BackColor = Theme.Page;

            for (int i = 0; i < control.Controls.Count; i++)
                ApplyCore(control.Controls[i]);
        }

        private static Color MapBack(Color color)
        {
            if (color.A == 0)
                return color;
            if (_dark && color.ToArgb() == Color.White.ToArgb())
                return Surface;
            if (!_dark && color.ToArgb() == _darkColors[(int)Slot.Surface].ToArgb())
                return Color.White;
            if (_dark)
            {
                int argb = color.ToArgb();
                if (argb == SystemColors.Control.ToArgb() || argb == Color.FromArgb(240, 240, 240).ToArgb())
                    return Page;
            }
            return MapPair(color);
        }

        private static Color MapFore(Color color)
        {
            if (color.A == 0)
                return color;
            if (color.ToArgb() == Color.White.ToArgb())
                return Color.White;
            if (_dark)
            {
                int argb = color.ToArgb();
                if (argb == Color.Black.ToArgb() || argb == SystemColors.ControlText.ToArgb())
                    return Text;
            }
            return MapPair(color);
        }

        private static Color MapPair(Color color)
        {
            int argb = color.ToArgb();
            Color mapped;
            if (_transition.TryGetValue(argb, out mapped))
                return mapped;
            for (int i = 0; i < (int)Slot.Count; i++)
            {
                if (argb == _lightColors[i].ToArgb() || argb == _darkColors[i].ToArgb())
                    return _dark ? _darkColors[i] : _lightColors[i];
            }
            return color;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        public static void ApplyWindowChrome(Form form)
        {
            if (form == null || !form.IsHandleCreated)
                return;
            int value = _dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref value, sizeof(int));
                SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027);
            }
            catch
            {
            }
        }
    }
}
