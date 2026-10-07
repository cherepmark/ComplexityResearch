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
            _spinX.Angle = Clamp(_spinX.Angle - dy * 0.4, -85, 85);
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
            // Нужна сетка минимум 2x2 точки, чтобы построить хотя бы одну грань.
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
    }

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;
}
