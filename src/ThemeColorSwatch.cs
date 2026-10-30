using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MCDT
{
    public sealed class ThemeColorSwatch : Control, IThemeAwareControl
    {
        private readonly Color _color;
        private bool _selected;
        private bool _hover;

        public ThemeColorSwatch(Color color, string name)
        {
            _color = color;
            Name = name;
            Size = new Size(26, 26);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public Color SwatchColor
        {
            get { return _color; }
        }

        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value)
                    return;
                _selected = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (_selected)
            {
                using (Pen pen = new Pen(Theme.Text, 2f))
                    graphics.DrawEllipse(pen, 1.5f, 1.5f, 23f, 23f);
            }
            else if (_hover)
            {
                using (Pen pen = new Pen(Theme.Muted, 1.5f))
                    graphics.DrawEllipse(pen, 1.5f, 1.5f, 23f, 23f);
            }
            using (SolidBrush brush = new SolidBrush(_color))
                graphics.FillEllipse(brush, 4.5f, 4.5f, 17f, 17f);
        }

        public void ApplyThemeColors()
        {
            Invalidate();
        }
    }
}
