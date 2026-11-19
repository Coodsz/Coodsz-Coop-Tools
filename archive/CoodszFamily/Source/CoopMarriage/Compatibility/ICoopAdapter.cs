namespace CoopMarriage.Compatibility;

internal interface ICoopAdapter
{
	bool IsAvailable { get; }

	bool IsAuthoritative { get; }

	string Description { get; }
}
