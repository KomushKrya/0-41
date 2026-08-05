#nullable enable

using Godot;
using System.Collections.Generic;

public partial class ScreenSpaceOutlineManager : Node
{
	public const string GroupName = "screen_space_outline_manager";

	[Export] public NodePath SourceCameraPath { get; set; } = new("../Player/Head/Camera3D");
	private readonly List<(MeshInstance3D Source, MeshInstance3D Mask)> _maskMeshes = new();
	private Camera3D _sourceCamera = null!;
	private SubViewport _maskViewport = null!;
	private Camera3D _maskCamera = null!;
	private Node3D _maskRoot = null!;
	private TextureRect _overlay = null!;
	private ShaderMaterial _overlayMaterial = null!;
	private InteractionOutline? _activeOutline;
	private Vector2I _viewportSize;

	public override void _EnterTree()
	{
		AddToGroup(GroupName);
	}

	public override void _Ready()
	{
		Camera3D? sourceCamera = GetNodeOrNull<Camera3D>(SourceCameraPath);
		if (sourceCamera == null)
		{
			Node player = GetTree().GetFirstNodeInGroup("player");
			sourceCamera = player?.GetNodeOrNull<Camera3D>("Head/Camera3D");
		}
		if (sourceCamera == null)
		{
			GD.PushError("ScreenSpaceOutlineManager: source camera is not available.");
			return;
		}
		_sourceCamera = sourceCamera;
		CreateMaskViewport();
		CreateOverlay();
		ResizeMaskViewport();
	}

	public override void _Process(double delta)
	{
		if (_activeOutline == null)
		{
			return;
		}

		ResizeMaskViewport();
		SyncMaskCamera();
		SyncMaskMeshTransforms();
	}

	public void ShowOutline(
		InteractionOutline outline,
		IReadOnlyList<MeshInstance3D> sourceMeshes,
		Color outlineColor,
		int outlinePixels)
	{
		if (_activeOutline != outline)
		{
			ClearMaskMeshes();
			CreateMaskMeshes(sourceMeshes);
			_activeOutline = outline;
		}

		_overlayMaterial.SetShaderParameter("outline_color", outlineColor);
		_overlayMaterial.SetShaderParameter("outline_width", Mathf.Clamp(outlinePixels, 1, 4));
		_overlay.Visible = true;
		_maskViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		ResizeMaskViewport();
		SyncMaskCamera();
		SyncMaskMeshTransforms();
	}

	public void HideOutline(InteractionOutline outline)
	{
		if (_activeOutline != outline)
		{
			return;
		}

		_overlay.Visible = false;
		_maskViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		_activeOutline = null;
		ClearMaskMeshes();
	}

	private void CreateMaskViewport()
	{
		_maskViewport = new SubViewport
		{
			Name = "OutlineMaskViewport",
			TransparentBg = true,
			OwnWorld3D = true,
			HandleInputLocally = false,
			RenderTargetClearMode = SubViewport.ClearMode.Always,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
		};
		AddChild(_maskViewport);

		_maskRoot = new Node3D { Name = "OutlineMaskRoot" };
		_maskViewport.AddChild(_maskRoot);

		_maskCamera = new Camera3D
		{
			Name = "OutlineMaskCamera",
			Current = true
		};
		_maskViewport.AddChild(_maskCamera);
	}

	private void CreateOverlay()
	{
		Shader shader = GD.Load<Shader>("res://assets/shaders/ScreenSpaceOutline.gdshader");
		_overlayMaterial = new ShaderMaterial { Shader = shader };

		CanvasLayer overlayLayer = new()
		{
			Name = "OutlineOverlayLayer",
			Layer = 90
		};
		AddChild(overlayLayer);

		_overlay = new TextureRect
		{
			Name = "OutlineOverlay",
			Texture = _maskViewport.GetTexture(),
			Material = _overlayMaterial,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			Visible = false
		};
		_overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		overlayLayer.AddChild(_overlay);
	}

	private void ResizeMaskViewport()
	{
		Vector2 visibleSize = GetViewport().GetVisibleRect().Size;
		Vector2I size = new(
			Mathf.Max(1, Mathf.RoundToInt(visibleSize.X)),
			Mathf.Max(1, Mathf.RoundToInt(visibleSize.Y))
		);

		if (size == _viewportSize)
		{
			return;
		}

		_viewportSize = size;
		_maskViewport.Size = size;
	}

	private void SyncMaskCamera()
	{
		_maskCamera.GlobalTransform = _sourceCamera.GlobalTransform;
		_maskCamera.Projection = _sourceCamera.Projection;
		_maskCamera.KeepAspect = _sourceCamera.KeepAspect;
		_maskCamera.Fov = _sourceCamera.Fov;
		_maskCamera.Size = _sourceCamera.Size;
		_maskCamera.Near = _sourceCamera.Near;
		_maskCamera.Far = _sourceCamera.Far;
		_maskCamera.HOffset = _sourceCamera.HOffset;
		_maskCamera.VOffset = _sourceCamera.VOffset;
	}

	private void CreateMaskMeshes(IReadOnlyList<MeshInstance3D> sourceMeshes)
	{
		StandardMaterial3D maskMaterial = new()
		{
			AlbedoColor = Colors.White,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
		};

		foreach (MeshInstance3D sourceMesh in sourceMeshes)
		{
			if (sourceMesh.Mesh == null)
			{
				continue;
			}

			MeshInstance3D maskMesh = new()
			{
				Name = $"{sourceMesh.Name}_Mask",
				Mesh = sourceMesh.Mesh,
				MaterialOverride = maskMaterial,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
			};
			_maskRoot.AddChild(maskMesh);
			maskMesh.GlobalTransform = sourceMesh.GlobalTransform;
			_maskMeshes.Add((sourceMesh, maskMesh));
		}
	}

	private void SyncMaskMeshTransforms()
	{
		foreach ((MeshInstance3D sourceMesh, MeshInstance3D maskMesh) in _maskMeshes)
		{
			maskMesh.GlobalTransform = sourceMesh.GlobalTransform;
		}
	}

	private void ClearMaskMeshes()
	{
		foreach ((MeshInstance3D _, MeshInstance3D maskMesh) in _maskMeshes)
		{
			maskMesh.QueueFree();
		}

		_maskMeshes.Clear();
	}
}
