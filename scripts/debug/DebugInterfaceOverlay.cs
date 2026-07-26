using Godot;
using System.Collections.Generic;

public partial class DebugInterfaceOverlay : CanvasLayer
{
	[Export] public NodePath PanelPath { get; set; } = new("Panel");
	[Export] public NodePath PreviewPath { get; set; } = new("Panel/MarginContainer/VBoxContainer/Preview");
	[Export] public NodePath TitlePath { get; set; } = new("Panel/MarginContainer/VBoxContainer/Title");
	[Export] public NodePath HelpPath { get; set; } = new("Panel/MarginContainer/VBoxContainer/Help");
	[Export] public NodePath PcViewportPath { get; set; } = new("");
	[Export] public NodePath MapViewportPath { get; set; } = new("");
	[Export] public NodePath DossierViewportPath { get; set; } = new("");
	[Export] public NodePath NotebookViewportPath { get; set; } = new("");

	private Control _panel = null!;
	private TextureRect _preview = null!;
	private Label _title = null!;
	private Label _help = null!;
	private SubViewport _activeViewport = null!;
	private Control _interactionAreaDebugRoot = null!;
	private ColorRect _viewportAreaRect = null!;
	private readonly List<ColorRect> _interactionAreaRects = new();
	private Vector2 _lastViewportMousePosition;
	private bool _hasLastViewportMousePosition;
	private bool _isDebugModeEnabled;
	private bool _isInteractionAreaDebugEnabled;
	private bool _isMapLayoutDebugEnabled;
	private string _activeInterfaceName = "none";

	public override void _Ready()
	{
		_panel = GetNode<Control>(PanelPath);
		_preview = GetNode<TextureRect>(PreviewPath);
		_title = GetNode<Label>(TitlePath);
		_help = GetNode<Label>(HelpPath);

		CreateInteractionAreaDebugOverlay();
		_panel.Visible = false;
		UpdateText();
	}

	public override void _Process(double delta)
	{
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

		if (!_isDebugModeEnabled)
		{
			return;
		}

		if (IsActiveViewportTextInputFocused() && keyEvent.Keycode is not Key.F3 and not Key.F4 and not Key.F5 and not Key.Escape)
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
			SetMapLayoutDebugEnabled(false);
			Input.MouseMode = Input.MouseModeEnum.Captured;
			return;
		}

		Input.MouseMode = Input.MouseModeEnum.Visible;
		UpdateText();
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
		_help.Text = "F3: debug on/off | F4: interaction areas | F5: map layout | 1: PC | 2: MAP | 3: DOSSIER | 4: NOTEBOOK | Esc: close";
	}
}
