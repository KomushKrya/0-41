using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;

public partial class DebugInterfaceOverlay : CanvasLayer
{
	private const int MaxRecentCoreEvents = 8;

	[Export] public NodePath PanelPath { get; set; } = new("Panel");
	[Export] public NodePath PreviewPath { get; set; } = new("Panel/MarginContainer/VBoxContainer/Preview");
	[Export] public NodePath TitlePath { get; set; } = new("Panel/MarginContainer/VBoxContainer/Title");
	[Export] public NodePath HelpPath { get; set; } = new("Panel/MarginContainer/VBoxContainer/Help");
	[Export] public NodePath SessionReadoutPath { get; set; } = new("SessionReadout");
	[Export] public NodePath InteractionReadoutPath { get; set; } = new("InteractionReadout");
	[Export] public NodePath CenterRayMarkerPath { get; set; } = new("CenterRayMarker");
	[Export] public NodePath PlayerPath { get; set; } = new("../Player");
	[Export] public NodePath InteractionRayPath { get; set; } = new("../Player/Head/Camera3D/InteractionRay");
	[Export] public NodePath PcViewportPath { get; set; } = new("");
	[Export] public NodePath MapViewportPath { get; set; } = new("");
	[Export] public NodePath DossierViewportPath { get; set; } = new("");
	[Export] public NodePath NotebookViewportPath { get; set; } = new("");

	private Control _panel = null!;
	private TextureRect _preview = null!;
	private Label _title = null!;
	private Label _help = null!;
	private Label _sessionReadout = null!;
	private Label _interactionReadout = null!;
	private Control _centerRayMarker = null!;
	private FlyPlayer _player = null!;
	private RayCast3D _interactionRay = null!;
	private SubViewport _activeViewport = null!;
	private Control _interactionAreaDebugRoot = null!;
	private ColorRect _viewportAreaRect = null!;
	private readonly List<ColorRect> _interactionAreaRects = new();
	private readonly List<string> _recentCoreEvents = new();
	private GameRuntime _runtime = null!;
	private IDisposable _coreEventSubscription;
	private Vector2 _lastViewportMousePosition;
	private bool _hasLastViewportMousePosition;
	private bool _isDebugModeEnabled;
	private bool _isInteractionAreaDebugEnabled;
	private bool _isMapLayoutDebugEnabled = true;
	private bool _isSessionReadoutEnabled;
	private bool _isInteractionRayReadoutEnabled;
	private string _activeInterfaceName = "none";

	public override void _Ready()
	{
		_panel = GetNode<Control>(PanelPath);
		_preview = GetNode<TextureRect>(PreviewPath);
		_title = GetNode<Label>(TitlePath);
		_help = GetNode<Label>(HelpPath);
		_sessionReadout = GetNode<Label>(SessionReadoutPath);
		_interactionReadout = GetNode<Label>(InteractionReadoutPath);
		_centerRayMarker = GetNode<Control>(CenterRayMarkerPath);
		_player = GetNodeOrNull<FlyPlayer>(PlayerPath);
		_interactionRay = GetNodeOrNull<RayCast3D>(InteractionRayPath);

		CreateInteractionAreaDebugOverlay();
		_panel.Visible = false;
		_sessionReadout.Visible = false;
		_interactionReadout.Visible = false;
		_centerRayMarker.Visible = false;

		_runtime = GameRuntime.Get(this);
		if (_runtime != null && _runtime.IsReady)
		{
			_coreEventSubscription = _runtime.Session.Events.SubscribeAll(OnCoreEvent);
		}

		UpdateText();
	}

	public override void _ExitTree()
	{
		_coreEventSubscription?.Dispose();
		_coreEventSubscription = null;
	}

	public override void _Process(double delta)
	{
		if (_isSessionReadoutEnabled)
		{
			UpdateSessionReadout();
		}

		if (_isInteractionRayReadoutEnabled)
		{
			UpdateInteractionRayReadout();
		}

		UpdateInteractionAreaDebugOverlay();
	}

	public override void _Input(InputEvent @event)
	{
		if (!_isDebugModeEnabled || _activeViewport == null)
		{
			return;
		}

		if (@event is InputEventMouseMotion motion)
		{
			if (TryGetViewportMousePosition(motion.Position, out Vector2 viewportPosition))
			{
				ForwardMouseMotion(motion, viewportPosition);
				GetViewport().SetInputAsHandled();
			}
			else
			{
				_hasLastViewportMousePosition = false;
			}
		}
		else if (@event is InputEventMouseButton button)
		{
			if (TryGetViewportMousePosition(button.Position, out Vector2 viewportPosition))
			{
				ForwardMouseButton(button, viewportPosition);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
		{
			return;
		}

		if (keyEvent.Keycode == Key.F3)
		{
			SetDebugModeEnabled(!_isDebugModeEnabled);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (keyEvent.Keycode == Key.F1)
		{
			SetInteractionRayReadoutEnabled(!_isInteractionRayReadoutEnabled);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (keyEvent.Keycode == Key.F2)
		{
			SetSessionReadoutEnabled(!_isSessionReadoutEnabled);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (!_isDebugModeEnabled)
		{
			return;
		}

		if (IsActiveViewportTextInputFocused() && keyEvent.Keycode is not Key.F1 and not Key.F2 and not Key.F3 and not Key.F4 and not Key.F5 and not Key.F12 and not Key.Escape)
		{
			_activeViewport.PushInput(keyEvent, true);
			GetViewport().SetInputAsHandled();
			return;
		}

		switch (keyEvent.Keycode)
		{
			case Key.Key1:
				ShowInterface("PC", PcViewportPath);
				break;
			case Key.Key2:
				ShowInterface("MAP", MapViewportPath);
				break;
			case Key.Key3:
				ShowInterface("DOSSIER", DossierViewportPath);
				break;
			case Key.Key4:
				ShowInterface("NOTEBOOK", NotebookViewportPath);
				break;
			case Key.F4:
				SetInteractionAreaDebugEnabled(!_isInteractionAreaDebugEnabled);
				break;
			case Key.F5:
				SetMapLayoutDebugEnabled(!_isMapLayoutDebugEnabled);
				break;
			case Key.Escape:
				CloseInterface();
				break;
			default:
				return;
		}

		GetViewport().SetInputAsHandled();
	}

	private bool IsActiveViewportTextInputFocused()
	{
		return _activeViewport?.GuiGetFocusOwner() is LineEdit;
	}

	private void SetDebugModeEnabled(bool isEnabled)
	{
		_isDebugModeEnabled = isEnabled;
		_panel.Visible = isEnabled;

		if (!isEnabled)
		{
			CloseInterface();
			SetInteractionAreaDebugEnabled(false);
			Input.MouseMode = Input.MouseModeEnum.Captured;
			return;
		}

		Input.MouseMode = Input.MouseModeEnum.Visible;
		UpdateText();
	}

	private void SetSessionReadoutEnabled(bool isEnabled)
	{
		_isSessionReadoutEnabled = isEnabled;
		_sessionReadout.Visible = isEnabled;

		if (isEnabled)
		{
			UpdateSessionReadout();
		}

		UpdateText();
	}

	private void SetInteractionRayReadoutEnabled(bool isEnabled)
	{
		_isInteractionRayReadoutEnabled = isEnabled;
		_interactionReadout.Visible = isEnabled;
		_centerRayMarker.Visible = isEnabled;

		if (isEnabled)
		{
			UpdateInteractionRayReadout();
		}

		UpdateText();
	}

	private void UpdateSessionReadout()
	{
		if (_runtime == null || !_runtime.IsReady)
		{
			_sessionReadout.Text =
				$"GAME SESSION\nCORE OFFLINE\n{_runtime?.LoadError ?? "KONTUR AUTOLOAD NOT FOUND"}";
			return;
		}

		ShiftStatusView status = _runtime.Session.GetStatus();
		int elapsedSeconds = Mathf.FloorToInt((float)status.ShiftTime);
		int hours = elapsedSeconds / 3600;
		int minutes = elapsedSeconds % 3600 / 60;
		int seconds = elapsedSeconds % 60;
		string shiftState = status.IsGameOver
			? $"GAME OVER: {status.GameOverReason}"
			: status.IsShiftActive
				? "IN PROGRESS"
				: status.Day == 0 ? "NOT STARTED" : "BETWEEN SHIFTS";

		_sessionReadout.Text =
			$"GAME SESSION\n" +
			$"DAY: {status.Day} / {_runtime.Session.Config.Days.Count}\n" +
			$"SHIFT: {shiftState}\n" +
			$"ELAPSED: {hours:00}:{minutes:00}:{seconds:00}\n" +
			$"INCIDENTS: {status.OpenIncidents} | PENDING: {status.PendingCalls}\n" +
			$"SCALES: {status.Scales}\n\n" +
			$"EVENT BUS\n{BuildEventBusReadout()}";
	}

	private void UpdateInteractionRayReadout()
	{
		_interactionReadout.Text = BuildInteractionRayReadout();
	}

	private string BuildInteractionRayReadout()
	{
		if (_player == null || _interactionRay == null)
		{
			return "INTERACTION RAY\nNOT FOUND";
		}

		_interactionRay.ForceRaycastUpdate();
		string state =
			$"INTERACTION RAY\n" +
			$"SEATED: {_player.IsSeated} | FOCUSED: {_player.IsViewFocused} | TRANSITION: {_player.IsCameraTransitioning} | NOCLIP: {_player.IsNoclipEnabled}\n" +
			$"ORIGIN: {FormatVector(_interactionRay.GlobalPosition)}\n" +
			$"TARGET: {FormatVector(_interactionRay.ToGlobal(_interactionRay.TargetPosition))}\n" +
			$"MASK: {_interactionRay.CollisionMask}";

		if (!_interactionRay.IsColliding())
		{
			return $"{state}\nHIT: none";
		}

		GodotObject collider = _interactionRay.GetCollider();
		Vector3 point = _interactionRay.GetCollisionPoint();
		float distance = _interactionRay.GlobalPosition.DistanceTo(point);
		if (collider is not Node colliderNode)
		{
			return $"{state}\nHIT: {collider.GetClass()}\nPOINT: {FormatVector(point)} | DIST: {distance:0.00}";
		}

		IInteractable interactable = FindInteractable(colliderNode);
		string interactableState = interactable == null
			? "INTERACTABLE: none"
			: $"INTERACTABLE: {((Node)interactable).GetPath()} | CAN: {interactable.CanInteract(_player)}";

		return
			$"{state}\n" +
			$"HIT: {colliderNode.GetPath()} ({colliderNode.GetClass()})\n" +
			$"POINT: {FormatVector(point)} | DIST: {distance:0.00}\n" +
			interactableState;
	}

	private static IInteractable FindInteractable(Node node)
	{
		Node current = node;
		while (current != null)
		{
			if (current is IInteractable interactable)
			{
				return interactable;
			}

			current = current.GetParent();
		}

		return null;
	}

	private static string FormatVector(Vector3 value)
	{
		return $"({value.X:0.00}, {value.Y:0.00}, {value.Z:0.00})";
	}

	private string BuildEventBusReadout()
	{
		return _recentCoreEvents.Count == 0
			? "  (no events)"
			: "  " + string.Join("\n  ", _recentCoreEvents);
	}

	private void OnCoreEvent(IGameEvent gameEvent)
	{
		_recentCoreEvents.Add(gameEvent.GetType().Name);

		if (_recentCoreEvents.Count > MaxRecentCoreEvents)
		{
			_recentCoreEvents.RemoveAt(0);
		}
	}

	private void SetMapLayoutDebugEnabled(bool isEnabled)
	{
		SubViewport mapViewport = ResolveViewport(MapViewportPath);
		MapBuildingEditor mapEditor = mapViewport?.GetNodeOrNull<MapBuildingEditor>("MapUI");
		if (mapEditor == null)
		{
			return;
		}

		_isMapLayoutDebugEnabled = isEnabled;
		mapEditor.SetLayoutDebugEnabled(isEnabled);
		UpdateText();
	}

	private void ShowInterface(string interfaceName, NodePath viewportPath)
	{
		_activeViewport = ResolveViewport(viewportPath);
		_hasLastViewportMousePosition = false;

		if (_activeViewport == null)
		{
			_activeInterfaceName = $"{interfaceName}: viewport not found";
			_preview.Texture = null;
			UpdateText();
			return;
		}

		_activeInterfaceName = interfaceName;
		_preview.Texture = _activeViewport.GetTexture();
		UpdateText();
	}

	private SubViewport ResolveViewport(NodePath viewportPath)
	{
		if (string.IsNullOrWhiteSpace(viewportPath.ToString()))
		{
			return null;
		}

		return GetNodeOrNull<SubViewport>(viewportPath);
	}

	private void CloseInterface()
	{
		_activeInterfaceName = "none";
		_activeViewport = null;
		_hasLastViewportMousePosition = false;
		HideInteractionAreaDebugRects();

		if (_preview != null)
		{
			_preview.Texture = null;
		}

		UpdateText();
	}

	private void SetInteractionAreaDebugEnabled(bool isEnabled)
	{
		_isInteractionAreaDebugEnabled = isEnabled;
		UpdateInteractionAreaDebugOverlay();
		UpdateText();
	}

	private bool TryGetViewportMousePosition(Vector2 screenPosition, out Vector2 viewportPosition)
	{
		viewportPosition = Vector2.Zero;

		Rect2 contentRect = GetPreviewContentRect();
		if (!contentRect.HasPoint(screenPosition))
		{
			return false;
		}

		Vector2 normalizedPosition = (screenPosition - contentRect.Position) / contentRect.Size;
		Vector2 viewportSize = _activeViewport.Size;
		viewportPosition = normalizedPosition * viewportSize;
		return true;
	}

	private Rect2 GetPreviewContentRect()
	{
		Rect2 previewRect = _preview.GetGlobalRect();

		if (_activeViewport == null)
		{
			return previewRect;
		}

		Vector2 viewportSize = _activeViewport.Size;
		if (viewportSize.X <= 0.0f || viewportSize.Y <= 0.0f)
		{
			return previewRect;
		}

		float scale = Mathf.Min(previewRect.Size.X / viewportSize.X, previewRect.Size.Y / viewportSize.Y);
		Vector2 contentSize = viewportSize * scale;
		Vector2 contentPosition = previewRect.Position + ((previewRect.Size - contentSize) * 0.5f);
		return new Rect2(contentPosition, contentSize);
	}

	private void ForwardMouseMotion(InputEventMouseMotion sourceEvent, Vector2 viewportPosition)
	{
		Vector2 relative = _hasLastViewportMousePosition
			? viewportPosition - _lastViewportMousePosition
			: Vector2.Zero;

		var forwardedEvent = new InputEventMouseMotion
		{
			Position = viewportPosition,
			GlobalPosition = viewportPosition,
			Relative = relative,
			Velocity = sourceEvent.Velocity,
			ButtonMask = sourceEvent.ButtonMask
		};

		_lastViewportMousePosition = viewportPosition;
		_hasLastViewportMousePosition = true;
		_activeViewport.PushInput(forwardedEvent, true);
	}

	private void ForwardMouseButton(InputEventMouseButton sourceEvent, Vector2 viewportPosition)
	{
		var forwardedEvent = new InputEventMouseButton
		{
			Position = viewportPosition,
			GlobalPosition = viewportPosition,
			ButtonIndex = sourceEvent.ButtonIndex,
			Pressed = sourceEvent.Pressed,
			DoubleClick = sourceEvent.DoubleClick,
			ButtonMask = sourceEvent.ButtonMask,
			Factor = sourceEvent.Factor
		};

		_lastViewportMousePosition = viewportPosition;
		_hasLastViewportMousePosition = true;
		_activeViewport.PushInput(forwardedEvent, true);
	}

	private void CreateInteractionAreaDebugOverlay()
	{
		_interactionAreaDebugRoot = new Control
		{
			Name = "InteractionAreaDebugOverlay",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
			ZIndex = 1000
		};
		_interactionAreaDebugRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_interactionAreaDebugRoot);

		_viewportAreaRect = new ColorRect
		{
			Name = "ViewportArea",
			Color = new Color(0.12f, 0.62f, 1.0f, 0.13f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_interactionAreaDebugRoot.AddChild(_viewportAreaRect);
	}

	private void UpdateInteractionAreaDebugOverlay()
	{
		if (_interactionAreaDebugRoot == null)
		{
			return;
		}

		if (!_isDebugModeEnabled || !_isInteractionAreaDebugEnabled || _activeViewport == null)
		{
			HideInteractionAreaDebugRects();
			return;
		}

		_interactionAreaDebugRoot.Visible = true;
		Rect2 contentRect = GetPreviewContentRect();
		_viewportAreaRect.Position = contentRect.Position;
		_viewportAreaRect.Size = contentRect.Size;
		_viewportAreaRect.Visible = true;

		var interactiveControls = new List<Control>();
		CollectInteractiveControls(_activeViewport, interactiveControls);

		for (int i = 0; i < interactiveControls.Count; i++)
		{
			ColorRect areaRect = GetOrCreateInteractionAreaRect(i);
			Rect2 screenRect = ConvertViewportRectToPreviewRect(interactiveControls[i].GetGlobalRect(), contentRect);
			areaRect.Position = screenRect.Position;
			areaRect.Size = screenRect.Size;
			areaRect.Visible = true;
		}

		for (int i = interactiveControls.Count; i < _interactionAreaRects.Count; i++)
		{
			_interactionAreaRects[i].Visible = false;
		}
	}

	private Rect2 ConvertViewportRectToPreviewRect(Rect2 viewportRect, Rect2 contentRect)
	{
		Vector2 viewportSize = _activeViewport.Size;
		Vector2 position = contentRect.Position + ((viewportRect.Position / viewportSize) * contentRect.Size);
		Vector2 size = (viewportRect.Size / viewportSize) * contentRect.Size;
		return new Rect2(position, size);
	}

	private void CollectInteractiveControls(Node node, List<Control> interactiveControls)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is Control control && control.IsVisibleInTree() && (control is BaseButton || control is LineEdit))
			{
				interactiveControls.Add(control);
			}

			CollectInteractiveControls(child, interactiveControls);
		}
	}

	private ColorRect GetOrCreateInteractionAreaRect(int index)
	{
		while (_interactionAreaRects.Count <= index)
		{
			var areaRect = new ColorRect
			{
				Name = $"InteractionArea{_interactionAreaRects.Count}",
				Color = new Color(1.0f, 0.82f, 0.18f, 0.35f),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			_interactionAreaDebugRoot.AddChild(areaRect);
			_interactionAreaRects.Add(areaRect);
		}

		return _interactionAreaRects[index];
	}

	private void HideInteractionAreaDebugRects()
	{
		if (_interactionAreaDebugRoot == null)
		{
			return;
		}

		_interactionAreaDebugRoot.Visible = false;

		if (_viewportAreaRect != null)
		{
			_viewportAreaRect.Visible = false;
		}

		foreach (ColorRect areaRect in _interactionAreaRects)
		{
			areaRect.Visible = false;
		}
	}

	private void UpdateText()
	{
		if (_title == null || _help == null)
		{
			return;
		}

		string areasState = _isInteractionAreaDebugEnabled ? "areas:on" : "areas:off";
		string layoutState = _isMapLayoutDebugEnabled ? "map-layout:on" : "map-layout:off";
		_title.Text = $"DEBUG INTERFACE: {_activeInterfaceName} | {areasState} | {layoutState}";
		_help.Text = "F1: interaction ray | F2: session data | F3: debug on/off | F4: interaction areas | F5: map layout | F6: core simulation | F12: noclip | 1: PC | 2: MAP | 3: DOSSIER | 4: NOTEBOOK | Esc: close";
	}
}
