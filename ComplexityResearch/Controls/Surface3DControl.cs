using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ComplexityResearch.Models;

namespace ComplexityResearch.Controls;

/// <summary>
/// Настоящая 3D-поверхность для двумерного эксперимента «A(n×m)·B(m×n)».
/// </summary>
/// <remarks>
/// <para>
/// В отличие от <see cref="HeatmapControl"/> (плоская карта, где время
/// закодировано только цветом) — это объёмная модель: ось X = n,
/// ось Z (глубина) = m, высота по оси Y = время. Цвет грани дополнительно
/// дублирует высоту той же цветовой шкалой, что и в тепловой карте
/// (<see cref="HeatmapControl.Colormap"/>), для единообразия.
/// </para>
/// <para>
/// Переиспользует ту же модель данных <see cref="HeatmapData"/> —
/// переключение между тепловой картой и 3D-поверхностью не требует
/// пересчёта эксперимента, только разные визуализации одних и тех же
/// троек (n, m, время).
/// </para>
/// <para>
/// Взаимодействие: зажатая левая кнопка мыши + перемещение — вращение
/// сцены; колесо мыши — приближение/отдаление; двойной щелчок — сброс
/// вида к исходному углу (тот же приём, что и в <c>ChartControl</c>).
/// </para>
/// </remarks>
public class Surface3DControl : Viewport3D
{
    private const double GridScale = 4.0;   // размер сетки по X и Z (-scale/2 .. +scale/2)
    private const double HeightScale = 2.2; // максимальная высота поверхности по Y

    private const double DefaultSpinX = -25;
    private const double DefaultSpinY = -35;
    private const double DefaultCameraDistance = 6.0;

    private readonly ModelVisual3D _sceneRoot = new();
    private readonly Model3DGroup _sceneGroup = new();
    private readonly Transform3DGroup _rotationTransform = new();
    private readonly AxisAngleRotation3D _spinX = new(new Vector3D(1, 0, 0), DefaultSpinX);
    private readonly AxisAngleRotation3D _spinY = new(new Vector3D(0, 1, 0), DefaultSpinY);

    private Point _lastMousePos;
    private bool _dragging;
    private double _cameraDistance = DefaultCameraDistance;

    /// <summary>Диапазоны последних построенных данных — для подписи осей снаружи (TextBlock в XAML).</summary>
    public (double minN, double maxN, double minM, double maxM, double minValue, double maxValue)? LastRange { get; private set; }

    public Surface3DControl()
    {
        ClipToBounds = true;

        _rotationTransform.Children.Add(new RotateTransform3D(_spinX));
        _rotationTransform.Children.Add(new RotateTransform3D(_spinY));
        _sceneRoot.Transform = _rotationTransform;
        _sceneRoot.Content = _sceneGroup;

        Camera = BuildCamera();
        Children.Add(_sceneRoot);
        Children.Add(new ModelVisual3D { Content = BuildLights() });

        Focusable = true;

        MouseLeftButtonDown += (_, e) =>
        {
            _dragging = true;
            _lastMousePos = e.GetPosition(this);
            CaptureMouse();
            Focus();
        };
        MouseLeftButtonUp += (_, _) =>
        {
            _dragging = false;
            ReleaseMouseCapture();
        };
        MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            var pos = e.GetPosition(this);
            double dx = pos.X - _lastMousePos.X;
            double dy = pos.Y - _lastMousePos.Y;
            _spinY.Angle += dx * 0.4;
            // Мышь вниз → камера поднимается («взгляд» на поверхность сверху),
            // мышь вверх → камера опускается: знак dy положительный.
            _spinX.Angle = Clamp(_spinX.Angle + dy * 0.4, -85, 85);
            _lastMousePos = pos;
        };
        MouseWheel += (_, e) =>
        {
            _cameraDistance = Clamp(_cameraDistance - e.Delta * 0.003, 2.5, 15);
            Camera = BuildCamera();
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                ResetView();
            }
        };
    }

    /// <summary>Сбрасывает поворот и приближение к исходным значениям.</summary>
    public void ResetView()
    {
        _spinX.Angle = DefaultSpinX;
        _spinY.Angle = DefaultSpinY;
        _cameraDistance = DefaultCameraDistance;
        Camera = BuildCamera();
    }

    /// <summary>Очищает сцену (например, перед запуском нового эксперимента).</summary>
    public void Clear()
    {
        _sceneGroup.Children.Clear();
        LastRange = null;
    }

    private PerspectiveCamera BuildCamera() => new()
    {
        Position = new Point3D(0, HeightScale * 0.9, _cameraDistance),
        LookDirection = new Vector3D(0, -HeightScale * 0.35, -_cameraDistance),
        UpDirection = new Vector3D(0, 1, 0),
        FieldOfView = 50
    };

    private static Model3DGroup BuildLights()
    {
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(90, 90, 90)));
        group.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -2, -1)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(110, 110, 110), new Vector3D(1, -1, 1)));
        return group;
    }

    /// <summary>
    /// Строит 3D-поверхность по тем же данным, что и тепловая карта:
    /// все тройки (n, m, время) без разбиения по m на отдельные кривые.
    /// Дополнительно строит оси X (n), Z (m) и Y (время) с делениями
    /// и подписями значений.
    /// </summary>
    public void SetData(HeatmapData data)
    {
        _sceneGroup.Children.Clear();
        LastRange = null;

        if (data.Cells.Count == 0) return;

        var xs = data.Cells.Select(c => c.N).Distinct().OrderBy(v => v).ToList();
        var ys = data.Cells.Select(c => c.M).Distinct().OrderBy(v => v).ToList();
        if (xs.Count < 2 || ys.Count < 2)
        {
            // Нужна сетка минимум 2x2 ��очки, чтобы построить хотя бы одну грань.
            return;
        }

        var lookup = data.Cells.ToDictionary(c => (c.N, c.M), c => c.ValueSeconds);
        double minV = data.Cells.Min(c => c.ValueSeconds);
        double maxV = data.Cells.Max(c => c.ValueSeconds);
        LastRange = (xs.Min(), xs.Max(), ys.Min(), ys.Max(), minV, maxV);

        double X(int i) => (xs.Count == 1 ? 0 : (double)i / (xs.Count - 1) - 0.5) * GridScale;
        double Z(int j) => (ys.Count == 1 ? 0 : (double)j / (ys.Count - 1) - 0.5) * GridScale;
        double Y(double v) => maxV > minV ? (v - minV) / (maxV - minV) * HeightScale : HeightScale * 0.5;

        for (int j = 0; j < ys.Count - 1; j++)
        {
            for (int i = 0; i < xs.Count - 1; i++)
            {
                double v00 = lookup[(xs[i], ys[j])];
                double v10 = lookup[(xs[i + 1], ys[j])];
                double v01 = lookup[(xs[i], ys[j + 1])];
                double v11 = lookup[(xs[i + 1], ys[j + 1])];

                var p00 = new Point3D(X(i), Y(v00), Z(j));
                var p10 = new Point3D(X(i + 1), Y(v10), Z(j));
                var p01 = new Point3D(X(i), Y(v01), Z(j + 1));
                var p11 = new Point3D(X(i + 1), Y(v11), Z(j + 1));

                double cellAvg = (v00 + v10 + v01 + v11) / 4.0;
                double t = maxV > minV ? (cellAvg - minV) / (maxV - minV) : 0;
                var color = HeatmapControl.Colormap(t);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                var material = new DiffuseMaterial(brush);

                var mesh = new MeshGeometry3D();
                mesh.Positions.Add(p00);
                mesh.Positions.Add(p10);
                mesh.Positions.Add(p11);
                mesh.Positions.Add(p01);
                // Две грани четырёхугольника; BackMaterial делает их видимыми
                // с обеих сторон, поэтому порядок обхода (winding) не важен.
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(1);
                mesh.TriangleIndices.Add(2);
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(2);
                mesh.TriangleIndices.Add(3);

                var geometryModel = new GeometryModel3D(mesh, material)
                {
                    BackMaterial = material
                };
                _sceneGroup.Children.Add(geometryModel);
            }
        }

        BuildAxes(xs, ys, minV, maxV, X, Z, Y);
    }

    /// <summary>
    /// Строит три оси с делениями и подписями:
    /// X = n (вперед), Z = m (вправо-вглубь), Y = время (вверх).
    /// Подписи выполнены 3D-текстом из тонких пластин (MeshGeometry3D),
    /// поэтому вращаются вместе со сценой.
    /// </summary>
    private void BuildAxes(
        List<double> xs, List<double> ys,
        double minV, double maxV,
        Func<int, double> xOf, Func<int, double> zOf, Func<double, double> yOf)
    {
        double half = GridScale / 2.0;
        double yBase = -0.35;              // уровень «пола» под поверхностью
        var axisColor = Color.FromRgb(200, 200, 200);
        var tickColor = Color.FromRgb(160, 160, 160);
        var labelColor = Color.FromRgb(230, 230, 230);
        var axisBrush = new SolidColorBrush(axisColor);
        axisBrush.Freeze();
        var tickBrush = new SolidColorBrush(tickColor);
        tickBrush.Freeze();
        var labelBrush = new SolidColorBrush(labelColor);
        labelBrush.Freeze();

        void AddLine(Point3D from, Point3D to, Brush brush, double thickness = 0.012)
        {
            var mesh = new MeshGeometry3D();
            double t = thickness;
            // Тонкая пластина-линия (двусторонняя).
            mesh.Positions.Add(new Point3D(from.X, from.Y, from.Z));
            mesh.Positions.Add(new Point3D(to.X + t, to.Y, to.Z));
            mesh.Positions.Add(new Point3D(to.X, to.Y + t, to.Z));
            mesh.Positions.Add(new Point3D(to.X, to.Y, to.Z + t));
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(1);
            mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(3);
            var material = new DiffuseMaterial(brush);
            _sceneGroup.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }

        // --- Оси ---
        // X (n): вдоль X на уровне пола, со стороны зрителя (z = +half).
        AddLine(new Point3D(-half, yBase, half), new Point3D(half + 0.55, yBase, half), axisBrush, 0.02);
        // Z (m): вдоль Z на уровне пола, справа (x = +half).
        AddLine(new Point3D(half, yBase, -half), new Point3D(half, yBase, half + 0.55), axisBrush, 0.02);
        // Y (время): вертикаль ��лева-спереди.
        AddLine(new Point3D(-half, yBase, half), new Point3D(-half, yOf(maxV) + 0.55, half), axisBrush, 0.02);

        // --- Деления и подписи по X (значения n) ---
        int xStep = Math.Max(1, (xs.Count - 1) / 6);
        for (int i = 0; i < xs.Count; i += xStep)
        {
            double x = xOf(i);
            AddLine(new Point3D(x, yBase, half), new Point3D(x, yBase - 0.12, half), tickBrush, 0.008);
            AddLabel(FormatValue(xs[i]), new Point3D(x, yBase - 0.42, half + 0.05), labelBrush, size: 0.26);
        }
        AddLabel("n", new Point3D(half + 0.95, yBase + 0.12, half + 0.05), labelBrush, size: 0.34, bold: true);

        // --- Деления и подписи по Z (значения m) ---
        int zStep = Math.Max(1, (ys.Count - 1) / 6);
        for (int j = 0; j < ys.Count; j += zStep)
        {
            double z = zOf(j);
            AddLine(new Point3D(half, yBase, z), new Point3D(half, yBase - 0.12, z), tickBrush, 0.008);
            AddLabel(FormatValue(ys[j]), new Point3D(half + 0.12, yBase - 0.42, z), labelBrush, size: 0.26);
        }
        AddLabel("m", new Point3D(half + 0.35, yBase + 0.12, -half - 0.35), labelBrush, size: 0.34, bold: true);

        // --- Деления и подписи по Y (время, сек) ---
        int ySteps = 4;
        for (int s = 0; s <= ySteps; s++)
        {
            double v = minV + (maxV - minV) * s / ySteps;
            double y = yOf(v);
            AddLine(new Point3D(-half, y, half), new Point3D(-half - 0.12, y, half), tickBrush, 0.008);
            AddLabel(FormatValue(v), new Point3D(-half - 0.2, y - 0.14, half + 0.05), labelBrush, size: 0.24);
        }
        AddLabel("t, с", new Point3D(-half - 0.55, yOf(maxV) + 0.45, half + 0.05), labelBrush, size: 0.3, bold: true);
    }

    /// <summary>
    /// Добавляет 3D-подпись: текст рисуется в тонкие пластины по глифам
    /// (упрощённый «блочный» шрифт 5×7 на символ), ориентация — горизонтально
    /// в плоскости XZ (лицом вверх), чтобы читалась сверху при вращении.
    /// </summary>
    private void AddLabel(string text, Point3D origin, Brush brush, double size, bool bold = false)
    {
        double advance = size * 0.8;
        double cursor = origin.X - text.Length * advance / 2.0;
        foreach (char ch in text)
        {
            foreach (var quad in GlyphQuads(ch, cursor, origin.Y, origin.Z, size))
            {
                var mesh = new MeshGeometry3D();
                foreach (var p in quad)
                {
                    mesh.Positions.Add(p);
                }
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(1);
                mesh.TriangleIndices.Add(2);
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(2);
                mesh.TriangleIndices.Add(3);
                var glyphMaterial = new DiffuseMaterial(brush);
                _sceneGroup.Children.Add(new GeometryModel3D(mesh, glyphMaterial) { BackMaterial = glyphMaterial });
            }
            cursor += advance;
        }
    }

    /// <summary>
    /// Глифы упрощённого 5×7 «пиксельного» шрифта: цифры, буквы для
    /// подписей осей и разделители. Каждый включённый пиксель — квадрат
    /// в плоскости XZ (Y фиксирован — текст «лежит» на плоскости).
    /// </summary>
    private static IEnumerable<Point3D[]> GlyphQuads(char c, double x0, double y, double z0, double size)
    {
        // 5 колонок × 7 строк; '.' = выключено, '#' = включено.
        string? pattern = c switch
        {
            '0' => "01110|10001|10011|10101|11001|10001|01110",
            '1' => "00100|01100|00100|00100|00100|00100|01110",
            '2' => "01110|10001|00001|00010|00100|01000|11111",
            '3' => "11110|00001|00001|01110|00001|00001|11110",
            '4' => "00010|00110|01010|10010|11111|00010|00010",
            '5' => "11111|10000|11110|00001|00001|10001|01110",
            '6' => "00110|01000|10000|11110|10001|10001|01110",
            '7' => "11111|00001|00010|00100|01000|01000|01000",
            '8' => "01110|10001|10001|01110|10001|10001|01110",
            '9' => "01110|10001|10001|01111|00001|00010|01100",
            '.' => "00000|00000|00000|00000|00000|01100|01100",
            ',' => "00000|00000|00000|00000|01100|01100|10000",
            '-' => "00000|00000|00000|11111|00000|00000|00000",
            ' ' => "00000|00000|00000|00000|00000|00000|00000",
            'n' => "00000|00000|10110|11011|10001|10001|10001",
            'm' => "00000|00000|11010|10101|10101|10101|10101",
            't' => "00100|00100|01110|00100|00100|00101|00010",
            'с' => "01110|10001|10000|10000|10000|10001|01110",
            'С' => "01110|10001|10000|10000|10000|10001|01110",
            _ => "01110|10001|10001|11111|10001|10001|10001"
        };
        if (pattern == null)
        {
            yield break;
        }

        double pixel = size / 7.0;
        string[] rows = pattern.Split('|');
        for (int row = 0; row < rows.Length; row++)
        {
            for (int col = 0; col < rows[row].Length; col++)
            {
                if (rows[row][col] != '#')
                {
                    continue;
                }
                // Строка 0 — верх глифа; текст ориентируем «лицом вверх»,
                // читая слева направо по оси X.
                double px = x0 + col * pixel;
                double pz = z0 + row * pixel;
                yield return
                [
                    new Point3D(px, y, pz),
                    new Point3D(px + pixel, y, pz),
                    new Point3D(px + pixel, y, pz + pixel),
                    new Point3D(px, y, pz + pixel)
                ];
            }
        }
    }

    /// <summary>Компактный формат чисел для подписей осей.</summary>
    private static string FormatValue(double v)
    {
        if (v == 0) return "0";
        double abs = Math.Abs(v);
        if (abs >= 1e6 || abs < 1e-4) return v.ToString("G2", CultureInfo.CurrentCulture);
        return v.ToString("0.###", CultureInfo.CurrentCulture);
    }

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;
}
