using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Draws an SVG path (the subset Tabler icons use: M L H V C S Q T A Z,
    /// absolute and relative) with the UI Toolkit Vector API.
    ///
    /// UI Toolkit cannot render SVG markup without com.unity.vectorgraphics, so the icons
    /// are kept as path data and stroked here instead of being re-drawn by hand or
    /// shipped as bitmaps.
    /// </summary>
    public class AlpsVectorIcon : VisualElement
    {
        private static readonly CustomStyleProperty<Color> IconColorProperty =
            new CustomStyleProperty<Color>("--alps-icon-color");

        private readonly List<List<Vector2>> _contours = new List<List<Vector2>>();
        private readonly List<bool> _closed = new List<bool>();

        private Vector2 _viewBox = new Vector2(24f, 24f);
        private Color _color = new Color(0.918f, 0.918f, 0.918f);
        private float _strokeWidth = 2f;
        private bool _fill;

        public AlpsVectorIcon()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        public AlpsVectorIcon(string pathData, Vector2 viewBox, float strokeWidth = 2f, bool fill = false)
            : this()
        {
            _viewBox = viewBox;
            _strokeWidth = strokeWidth;
            _fill = fill;
            SetPaths(pathData);
        }

        /// <summary>True when the contours are filled rather than stroked.</summary>
        public bool Filled => _fill;

        public Color IconColor
        {
            get => _color;
            set
            {
                _color = value;
                MarkDirtyRepaint();
            }
        }

        public float StrokeWidth
        {
            get => _strokeWidth;
            set
            {
                _strokeWidth = value;
                MarkDirtyRepaint();
            }
        }

        public void SetViewBox(Vector2 viewBox)
        {
            _viewBox = viewBox;
            MarkDirtyRepaint();
        }

        /// <summary>Replace the geometry with one or more SVG path strings.</summary>
        public void SetPaths(params string[] pathData)
        {
            _contours.Clear();
            _closed.Clear();

            if (pathData == null)
            {
                MarkDirtyRepaint();
                return;
            }

            foreach (var data in pathData)
            {
                if (!string.IsNullOrEmpty(data))
                {
                    AlpsSvgPath.Flatten(data, _contours, _closed);
                }
            }

            MarkDirtyRepaint();
        }

        /// <summary>Replace the geometry with contours already expressed in view-box space.</summary>
        public void SetContours(IEnumerable<IReadOnlyList<Vector2>> contours, bool closed = false)
        {
            _contours.Clear();
            _closed.Clear();

            if (contours == null)
            {
                MarkDirtyRepaint();
                return;
            }

            foreach (var contour in contours)
            {
                if (contour == null || contour.Count < 2)
                {
                    continue;
                }

                _contours.Add(new List<Vector2>(contour));
                _closed.Add(closed);
            }

            MarkDirtyRepaint();
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(IconColorProperty, out var color))
            {
                _color = color;
                MarkDirtyRepaint();
            }
        }

        /// <summary>
        /// The geometry this icon draws, in local pixels, together with the stroke width
        /// it is drawn at. Used by the painter below and by the layout tests, so what is
        /// verified is exactly what is rendered.
        /// </summary>
        public bool TryGetLocalGeometry(out List<List<Vector2>> contours, out float strokeWidth)
        {
            contours = new List<List<Vector2>>();
            strokeWidth = 0f;

            var rect = contentRect;
            if (_contours.Count == 0 || rect.width <= 0f || rect.height <= 0f ||
                _viewBox.x <= 0f || _viewBox.y <= 0f)
            {
                return false;
            }

            var scale = Mathf.Min(rect.width / _viewBox.x, rect.height / _viewBox.y);
            var offset = new Vector2(
                (rect.width - _viewBox.x * scale) * 0.5f,
                (rect.height - _viewBox.y * scale) * 0.5f);

            foreach (var contour in _contours)
            {
                var local = new List<Vector2>(contour.Count);
                foreach (var point in contour)
                {
                    local.Add(offset + point * scale);
                }

                contours.Add(local);
            }

            strokeWidth = Mathf.Max(0.01f, _strokeWidth * scale);
            return true;
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (_contours.Count == 0)
            {
                return;
            }

            var rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f || _viewBox.x <= 0f || _viewBox.y <= 0f)
            {
                return;
            }

            // Uniform "meet" fit, matching SVG's default preserveAspectRatio.
            var scale = Mathf.Min(rect.width / _viewBox.x, rect.height / _viewBox.y);
            var offset = new Vector2(
                (rect.width - _viewBox.x * scale) * 0.5f,
                (rect.height - _viewBox.y * scale) * 0.5f);

            var painter = context.painter2D;
            painter.lineWidth = Mathf.Max(0.01f, _strokeWidth * scale);
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = _color;
            painter.fillColor = _color;

            for (var i = 0; i < _contours.Count; i++)
            {
                var contour = _contours[i];
                if (contour.Count < 2)
                {
                    continue;
                }

                painter.BeginPath();
                painter.MoveTo(offset + contour[0] * scale);
                for (var p = 1; p < contour.Count; p++)
                {
                    painter.LineTo(offset + contour[p] * scale);
                }

                if (_closed[i])
                {
                    painter.ClosePath();
                }

                if (_fill)
                {
                    painter.Fill();
                }
                else
                {
                    painter.Stroke();
                }
            }
        }
    }

    /// <summary>Flattens SVG path data into polylines. Curves are sampled, arcs go via cubics.</summary>
    public static class AlpsSvgPath
    {
        private const int CurveSegments = 12;

        public static void Flatten(string data, List<List<Vector2>> contours, List<bool> closedFlags)
        {
            new Builder(contours, closedFlags).Run(Tokenize(data));
        }

        /// <summary>Holds the pen state while walking one path string.</summary>
        private sealed class Builder
        {
            private readonly List<List<Vector2>> _contours;
            private readonly List<bool> _closedFlags;

            private List<Vector2> _contour;
            private Vector2 _current;
            private Vector2 _start;
            private Vector2 _lastCubicControl;
            private Vector2 _lastQuadControl;

            public Builder(List<List<Vector2>> contours, List<bool> closedFlags)
            {
                _contours = contours;
                _closedFlags = closedFlags;
            }

            public void Run(List<Token> tokens)
            {
                var index = 0;
                var command = '\0';
                var previousCommand = '\0';

                while (index < tokens.Count)
                {
                    if (tokens[index].IsCommand)
                    {
                        command = tokens[index].Command;
                        index++;
                    }
                    else if (command == 'M')
                    {
                        // Extra coordinate pairs after a moveto are implicit linetos.
                        command = 'L';
                    }
                    else if (command == 'm')
                    {
                        command = 'l';
                    }
                    else if (command == '\0')
                    {
                        break;
                    }

                    if (!Execute(command, tokens, ref index, previousCommand))
                    {
                        break;
                    }

                    previousCommand = command;
                }

                EndContour(false);
            }

            private bool Execute(char command, List<Token> tokens, ref int index, char previousCommand)
            {
                var relative = char.IsLower(command);
                var upper = char.ToUpperInvariant(command);

                switch (upper)
                {
                    case 'M':
                    {
                        if (!TryReadPoint(tokens, ref index, out var point)) return false;
                        EndContour(false);
                        _current = relative ? _current + point : point;
                        _start = _current;
                        Append(_current);
                        return true;
                    }

                    case 'L':
                    {
                        if (!TryReadPoint(tokens, ref index, out var point)) return false;
                        _current = relative ? _current + point : point;
                        Append(_current);
                        return true;
                    }

                    case 'H':
                    {
                        if (!TryReadNumber(tokens, ref index, out var x)) return false;
                        _current = new Vector2(relative ? _current.x + x : x, _current.y);
                        Append(_current);
                        return true;
                    }

                    case 'V':
                    {
                        if (!TryReadNumber(tokens, ref index, out var y)) return false;
                        _current = new Vector2(_current.x, relative ? _current.y + y : y);
                        Append(_current);
                        return true;
                    }

                    case 'C':
                    {
                        if (!TryReadPoint(tokens, ref index, out var c1)) return false;
                        if (!TryReadPoint(tokens, ref index, out var c2)) return false;
                        if (!TryReadPoint(tokens, ref index, out var end)) return false;
                        if (relative) { c1 += _current; c2 += _current; end += _current; }
                        AppendCubic(_current, c1, c2, end);
                        _lastCubicControl = c2;
                        _current = end;
                        return true;
                    }

                    case 'S':
                    {
                        if (!TryReadPoint(tokens, ref index, out var c2)) return false;
                        if (!TryReadPoint(tokens, ref index, out var end)) return false;
                        if (relative) { c2 += _current; end += _current; }
                        var previousUpper = char.ToUpperInvariant(previousCommand);
                        var c1 = previousUpper == 'C' || previousUpper == 'S'
                            ? _current * 2f - _lastCubicControl
                            : _current;
                        AppendCubic(_current, c1, c2, end);
                        _lastCubicControl = c2;
                        _current = end;
                        return true;
                    }

                    case 'Q':
                    {
                        if (!TryReadPoint(tokens, ref index, out var c)) return false;
                        if (!TryReadPoint(tokens, ref index, out var end)) return false;
                        if (relative) { c += _current; end += _current; }
                        AppendQuadratic(_current, c, end);
                        _lastQuadControl = c;
                        _current = end;
                        return true;
                    }

                    case 'T':
                    {
                        if (!TryReadPoint(tokens, ref index, out var end)) return false;
                        if (relative) { end += _current; }
                        var previousUpper = char.ToUpperInvariant(previousCommand);
                        var c = previousUpper == 'Q' || previousUpper == 'T'
                            ? _current * 2f - _lastQuadControl
                            : _current;
                        AppendQuadratic(_current, c, end);
                        _lastQuadControl = c;
                        _current = end;
                        return true;
                    }

                    case 'A':
                    {
                        if (!TryReadNumber(tokens, ref index, out var rx)) return false;
                        if (!TryReadNumber(tokens, ref index, out var ry)) return false;
                        if (!TryReadNumber(tokens, ref index, out var rotation)) return false;
                        if (!TryReadNumber(tokens, ref index, out var largeArc)) return false;
                        if (!TryReadNumber(tokens, ref index, out var sweep)) return false;
                        if (!TryReadPoint(tokens, ref index, out var end)) return false;
                        if (relative) { end += _current; }
                        AppendArc(_current, rx, ry, rotation, largeArc > 0.5f, sweep > 0.5f, end);
                        _current = end;
                        return true;
                    }

                    case 'Z':
                    {
                        Append(_start);
                        EndContour(true);
                        _current = _start;
                        return true;
                    }

                    default:
                        return false;
                }
            }

            private void Append(Vector2 point)
            {
                if (_contour == null)
                {
                    _contour = new List<Vector2>();
                }

                _contour.Add(point);
            }

            private void EndContour(bool closed)
            {
                if (_contour != null && _contour.Count >= 2)
                {
                    _contours.Add(_contour);
                    _closedFlags.Add(closed);
                }

                _contour = null;
            }

            private void AppendCubic(Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p1)
            {
                for (var i = 1; i <= CurveSegments; i++)
                {
                    var t = i / (float)CurveSegments;
                    var mt = 1f - t;
                    Append(mt * mt * mt * p0
                           + 3f * mt * mt * t * c1
                           + 3f * mt * t * t * c2
                           + t * t * t * p1);
                }
            }

            private void AppendQuadratic(Vector2 p0, Vector2 c, Vector2 p1)
            {
                for (var i = 1; i <= CurveSegments; i++)
                {
                    var t = i / (float)CurveSegments;
                    var mt = 1f - t;
                    Append(mt * mt * p0 + 2f * mt * t * c + t * t * p1);
                }
            }

            /// <summary>Endpoint-to-centre arc conversion, per the SVG implementation notes.</summary>
            private void AppendArc(
                Vector2 p0,
                float rx,
                float ry,
                float rotationDegrees,
                bool largeArc,
                bool sweep,
                Vector2 p1)
            {
                if (Mathf.Approximately(rx, 0f) || Mathf.Approximately(ry, 0f))
                {
                    Append(p1);
                    return;
                }

                rx = Mathf.Abs(rx);
                ry = Mathf.Abs(ry);

                var phi = rotationDegrees * Mathf.Deg2Rad;
                var cosPhi = Mathf.Cos(phi);
                var sinPhi = Mathf.Sin(phi);

                var dx = (p0.x - p1.x) * 0.5f;
                var dy = (p0.y - p1.y) * 0.5f;
                var x1 = cosPhi * dx + sinPhi * dy;
                var y1 = -sinPhi * dx + cosPhi * dy;

                var lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
                if (lambda > 1f)
                {
                    var correction = Mathf.Sqrt(lambda);
                    rx *= correction;
                    ry *= correction;
                }

                var numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
                var denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1;
                var factor = denominator <= 0f ? 0f : Mathf.Sqrt(Mathf.Max(0f, numerator / denominator));
                if (largeArc == sweep)
                {
                    factor = -factor;
                }

                var cx1 = factor * rx * y1 / ry;
                var cy1 = -factor * ry * x1 / rx;

                var cx = cosPhi * cx1 - sinPhi * cy1 + (p0.x + p1.x) * 0.5f;
                var cy = sinPhi * cx1 + cosPhi * cy1 + (p0.y + p1.y) * 0.5f;

                var startAngle = Angle(1f, 0f, (x1 - cx1) / rx, (y1 - cy1) / ry);
                var deltaAngle = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);

                if (!sweep && deltaAngle > 0f)
                {
                    deltaAngle -= 2f * Mathf.PI;
                }
                else if (sweep && deltaAngle < 0f)
                {
                    deltaAngle += 2f * Mathf.PI;
                }

                var steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(deltaAngle) / (Mathf.PI * 0.25f)) * 3);
                for (var i = 1; i <= steps; i++)
                {
                    var angle = startAngle + deltaAngle * (i / (float)steps);
                    var x = rx * Mathf.Cos(angle);
                    var y = ry * Mathf.Sin(angle);
                    Append(new Vector2(
                        cosPhi * x - sinPhi * y + cx,
                        sinPhi * x + cosPhi * y + cy));
                }
            }

            private static float Angle(float ux, float uy, float vx, float vy)
            {
                var dot = ux * vx + uy * vy;
                var lengths = Mathf.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
                if (lengths <= 0f)
                {
                    return 0f;
                }

                var angle = Mathf.Acos(Mathf.Clamp(dot / lengths, -1f, 1f));
                return ux * vy - uy * vx < 0f ? -angle : angle;
            }
        }

        public readonly struct Token
        {
            public readonly bool IsCommand;
            public readonly char Command;
            public readonly float Number;

            public Token(char command)
            {
                IsCommand = true;
                Command = command;
                Number = 0f;
            }

            public Token(float number)
            {
                IsCommand = false;
                Command = '\0';
                Number = number;
            }
        }

        private static List<Token> Tokenize(string data)
        {
            var tokens = new List<Token>();
            var i = 0;

            while (i < data.Length)
            {
                var c = data[i];

                if (char.IsWhiteSpace(c) || c == ',')
                {
                    i++;
                    continue;
                }

                if (char.IsLetter(c))
                {
                    tokens.Add(new Token(c));
                    i++;
                    continue;
                }

                var startIndex = i;
                if (c == '+' || c == '-')
                {
                    i++;
                }

                var seenDot = false;
                while (i < data.Length)
                {
                    var d = data[i];
                    if (char.IsDigit(d))
                    {
                        i++;
                    }
                    else if (d == '.' && !seenDot)
                    {
                        seenDot = true;
                        i++;
                    }
                    else if ((d == 'e' || d == 'E') && i + 1 < data.Length)
                    {
                        i++;
                        if (data[i] == '+' || data[i] == '-')
                        {
                            i++;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                if (i == startIndex)
                {
                    // Unrecognised character. Skip it rather than spinning.
                    i++;
                    continue;
                }

                var text = data.Substring(startIndex, i - startIndex);
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    tokens.Add(new Token(value));
                }
            }

            return tokens;
        }

        private static bool TryReadNumber(List<Token> tokens, ref int index, out float value)
        {
            if (index < tokens.Count && !tokens[index].IsCommand)
            {
                value = tokens[index].Number;
                index++;
                return true;
            }

            value = 0f;
            return false;
        }

        private static bool TryReadPoint(List<Token> tokens, ref int index, out Vector2 point)
        {
            if (TryReadNumber(tokens, ref index, out var x) && TryReadNumber(tokens, ref index, out var y))
            {
                point = new Vector2(x, y);
                return true;
            }

            point = Vector2.zero;
            return false;
        }
    }
}
