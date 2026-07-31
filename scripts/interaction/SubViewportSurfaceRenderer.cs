using Godot;

public partial class SubViewportSurfaceRenderer : Node
{
	[Export] public NodePath SurfacePath { get; set; } = new("../ViewportSurface");
	[Export] public NodePath ViewportPath { get; set; } = new("../ContentViewport");
	[Export(PropertyHint.Range, "0,1,0.01")] public float EmissionEnergy { get; set; } = 0.08f;

	public override void _Ready()
	{
		MeshInstance3D surface = GetNodeOrNull<MeshInstance3D>(SurfacePath);
		SubViewport viewport = GetNodeOrNull<SubViewport>(ViewportPath);
		if (surface == null || viewport == null)
		{
			GD.PushError($"{nameof(SubViewportSurfaceRenderer)}: surface or viewport was not found.");
			return;
		}

		var material = new StandardMaterial3D
		{
			AlbedoColor = Colors.White,
			AlbedoTexture = viewport.GetTexture(),
			EmissionEnabled = true,
			EmissionTexture = viewport.GetTexture(),
			EmissionEnergyMultiplier = EmissionEnergy,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest
		};

		surface.MaterialOverride = material;
		GD.Print($"{nameof(SubViewportSurfaceRenderer)}: viewport {viewport.Size} applied to {surface.GetPath()}.");
	}
}
