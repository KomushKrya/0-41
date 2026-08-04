#nullable enable

using Godot;

[GlobalClass]
public partial class DossierPresentationLayout : Resource
{
	/// <summary>Transform of the opened dossier relative to the desk computer.</summary>
	[Export] public Transform3D DossierTransform { get; set; } = Transform3D.Identity;

	/// <summary>Transform of the camera framing the computer and dossier.</summary>
	[Export] public Transform3D CameraTransform { get; set; } = Transform3D.Identity;

	/// <summary>Field of view used by the dossier presentation camera.</summary>
	[Export(PropertyHint.Range, "1,179,0.1")] public float CameraFov { get; set; } = 75.0f;
}
