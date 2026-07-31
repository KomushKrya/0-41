using System.Collections.Generic;
using Godot;

public partial class NewComputerScreenPreview : Node3D
{
	private const string OutputPath = "res://scenes/debug/NewComputerScreenPreview.png";

	private SubViewport _viewport = null!;
	private Node _modelRoot = null!;

	public override async void _Ready()
	{
		_viewport = GetNode<SubViewport>("ComputerViewport");
		_modelRoot = GetNode("ComputerModel");

		var meshes = new List<MeshInstance3D>();
		CollectMeshes(_modelRoot, meshes);
		foreach (var mesh in meshes)
		{
			GD.Print($"New PC mesh: {mesh.GetPath()} | AABB: {mesh.GetAabb()}");
			PrintUvRange(mesh);
		}

		MeshInstance3D screen = FindScreenMesh(meshes);
		if (screen == null)
		{
			GD.PushError("New PC preview: screen mesh was not found.");
		}
		else
		{
			ApplyViewportMaterial(screen);
			GD.Print($"New PC preview: viewport applied to {screen.GetPath()}.");
		}

		for (int frame = 0; frame < 8; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		string absoluteOutputPath = ProjectSettings.GlobalizePath(OutputPath);
		Error result = GetViewport().GetTexture().GetImage().SavePng(absoluteOutputPath);
		if (result == Error.Ok)
		{
			GD.Print($"New PC preview saved to {absoluteOutputPath}");
		}
		else
		{
			GD.PushError($"New PC preview could not be saved: {result}.");
		}

		GetTree().Quit();
	}

	private void ApplyViewportMaterial(MeshInstance3D screen)
	{
		var shader = new Shader
		{
			Code = """
				shader_type spatial;
				render_mode unshaded, cull_disabled;

				uniform sampler2D screen_texture : source_color, filter_nearest;

				void fragment() {
					float normalized_u = clamp((UV.x - 0.17662379) / 0.67052755, 0.0, 1.0);
					vec2 screen_uv = vec2(UV.y, 1.0 - normalized_u);
					vec3 screen_color = texture(screen_texture, screen_uv).rgb;
					ALBEDO = screen_color;
					EMISSION = screen_color * 0.65;
				}
				"""
		};

		var material = new ShaderMaterial { Shader = shader };
		material.SetShaderParameter("screen_texture", _viewport.GetTexture());
		screen.MaterialOverride = material;
	}

	private static MeshInstance3D FindScreenMesh(IEnumerable<MeshInstance3D> meshes)
    {
        foreach (var mesh in meshes)
        {
            string name = mesh.Name.ToString().ToLowerInvariant();
            if (name == "plane")
            {
                return mesh;
            }

            if (name.Contains("screen") || name.Contains("display") || name.Contains("monitor"))
            {
                return mesh;
            }
		}

		return null;
	}

	private static void PrintUvRange(MeshInstance3D meshInstance)
	{
		if (meshInstance.Mesh is not ArrayMesh mesh || mesh.GetSurfaceCount() == 0)
		{
			return;
		}

		Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
		Vector2[] uvs = (Vector2[])arrays[(int)ArrayMesh.ArrayType.TexUV];
		if (uvs.Length == 0)
		{
			GD.Print($"New PC UV: {meshInstance.Name} | none");
			return;
		}

		Vector2 min = uvs[0];
		Vector2 max = uvs[0];
		foreach (Vector2 uv in uvs)
		{
			min = min.Min(uv);
			max = max.Max(uv);
		}

		GD.Print($"New PC UV: {meshInstance.Name} | min: {min} | max: {max}");
	}

	private static void CollectMeshes(Node node, List<MeshInstance3D> meshes)
	{
		if (node is MeshInstance3D mesh)
		{
			meshes.Add(mesh);
		}

		foreach (Node child in node.GetChildren())
		{
			CollectMeshes(child, meshes);
		}
	}
}
