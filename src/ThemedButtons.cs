using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MCDT
{
    public sealed class ThemedRadioButton : RadioButton
    {
        private const int GlyphSize = 15;
        private bool _hover;

        public ThemedRadioButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Page;
            ForeColor = Theme.Text;
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
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int top = (ClientSize.Height - GlyphSize) / 2;
            Rectangle glyph = new Rectangle(1, top, GlyphSize, GlyphSize);
            Color border = !Enabled ? Theme.Border : (Checked || _hover ? Theme.Accent : Theme.BorderStrong);
            Color fill = Enabled ? Theme.Surface : Theme.PageAlt;
            using (SolidBrush fillBrush = new SolidBrush(fill))
                graphics.FillEllipse(fillBrush, glyph);
            using (Pen pen = new Pen(Enabled ? border : Theme.Border, Checked ? 2F : 1.4F))
                graphics.DrawEllipse(pen, glyph);

            if (Checked)
            {
                Rectangle dot = new Rectangle(glyph.X + 4, glyph.Y + 4, GlyphSize - 8, GlyphSize - 8);
                using (SolidBrush dotBrush = new SolidBrush(Enabled ? Theme.Accent : Theme.Muted))
                    graphics.FillEllipse(dotBrush, dot);
            }

            DrawCaption(graphics);
        }

        private void DrawCaption(Graphics graphics)
        {
            int left = GlyphSize + 6;
            Rectangle bounds = new Rectangle(left, 0, Math.Max(0, ClientSize.Width - left), ClientSize.Height);
            Color color = Enabled ? ForeColor : Theme.Muted;
            TextRenderer.DrawText(graphics, Text, Font, bounds, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(graphics, bounds, color, BackColor);
        }
    }

    public sealed class ThemedCheckBox : CheckBox
    {
        private const int GlyphSize = 15;
        private bool _hover;

        public ThemedCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Page;
            ForeColor = Theme.Text;
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
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int top = (ClientSize.Height - GlyphSize) / 2;
            Rectangle glyph = new Rectangle(1, top, GlyphSize, GlyphSize);
            Color border = !Enabled ? Theme.Border : (Checked || _hover ? Theme.Accent : Theme.BorderStrong);
            Color fill;
            if (Checked)
                fill = Enabled ? Theme.Accent : Theme.BorderStrong;
            else
                fill = Enabled ? Theme.Surface : Theme.PageAlt;

            using (GraphicsPath path = RoundedRect(glyph, 3))
            {
                using (SolidBrush fillBrush = new SolidBrush(fill))
                    graphics.FillPath(fillBrush, path);
                using (Pen pen = new Pen(Enabled ? border : Theme.Border, 1.4F))
                    graphics.DrawPath(pen, path);
            }

            if (Checked)
            {
                using (Pen pen = new Pen(Color.White, 1.8F))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    graphics.DrawLines(pen, new PointF[]
                    {
                        new PointF(glyph.X + 3.6F, glyph.Y + 7.6F),
                        new PointF(glyph.X + 6.4F, glyph.Y + 10.4F),
                        new PointF(glyph.X + 11.6F, glyph.Y + 4.2F)
                    });
                }
            }

            DrawCaption(graphics);
        }

        private void DrawCaption(Graphics graphics)
        {
            int left = GlyphSize + 6;
            Rectangle bounds = new Rectangle(left, 0, Math.Max(0, ClientSize.Width - left), ClientSize.Height);
            Color color = Enabled ? ForeColor : Theme.Muted;
            TextRenderer.DrawText(graphics, Text, Font, bounds, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(graphics, bounds, color, BackColor);
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
