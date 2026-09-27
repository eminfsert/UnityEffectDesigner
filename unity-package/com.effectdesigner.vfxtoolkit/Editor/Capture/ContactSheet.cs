using System.Globalization;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Capture
{
    /// <summary>
    /// Assembles captured frames into one image: one column per time, one row per
    /// view x background combination, with the time stamped above each column.
    /// </summary>
    sealed class ContactSheet
    {
        public const int MaxSheetWidth = 4096;
        const int Gap = 4;
        const int LabelScale = 2;
        static readonly Color32 SheetColor = new Color32(24, 24, 28, 255);
        static readonly Color32 LabelColor = new Color32(235, 235, 240, 255);

        readonly int _columns;
        readonly int _rows;
        readonly int _cell;
        readonly int _header;
        readonly Color32[] _pixels;

        public int Width { get; }
        public int Height { get; }
        public int CellSize => _cell;

        public ContactSheet(float[] times, int rows, int frameSize)
        {
            _columns = times.Length;
            _rows = rows;
            _cell = Mathf.Min(frameSize, (MaxSheetWidth - Gap * (_columns + 1)) / _columns);
            _header = PixelFont.GlyphHeight * LabelScale + Gap * 2;

            Width = _columns * _cell + (_columns + 1) * Gap;
            Height = _header + _rows * _cell + (_rows + 1) * Gap;
            _pixels = new Color32[Width * Height];
            for (int i = 0; i < _pixels.Length; i++)
                _pixels[i] = SheetColor;

            for (int c = 0; c < _columns; c++)
            {
                string label = FormatTime(times[c]);
                int cellX = Gap + c * (_cell + Gap);
                int textX = cellX + (_cell - PixelFont.MeasureWidth(label, LabelScale)) / 2;
                PixelFont.Draw(_pixels, Width, Height, label, textX, Height - 1 - Gap, LabelScale, LabelColor);
            }
        }

        public static string FormatTime(float t) => t.ToString("0.00", CultureInfo.InvariantCulture) + "s";

        /// <summary>Copies a frame into the cell at (row, column); row 0 is the top row.</summary>
        public void Place(Texture2D frame, int row, int column)
        {
            int cellX = Gap + column * (_cell + Gap);
            int cellTop = Height - _header - Gap - row * (_cell + Gap); // exclusive top edge
            int cellY = cellTop - _cell;

            bool sameSize = frame.width == _cell && frame.height == _cell;
            Color32[] source = sameSize ? frame.GetPixels32() : null;

            for (int y = 0; y < _cell; y++)
            {
                for (int x = 0; x < _cell; x++)
                {
                    Color32 c = sameSize
                        ? source[y * _cell + x]
                        : (Color32)frame.GetPixelBilinear((x + 0.5f) / _cell, (y + 0.5f) / _cell);
                    _pixels[(cellY + y) * Width + cellX + x] = c;
                }
            }
        }

        public byte[] EncodePng()
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
            try
            {
                tex.SetPixels32(_pixels);
                tex.Apply(false);
                return tex.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }
    }
}
