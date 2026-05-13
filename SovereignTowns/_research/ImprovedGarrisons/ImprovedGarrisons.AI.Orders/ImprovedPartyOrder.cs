using ImprovedGarrisons.AI.AITypes;

namespace ImprovedGarrisons.AI.Orders;

public abstract class ImprovedPartyOrder
{
	protected ImprovedPartyAi PartyToOrder { get; private set; }

	protected bool OrderFinished { get; private set; }

	public bool IsInitialized { get; private set; } = false;


	public virtual void InitializeOrder(ImprovedPartyAi partyToOrder)
	{
		PartyToOrder = partyToOrder;
		partyToOrder.mobileParty.MemberRoster.UpdateVersion();
		IsInitialized = true;
	}

	public abstract void ExecuteOrder();

	public abstract string GetStatusText();

	protected virtual void SetOrderToFinished()
	{
		OrderFinished = true;
	}

	public virtual bool IsOrderFinished()
	{
		return OrderFinished;
	}
}
