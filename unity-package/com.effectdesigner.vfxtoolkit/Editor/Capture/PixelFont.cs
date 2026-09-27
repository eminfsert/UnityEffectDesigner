using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Capture
{
    /// <summary>
    /// Minimal 3x5 bitmap font used to stamp time labels onto contact sheets
    /// without depending on font assets or GUI rendering.
    /// </summary>
    static class PixelFont
    {
        public const int GlyphWidth = 3;
        public const int GlyphHeight = 5;

        // Each glyph is 5 rows, top to bottom, 3 bits per row (MSB = left column).
        static int[] Glyph(char c)
        {
            switch (c)
            {
                case '0': return new[] { 7, 5, 5, 5, 7 };
                case '1': return new[] { 2, 6, 2, 2, 7 };
                case '2': return new[] { 7, 1, 7, 4, 7 };
                case '3': return new[] { 7, 1, 7, 1, 7 };
                case '4': return new[] { 5, 5, 7, 1, 1 };
                case '5': return new[] { 7, 4, 7, 1, 7 };
                case '6': return new[] { 7, 4, 7, 5, 7 };
                case '7': return new[] { 7, 1, 1, 1, 1 };
                case '8': return new[] { 7, 5, 7, 5, 7 };
                case '9': return new[] { 7, 5, 7, 1, 7 };
                case '.': return new[] { 0, 0, 0, 0, 2 };
                case 's': return new[] { 0, 3, 6, 1, 6 };
                case 't': return new[] { 2, 7, 2, 2, 3 };
                case '=': return new[] { 0, 7, 0, 7, 0 };
                default: return new[] { 0, 0, 0, 0, 0 };
            }
        }

        /// <summary>Draws text with its top-left corner at (x, yTop) in texture space (y grows upward).</summary>
        public static void Draw(Color32[] pixels, int texWidth, int texHeight, string text, int x, int yTop, int scale, Color32 color)
        {
            int cursor = x;
            foreach (char c in text)
            {
                var rows = Glyph(c);
                for (int row = 0; row < GlyphHeight; row++)
                {
                    for (int col = 0; col < GlyphWidth; col++)
                    {
                        if ((rows[row] & (1 << (GlyphWidth - 1 - col))) == 0)
                            continue;
                        for (int sy = 0; sy < scale; sy++)
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int px = cursor + col * scale + sx;
                            int py = yTop - row * scale - sy;
                            if (px < 0 || py < 0 || px >= texWidth || py >= texHeight)
                                continue;
                            pixels[py * texWidth + px] = color;
                        }
                    }
                }
                cursor += (GlyphWidth + 1) * scale;
            }
        }

        public static int MeasureWidth(string text, int scale) => text.Length * (GlyphWidth + 1) * scale - scale;
    }
}
