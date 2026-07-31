#nullable enable

using Godot;
using System.Collections.Generic;

public partial class InteractionOutline : Node
{
	[Export] public NodePath VisualRootPath { get; set; } = new("..");
	[Export] public Color OutlineColor { get; set; } = new(0.85f, 0.95f, 0.75f);
	[Export(PropertyHint.Range, "1,4,1")] public int OutlinePixels { get; set; } = 3;

	private readonly List<MeshInstance3D> _sourceMeshes = new();
	private ScreenSpaceOutlineManager? _manager;

	public override void _Ready()
	{
		Node visualRoot = GetNode(VisualRootPath);
		_sourceMeshes.AddRange(FindVisualMeshes(visualRoot));
		_manager = GetOutlineManager();
	}

	public void SetHighlighted(bool isHighlighted)
	{
		_manager ??= GetOutlineManager();
		if (_manager == null)
		{
			return;
		}

		if (isHighlighted)
		{
			_manager.ShowOutline(this, _sourceMeshes, OutlineColor, OutlinePixels);
			return;
		}

		_manager.HideOutline(this);
	}

	public override void _ExitTree()
	{
		_manager?.HideOutline(this);
	}

	private ScreenSpaceOutlineManager? GetOutlineManager()
	{
		return GetTree().GetFirstNodeInGroup(ScreenSpaceOutlineManager.GroupName) as ScreenSpaceOutlineManager;
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

			foreach (MeshInstance3D nestedMesh in FindVisualMeshes(child))
			{
				yield return nestedMesh;
			}
		}
	}
}
