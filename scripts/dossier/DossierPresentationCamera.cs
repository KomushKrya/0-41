#nullable enable

using Godot;

/// <summary>Editor-only camera whose transform is persisted into the shared presentation layout.</summary>
[Tool]
public partial class DossierPresentationCamera : Camera3D
{
	[Export] public DossierPresentationLayout Layout { get; set; } = null!;
	[Export] public NodePath ReferenceNodePath { get; set; } = new("../../DeskComputer");

	private Transform3D _lastTransform;
	private float _lastFov;
	private Node3D? _referenceNode;

	public override void _Ready()
	{
		if (!Engine.IsEditorHint() || Layout == null)
		{
			return;
		}

		_referenceNode = GetNodeOrNull<Node3D>(ReferenceNodePath);
		GlobalTransform = GetReferenceTransform() * Layout.CameraTransform;
		Fov = Layout.CameraFov;
		_lastTransform = GlobalTransform;
		_lastFov = Fov;
	}

	public override void _Process(double delta)
	{
		if (!Engine.IsEditorHint() || Layout == null)
		{
			return;
		}

		bool transformChanged = !GlobalTransform.IsEqualApprox(_lastTransform);
		bool fovChanged = !Mathf.IsEqualApprox(Fov, _lastFov);
		if (!transformChanged && !fovChanged)
		{
			return;
		}

		_lastTransform = GlobalTransform;
		_lastFov = Fov;
		Layout.CameraTransform = GetReferenceTransform().AffineInverse() * GlobalTransform;
		Layout.CameraFov = Fov;
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
