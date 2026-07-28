using Godot;

public partial class ComputerScreenRenderer : Node3D
{
	[Export] public NodePath ScreenPath { get; set; } = new("Screen");
	[Export] public NodePath ViewportPath { get; set; } = new("ComputerViewport");
	[Export] public float EmissionEnergy { get; set; } = 0.65f;

	private MeshInstance3D _screen = null!;
	private SubViewport _viewport = null!;

	public override void _Ready()
	{
		_screen = ResolveScreen();
		_viewport = ResolveViewport();

		if (_screen == null || _viewport == null)
		{
			return;
		}

		ApplyViewportMaterial();
	}

	private MeshInstance3D ResolveScreen()
	{
		if (HasNode(ScreenPath))
		{
			return GetNode<MeshInstance3D>(ScreenPath);
		}

		var found = FindChild("Screen", true, false) as MeshInstance3D;
		if (found == null)
		{
			GD.PushError($"{nameof(ComputerScreenRenderer)}: Screen MeshInstance3D was not found.");
		}

		return found;
	}

	private SubViewport ResolveViewport()
	{
		if (HasNode(ViewportPath))
		{
			return GetNode<SubViewport>(ViewportPath);
		}

		var found = FindChild("ComputerViewport", true, false) as SubViewport;
		if (found == null)
		{
			GD.PushError($"{nameof(ComputerScreenRenderer)}: ComputerViewport was not found.");
		}

		return found;
	}

	private void ApplyViewportMaterial()
	{
		var viewportTexture = _viewport.GetTexture();
		var screenMaterial = new StandardMaterial3D
		{
			AlbedoTexture = viewportTexture,
			EmissionEnabled = true,
			EmissionTexture = viewportTexture,
			EmissionEnergyMultiplier = EmissionEnergy,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled
		};

		_screen.MaterialOverride = screenMaterial;
	}
}
