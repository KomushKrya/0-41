public interface IInteractable
{
	string InteractionLabel { get; }
	bool CanInteract(FlyPlayer player);
	void Interact(FlyPlayer player);
	void SetHovered(bool isHovered);
}
