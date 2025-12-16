using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;

namespace BackupCleaner
{
    public static class IconGenerator
    {
        public static Icon CreateAppIcon()
        {
            // Maak een 256x256 bitmap
            using var bitmap = new Bitmap(256, 256);
            using var g = Graphics.FromImage(bitmap);
            
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            
            // Achtergrond - rounded rectangle
            var bgBrush = new SolidBrush(Color.FromArgb(30, 58, 95)); // #1E3A5F
            var bgRect = new Rectangle(16, 16, 224, 224);
            FillRoundedRectangle(g, bgBrush, bgRect, 40);
            
            // Folder icoon tekenen
            using var folderBrush = new SolidBrush(Color.FromArgb(255, 107, 53)); // #FF6B35 accent
            using var folderPen = new Pen(Color.White, 8);
            
            // Folder body
            var folderPath = new GraphicsPath();
            folderPath.AddArc(50, 80, 30, 30, 180, 90);
            folderPath.AddLine(65, 65, 110, 65);
            folderPath.AddLine(110, 65, 130, 85);
            folderPath.AddLine(130, 85, 200, 85);
            folderPath.AddArc(186, 85, 30, 30, 270, 90);
            folderPath.AddLine(216, 100, 216, 180);
            folderPath.AddArc(186, 166, 30, 30, 0, 90);
            folderPath.AddLine(200, 196, 56, 196);
            folderPath.AddArc(40, 166, 30, 30, 90, 90);
            folderPath.AddLine(40, 180, 40, 95);
            folderPath.CloseFigure();
            
            g.FillPath(folderBrush, folderPath);
            g.DrawPath(new Pen(Color.White, 4), folderPath);
            
            // Broom/cleanup symbool (simpele versie - vinkje)
            using var checkPen = new Pen(Color.White, 12) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(checkPen, 85, 140, 115, 165);
            g.DrawLine(checkPen, 115, 165, 175, 110);
            
            // Convert bitmap naar icon
            return Icon.FromHandle(bitmap.GetHicon());
        }

        public static void SaveIconToFile(string path)
        {
            using var icon = CreateAppIcon();
            using var fs = new FileStream(path, FileMode.Create);
            icon.Save(fs);
        }

        private static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
            path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
            path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
            path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }
    }
}

