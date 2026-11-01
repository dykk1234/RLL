using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LiteLauncher
{
    /// <summary>
    /// 侧边导航用的矢量图标。全部用 GraphicsPath 绘制，不依赖任何外部图片，
    /// 因此可以随主题色自动着色，也避免了引入第三方素材的版权问题。
    /// 每个图标都在 20x20 的逻辑坐标系里设计，由调用方传入实际矩形做缩放。
    /// </summary>
    internal static class NavGlyphs
    {
        public enum Glyph
        {
            Play,
            Account,
            Download,
            Mods,
            Settings,
            Log,
            About
        }

        public static Glyph FromIndex(int index)
        {
            switch (index)
            {
                case 0:
                    return Glyph.Play;
                case 1:
                    return Glyph.Account;
                case 2:
                    return Glyph.Download;
                case 3:
                    return Glyph.Mods;
                case 4:
                    return Glyph.Settings;
                case 5:
                    return Glyph.Log;
                default:
                    return Glyph.About;
            }
        }

        /// <summary>
        /// 在指定矩形内绘制图标。color 决定描边/填充色，selected 时通常传白色。
        /// </summary>
        public static void Draw(Graphics graphics, Glyph glyph, RectangleF bounds, Color color)
        {
            if (graphics == null || bounds.Width <= 1 || bounds.Height <= 1)
            {
                return;
            }
            GraphicsState state = graphics.Save();
            try
            {
                // 统一缩放：把 20x20 的设计坐标映射到目标矩形。
                graphics.TranslateTransform(bounds.Left, bounds.Top);
                graphics.ScaleTransform(bounds.Width / 20f, bounds.Height / 20f);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(color, 1.4f))
                using (SolidBrush brush = new SolidBrush(color))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    switch (glyph)
                    {
                        case Glyph.Play:
                            DrawPlay(graphics, pen, brush);
                            break;
                        case Glyph.Account:
                            DrawAccount(graphics, pen, brush);
                            break;
                        case Glyph.Download:
                            DrawDownload(graphics, pen, brush);
                            break;
                        case Glyph.Mods:
                            DrawMods(graphics, pen, brush);
                            break;
                        case Glyph.Settings:
                            DrawSettings(graphics, pen, brush);
                            break;
                        case Glyph.Log:
                            DrawLog(graphics, pen, brush);
                            break;
                        default:
                            DrawAbout(graphics, pen, brush);
                            break;
                    }
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private static void DrawPlay(Graphics graphics, Pen pen, SolidBrush brush)
        {
            // Triangle pointing right, with a small optical inset.
            PointF[] points = new PointF[]
            {
                new PointF(6f, 4f),
                new PointF(15f, 10f),
                new PointF(6f, 16f)
            };
            graphics.FillPolygon(brush, points);
        }

        private static void DrawAccount(Graphics graphics, Pen pen, SolidBrush brush)
        {
            // Head circle plus shoulder arc.
            graphics.DrawEllipse(pen, 6.5f, 3f, 7f, 7f);
            graphics.DrawArc(pen, 3.5f, 11f, 13f, 9f, 180, 180);
        }

        private static void DrawDownload(Graphics graphics, Pen pen, SolidBrush brush)
        {
            // Arrow down into a tray.
            graphics.DrawLine(pen, 10f, 3f, 10f, 12f);
            graphics.DrawLine(pen, 6.5f, 8.5f, 10f, 12f);
            graphics.DrawLine(pen, 13.5f, 8.5f, 10f, 12f);
            graphics.DrawLine(pen, 4f, 15.5f, 16f, 15.5f);
        }

        private static void DrawMods(Graphics graphics, Pen pen, SolidBrush brush)
        {
            // Four rounded blocks: represents mod slots.
            graphics.DrawRectangle(pen, 3.5f, 3.5f, 5.5f, 5.5f);
            graphics.DrawRectangle(pen, 11f, 3.5f, 5.5f, 5.5f);
            graphics.DrawRectangle(pen, 3.5f, 11f, 5.5f, 5.5f);
            graphics.DrawRectangle(pen, 11f, 11f, 5.5f, 5.5f);
        }

        private static void DrawSettings(Graphics graphics, Pen pen, SolidBrush brush)
        {
            // Gear approximation: center circle plus four studs.
            graphics.DrawEllipse(pen, 7f, 7f, 6f, 6f);
            graphics.DrawLine(pen, 10f, 1.5f, 10f, 4.5f);
            graphics.DrawLine(pen, 10f, 15.5f, 10f, 18.5f);
            graphics.DrawLine(pen, 1.5f, 10f, 4.5f, 10f);
            graphics.DrawLine(pen, 15.5f, 10f, 18.5f, 10f);
            graphics.DrawLine(pen, 4f, 4f, 6.2f, 6.2f);
            graphics.DrawLine(pen, 13.8f, 13.8f, 16f, 16f);
            graphics.DrawLine(pen, 16f, 4f, 13.8f, 6.2f);
            graphics.DrawLine(pen, 6.2f, 13.8f, 4f, 16f);
        }

        private static void DrawLog(Graphics graphics, Pen pen, SolidBrush brush)
        {
            // Document with text lines.
            graphics.DrawRectangle(pen, 4f, 3f, 12f, 14f);
            graphics.DrawLine(pen, 6.5f, 7f, 13.5f, 7f);
            graphics.DrawLine(pen, 6.5f, 10f, 13.5f, 10f);
            graphics.DrawLine(pen, 6.5f, 13f, 11f, 13f);
        }

        private static void DrawAbout(Graphics graphics, Pen pen, SolidBrush brush)
        {
            // Info circle with a dot and stem.
            graphics.DrawEllipse(pen, 3f, 3f, 14f, 14f);
            graphics.FillEllipse(brush, 9f, 6f, 2f, 2f);
            graphics.DrawLine(pen, 10f, 9.5f, 10f, 14.5f);
        }
    }
}
