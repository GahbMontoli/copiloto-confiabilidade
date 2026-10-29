using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AssistentePatinho.App;

public enum DuckAnimationMode { Idle, Walking }

public sealed class Duck3DView : UserControl
{
    private static readonly MeshGeometry3D Sphere = CreateSphere(20, 14);
    private static readonly MeshGeometry3D Box = CreateBox();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly AxisAngleRotation3D _tilt = new(new Vector3D(0, 0, 1), 0);
    private readonly AxisAngleRotation3D _leftWing = new(new Vector3D(0, 0, 1), -8);
    private readonly AxisAngleRotation3D _rightWing = new(new Vector3D(0, 0, 1), 8);
    private readonly ScaleTransform3D _leftEye = new(1, 1, 1);
    private readonly ScaleTransform3D _rightEye = new(1, 1, 1);
    private readonly TranslateTransform3D _leftFoot = new();
    private readonly TranslateTransform3D _rightFoot = new();
    private readonly TranslateTransform3D _bob = new();
    private DuckAnimationMode _mode;
    private double _alertUntil;

    public Duck3DView()
    {
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        var viewport = new Viewport3D
        {
            Camera = new PerspectiveCamera(new Point3D(0, 0.15, 6.3), new Vector3D(0, -0.1, -6.3), new Vector3D(0, 1, 0), 34),
            ClipToBounds = false
        };
        viewport.Children.Add(new ModelVisual3D { Content = BuildScene() });
        Content = viewport;
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    public void SetMode(DuckAnimationMode mode) => _mode = mode;
    public void PulseAlert() => _alertUntil = _clock.Elapsed.TotalSeconds + 0.8;

    private Model3DGroup BuildScene()
    {
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(205, 198, 184)));
        scene.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.7, -1, -1.2)));
        var yellow = MakeMaterial(Color.FromRgb(255, 221, 91));
        var lightYellow = MakeMaterial(Color.FromRgb(255, 236, 150));
        var wing = MakeMaterial(Color.FromRgb(232, 190, 71));
        var orange = MakeMaterial(Color.FromRgb(238, 139, 55));
        var dark = MakeMaterial(Color.FromRgb(45, 34, 18));
        var blush = MakeMaterial(Color.FromRgb(244, 158, 171));

        scene.Children.Add(Ellipsoid(yellow, new(1.08, 1.12, .78), new(0, -.28, 0)));
        scene.Children.Add(Ellipsoid(lightYellow, new(.83, .78, .72), new(0, .68, .05)));
        scene.Children.Add(Ellipsoid(wing, new(.42, .72, .3), new(-.92, -.2, 0), _leftWing));
        scene.Children.Add(Ellipsoid(wing, new(.42, .72, .3), new(.92, -.2, 0), _rightWing));
        scene.Children.Add(Ellipsoid(dark, new(.12, .16, .09), new(-.3, .82, .69), scale: _leftEye));
        scene.Children.Add(Ellipsoid(dark, new(.12, .16, .09), new(.3, .82, .69), scale: _rightEye));
        scene.Children.Add(Ellipsoid(blush, new(.16, .07, .06), new(-.5, .55, .68)));
        scene.Children.Add(Ellipsoid(blush, new(.16, .07, .06), new(.5, .55, .68)));
        scene.Children.Add(Cuboid(orange, new(.54, .17, .28), new(0, .5, .78)));
        scene.Children.Add(Ellipsoid(orange, new(.42, .12, .34), new(-.43, -1.28, .22), offset: _leftFoot));
        scene.Children.Add(Ellipsoid(orange, new(.42, .12, .34), new(.43, -1.28, .22), offset: _rightFoot));
        var transforms = new Transform3DGroup();
        transforms.Children.Add(new RotateTransform3D(_tilt));
        transforms.Children.Add(_bob);
        scene.Transform = transforms;
        return scene;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var time = _clock.Elapsed.TotalSeconds;
        var step = Math.Sin(time * 10.5);
        var idle = Math.Sin(time * 2.6);
        var walking = _mode == DuckAnimationMode.Walking;
        _bob.OffsetY = walking ? Math.Abs(step) * .08 : idle * .035;
        _tilt.Angle = walking ? step * 3.4 : idle * 1.4;
        _leftWing.Angle = walking ? -12 + step * 16 : -8 + idle * 4;
        _rightWing.Angle = walking ? 12 - step * 16 : 8 - idle * 4;
        _leftFoot.OffsetY = walking ? Math.Max(0, step) * .16 : 0;
        _rightFoot.OffsetY = walking ? Math.Max(0, -step) * .16 : 0;
        var eyeHeight = time % 4.4 > 4.18 ? .12 : 1;
        _leftEye.ScaleY = eyeHeight;
        _rightEye.ScaleY = eyeHeight;
        if (time < _alertUntil) _tilt.Angle += Math.Sin(time * 48) * 7;
    }

    private static GeometryModel3D Ellipsoid(Material material, Vector3D size, Vector3D position,
        AxisAngleRotation3D? rotation = null, ScaleTransform3D? scale = null, TranslateTransform3D? offset = null)
    {
        var transforms = new Transform3DGroup();
        transforms.Children.Add(new ScaleTransform3D(size.X, size.Y, size.Z));
        if (scale is not null) transforms.Children.Add(scale);
        if (rotation is not null) transforms.Children.Add(new RotateTransform3D(rotation));
        transforms.Children.Add(new TranslateTransform3D(position.X, position.Y, position.Z));
        if (offset is not null) transforms.Children.Add(offset);
        return new GeometryModel3D(Sphere, material) { BackMaterial = material, Transform = transforms };
    }

    private static GeometryModel3D Cuboid(Material material, Vector3D size, Vector3D position)
    {
        var transforms = new Transform3DGroup();
        transforms.Children.Add(new ScaleTransform3D(size.X, size.Y, size.Z));
        transforms.Children.Add(new TranslateTransform3D(position.X, position.Y, position.Z));
        return new GeometryModel3D(Box, material) { BackMaterial = material, Transform = transforms };
    }

    private static Material MakeMaterial(Color color)
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        material.Children.Add(new SpecularMaterial(Brushes.White, 38));
        material.Freeze();
        return material;
    }

    private static MeshGeometry3D CreateSphere(int longitude, int latitude)
    {
        var mesh = new MeshGeometry3D();
        for (var lat = 0; lat <= latitude; lat++)
        {
            var theta = Math.PI * lat / latitude;
            for (var lon = 0; lon <= longitude; lon++)
            {
                var phi = 2 * Math.PI * lon / longitude;
                var point = new Point3D(Math.Sin(theta) * Math.Cos(phi), Math.Cos(theta), Math.Sin(theta) * Math.Sin(phi));
                mesh.Positions.Add(point);
                mesh.Normals.Add(new Vector3D(point.X, point.Y, point.Z));
            }
        }
        for (var lat = 0; lat < latitude; lat++)
        for (var lon = 0; lon < longitude; lon++)
        {
            var first = lat * (longitude + 1) + lon;
            var second = first + longitude + 1;
            mesh.TriangleIndices.Add(first); mesh.TriangleIndices.Add(second); mesh.TriangleIndices.Add(first + 1);
            mesh.TriangleIndices.Add(first + 1); mesh.TriangleIndices.Add(second); mesh.TriangleIndices.Add(second + 1);
        }
        mesh.Freeze();
        return mesh;
    }

    private static MeshGeometry3D CreateBox()
    {
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new(-1,-1,-1), new(1,-1,-1), new(1,1,-1), new(-1,1,-1),
                new(-1,-1,1), new(1,-1,1), new(1,1,1), new(-1,1,1)
            },
            TriangleIndices = new Int32Collection
            {
                0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4,
                2,3,7, 2,7,6, 1,2,6, 1,6,5, 0,4,7, 0,7,3
            }
        };
        mesh.Freeze();
        return mesh;
    }
}
