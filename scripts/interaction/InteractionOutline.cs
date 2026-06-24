using Godot;
using System.Collections.Generic;

public partial class InteractionOutline : Node
{
	[Export] public NodePath VisualRootPath { get; set; } = new("..");
	[Export] public Color OutlineColor { get; set; } = new(0.85f, 0.95f, 0.75f);
	[Export] public float OutlineWidth { get; set; } = 0.035f;

	private readonly List<MeshInstance3D> _outlineMeshes = new();

	public override void _Ready()
	{
		Node visualRoot = GetNode(VisualRootPath);
		StandardMaterial3D outlineMaterial = CreateOutlineMaterial();
		List<MeshInstance3D> sourceMeshes = new(FindVisualMeshes(visualRoot));

		foreach (MeshInstance3D sourceMesh in sourceMeshes)
		{
			MeshInstance3D outlineMesh = new()
			{
				Name = $"{sourceMesh.Name}_Outline",
				Mesh = sourceMesh.Mesh,
				MaterialOverride = outlineMaterial,
				Transform = Transform3D.Identity,
				Visible = false
			};

			sourceMesh.AddChild(outlineMesh);
			_outlineMeshes.Add(outlineMesh);
		}
	}

	public void SetHighlighted(bool isHighlighted)
	{
		foreach (MeshInstance3D outlineMesh in _outlineMeshes)
		{
			outlineMesh.Visible = isHighlighted;
		}
	}

	private StandardMaterial3D CreateOutlineMaterial()
	{
		return new StandardMaterial3D
		{
			AlbedoColor = OutlineColor,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Front,
			Grow = true,
			GrowAmount = OutlineWidth
		};
	}

	private static IEnumerable<MeshInstance3D> FindVisualMeshes(Node root)
	{
		if (root is MeshInstance3D rootMesh)
		{
			yield return rootMesh;
		}

		foreach (Node child in root.GetChildren())
		{
			if (child is InteractionOutline)
			{
				continue;
			}

			if (child is MeshInstance3D meshInstance)
			{
				yield return meshInstance;
			}

			foreach (MeshInstance3D nestedMesh in FindVisualMeshes(child))
			{
				yield return nestedMesh;
			}
		}
	}
}
