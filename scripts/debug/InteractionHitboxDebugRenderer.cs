using System.Collections.Generic;
using Godot;

/// <summary>
/// Показывает реальные коллайдеры объектов, доступных через <see cref="IInteractable"/>.
/// Визуализация создаётся только в режиме отладки и не участвует в физике.
/// </summary>
public partial class InteractionHitboxDebugRenderer : Node
{
	private const string DebugMeshMeta = "interaction_hitbox_debug_mesh";
	private const double RescanIntervalSeconds = 0.25;

	private readonly List<MeshInstance3D> _debugMeshes = new();
	private double _rescanRemaining;
	private bool _isEnabled;

	public void SetEnabled(bool isEnabled)
	{
		if (_isEnabled == isEnabled)
		{
			return;
		}

		_isEnabled = isEnabled;
		if (isEnabled)
		{
			Rebuild();
		}
		else
		{
			Clear();
		}
	}

	public override void _Process(double delta)
	{
		if (!_isEnabled)
		{
			return;
		}

		_rescanRemaining -= delta;
		if (_rescanRemaining <= 0.0)
		{
			Rebuild();
		}
	}

	public override void _ExitTree()
	{
		Clear();
	}

	private void Rebuild()
	{
		_rescanRemaining = RescanIntervalSeconds;
		Clear();
		CollectInteractableHitboxes(GetTree().Root);
	}

	private void CollectInteractableHitboxes(Node node)
	{
		if (node is Area3D area && area is IInteractable)
		{
			AddHitboxes(area);
		}

		foreach (Node child in node.GetChildren())
		{
			CollectInteractableHitboxes(child);
		}
	}

	private void AddHitboxes(Area3D area)
	{
		var collisionShapes = new List<CollisionShape3D>();
		CollectCollisionShapes(area, collisionShapes);
		foreach (CollisionShape3D collisionShape in collisionShapes)
		{
			Mesh mesh = CreateDebugMesh(collisionShape.Shape);
			if (mesh == null)
			{
				continue;
			}

			var debugMesh = new MeshInstance3D
			{
				Name = "InteractionHitboxDebug",
				Mesh = mesh,
				MaterialOverride = CreateDebugMaterial(),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			debugMesh.SetMeta(DebugMeshMeta, true);
			collisionShape.AddChild(debugMesh);
			_debugMeshes.Add(debugMesh);
		}
	}

	private static void CollectCollisionShapes(Node node, List<CollisionShape3D> result)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child.HasMeta(DebugMeshMeta))
			{
				continue;
			}

			if (child is CollisionShape3D collisionShape)
			{
				result.Add(collisionShape);
				continue;
			}

			CollectCollisionShapes(child, result);
		}
	}

	private static Mesh CreateDebugMesh(Shape3D shape)
	{
		return shape switch
		{
			BoxShape3D box => new BoxMesh { Size = box.Size },
			SphereShape3D sphere => new SphereMesh { Radius = sphere.Radius, Height = sphere.Radius * 2.0f },
			CapsuleShape3D capsule => new CapsuleMesh { Radius = capsule.Radius, Height = capsule.Height },
			CylinderShape3D cylinder => new CylinderMesh
			{
				TopRadius = cylinder.Radius,
				BottomRadius = cylinder.Radius,
				Height = cylinder.Height,
			},
			_ => null,
		};
	}

	private static StandardMaterial3D CreateDebugMaterial()
	{
		Color color = new(0.08f, 0.92f, 1.0f, 0.28f);
		return new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = color,
			EmissionEnabled = true,
			Emission = new Color(0.08f, 0.92f, 1.0f, 1.0f),
			EmissionEnergyMultiplier = 0.75f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
	}

	private void Clear()
	{
		foreach (MeshInstance3D debugMesh in _debugMeshes)
		{
			if (IsInstanceValid(debugMesh))
			{
				debugMesh.QueueFree();
			}
		}

		_debugMeshes.Clear();
	}
}
