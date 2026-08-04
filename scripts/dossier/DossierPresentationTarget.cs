#nullable enable

using Godot;

/// <summary>Editor-only gizmo that persists the workbench pose into its shared layout resource.</summary>
[Tool]
public partial class DossierPresentationTarget : Node3D
{
	[Export] public DossierPresentationLayout Layout { get; set; } = null!;
	[Export] public NodePath ReferenceNodePath { get; set; } = new("../DeskComputer");

	private Transform3D _lastTransform;
	private Node3D? _referenceNode;

	public override void _Ready()
	{
		if (!Engine.IsEditorHint() || Layout == null)
		{
			return;
		}

		_referenceNode = GetNodeOrNull<Node3D>(ReferenceNodePath);
		GlobalTransform = GetReferenceTransform() * Layout.DossierTransform;
		_lastTransform = GlobalTransform;
	}

	public override void _Process(double delta)
	{
		if (!Engine.IsEditorHint() || Layout == null || GlobalTransform.IsEqualApprox(_lastTransform))
		{
			return;
		}

		_lastTransform = GlobalTransform;
		Layout.DossierTransform = GetReferenceTransform().AffineInverse() * GlobalTransform;
		if (!string.IsNullOrEmpty(Layout.ResourcePath))
		{
			ResourceSaver.Save(Layout, Layout.ResourcePath);
		}
	}

	private Transform3D GetReferenceTransform()
	{
		return _referenceNode?.GlobalTransform ?? Transform3D.Identity;
	}
}
