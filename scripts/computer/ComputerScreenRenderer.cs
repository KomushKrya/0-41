using Godot;

public partial class ComputerScreenRenderer : Node3D
{
	private const string ScreenShaderPath = "res://assets/shaders/ComputerScreen.gdshader";

	[Export] public NodePath ScreenPath { get; set; } = new("Screen");
	[Export] public string ScreenNodeName { get; set; } = "Plane_001";
	[Export] public NodePath ViewportPath { get; set; } = new("ComputerViewport");
	[Export] public float EmissionEnergy { get; set; } = 0.65f;

	private MeshInstance3D _screen = null!;
	private SubViewport _viewport = null!;
	private Node _computerRoot = null!;

	public override void _Ready()
	{
		_computerRoot = ResolveComputerRoot();
		_screen = ResolveScreen();
		_viewport = ResolveViewport();

		if (_screen == null || _viewport == null)
		{
			return;
		}

		ApplyViewportMaterial();
	}

	private Node ResolveComputerRoot()
	{
		if (HasNode(ViewportPath) || HasNode(ScreenPath))
		{
			return this;
		}

		return GetParent() ?? this;
	}

	private MeshInstance3D ResolveScreen()
	{
		if (_computerRoot.HasNode(ScreenPath))
		{
			return _computerRoot.GetNode<MeshInstance3D>(ScreenPath);
		}

		var found = _computerRoot.FindChild(ScreenNodeName, true, false) as MeshInstance3D;
		if (found == null)
		{
			GD.PushError($"{nameof(ComputerScreenRenderer)}: {ScreenNodeName} MeshInstance3D was not found.");
		}

		return found;
	}

	private SubViewport ResolveViewport()
	{
		if (_computerRoot.HasNode(ViewportPath))
		{
			return _computerRoot.GetNode<SubViewport>(ViewportPath);
		}

		var found = _computerRoot.FindChild("ComputerViewport", true, false) as SubViewport;
		if (found == null)
		{
			GD.PushError($"{nameof(ComputerScreenRenderer)}: ComputerViewport was not found.");
		}

		return found;
	}

	private void ApplyViewportMaterial()
	{
		var viewportTexture = _viewport.GetTexture();
		var screenShader = ResourceLoader.Load<Shader>(ScreenShaderPath);
		if (screenShader == null)
		{
			GD.PushError($"{nameof(ComputerScreenRenderer)}: screen shader was not found at {ScreenShaderPath}.");
			return;
		}

		var screenMaterial = new ShaderMaterial { Shader = screenShader };
		screenMaterial.SetShaderParameter("screen_texture", viewportTexture);
		screenMaterial.SetShaderParameter("emission_energy", EmissionEnergy);

		_screen.MaterialOverride = screenMaterial;
		GD.Print($"{nameof(ComputerScreenRenderer)}: viewport {viewportTexture.GetSize()} applied to {_screen.GetPath()}.");
	}
}
