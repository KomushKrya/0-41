extends CharacterBody3D

@export var move_speed: float = 3.0
@export var vertical_speed: float = 2.5
@export var mouse_sensitivity: float = 0.0025

@onready var head: Node3D = $Head

var pitch: float = 0.0


func _ready() -> void:
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion and Input.get_mouse_mode() == Input.MOUSE_MODE_CAPTURED:
		rotate_y(-event.relative.x * mouse_sensitivity)

		pitch -= event.relative.y * mouse_sensitivity
		pitch = clamp(pitch, deg_to_rad(-85.0), deg_to_rad(85.0))
		head.rotation.x = pitch

	if event.is_action_pressed("ui_cancel"):
		if Input.get_mouse_mode() == Input.MOUSE_MODE_CAPTURED:
			Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)
		else:
			Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)


func _physics_process(delta: float) -> void:
	var input_dir: Vector2 = Input.get_vector(
		"move_left",
		"move_right",
		"move_forward",
		"move_back"
	)

	var direction: Vector3 = (
		transform.basis * Vector3(input_dir.x, 0.0, input_dir.y)
	).normalized()

	var vertical_direction: float = 0.0

	if Input.is_action_pressed("fly_up"):
		vertical_direction += 1.0

	if Input.is_action_pressed("fly_down"):
		vertical_direction -= 1.0

	velocity = direction * move_speed
	velocity.y = vertical_direction * vertical_speed

	move_and_slide()
