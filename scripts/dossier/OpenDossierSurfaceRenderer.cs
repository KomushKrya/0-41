using Godot;

/// <summary>Renders the left and right halves of one dossier UI texture onto separate 3D pages.</summary>
[Tool]
public partial class OpenDossierSurfaceRenderer : Node
{
	[Export] public NodePath LeftPagePath { get; set; } = new("../VisualRoot/LeftPageSurface");
	[Export] public NodePath RightPagePath { get; set; } = new("../VisualRoot/RightPageSurface");
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

	private ShaderMaterial CreatePageMaterial(Texture2D texture, float offset)
	{
		var material = new ShaderMaterial { Shader = PageShader };
		material.SetShaderParameter("dossier_texture", texture);
		material.SetShaderParameter("page_offset", offset);
		material.SetShaderParameter("emission_energy", EmissionEnergy);
		return material;
	}
}
