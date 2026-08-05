using Godot;

/// <summary>Renders the two halves of the dossier spread onto the 3D page surfaces.</summary>
[Tool]
public partial class OpenDossierSurfaceRenderer : Node
{
	[Export] public NodePath LeftPagePath { get; set; } = new("../VisualRoot/CoverPivot/LeftPageSurface");
	[Export] public NodePath RightPagePath { get; set; } = new("../VisualRoot/RightPage/RightPageSurface");
	[Export] public NodePath ViewportPath { get; set; } = new("../DossierViewport");
	[Export] public Shader PageShader { get; set; } = null!;
	[Export(PropertyHint.Range, "0,1,0.01")] public float EmissionEnergy { get; set; } = 0.08f;

	public override void _Ready()
	{
		MeshInstance3D leftPage = GetNodeOrNull<MeshInstance3D>(LeftPagePath);
		MeshInstance3D rightPage = GetNodeOrNull<MeshInstance3D>(RightPagePath);
		SubViewport viewport = GetNodeOrNull<SubViewport>(ViewportPath);
		if (leftPage == null || rightPage == null || viewport == null || PageShader == null)
		{
			GD.PushError($"{nameof(OpenDossierSurfaceRenderer)}: pages, viewport, or shader are not assigned.");
			return;
		}

		leftPage.MaterialOverride = CreatePageMaterial(viewport.GetTexture(), 0.0f);
		rightPage.MaterialOverride = CreatePageMaterial(viewport.GetTexture(), 0.5f);
	}

	/// <param name="horizontalOffset">Left half of the spread is 0.0, right half is 0.5.</param>
	private ShaderMaterial CreatePageMaterial(Texture2D texture, float horizontalOffset)
	{
		var material = new ShaderMaterial { Shader = PageShader };
		material.SetShaderParameter("dossier_texture", texture);
		material.SetShaderParameter("emission_energy", EmissionEnergy);
		material.SetShaderParameter("uv_offset", new Vector2(horizontalOffset, 0.0f));
		material.SetShaderParameter("uv_scale", new Vector2(0.5f, 1.0f));
		return material;
	}
}
