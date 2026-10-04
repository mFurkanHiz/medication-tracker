namespace MedicationTracker.Api.Modules.Catalog;

/// <summary>
/// The one wire shape for a medicine's caution notes.
/// </summary>
/// <remarks>
/// <para>
/// Three readers hand these to a client — the workspace, the Today list and the export —
/// and they must agree. A client that learns the shape from one and meets a different one
/// in another will render a blank warning block somewhere, which is worse than showing
/// nothing: it teaches people that the block is noise and then one day it is not.
/// </para>
/// <para>
/// <c>null</c> when the household has recorded nothing, rather than five nulls, so
/// "does this medicine carry a warning" is a single check. The screens need that answer
/// to decide whether to show the block at all.
/// </para>
/// </remarks>
internal static class CautionView
{
    public static object? Of(MedicationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.Cautions.IsEmpty
            ? null
            : new
            {
                doNotTakeWith = definition.CautionDoNotTakeWith,
                foodsToAvoid = definition.CautionFoodsToAvoid,
                thingsToDo = definition.CautionThingsToDo,
                thingsToAvoid = definition.CautionThingsToAvoid,
                warning = definition.CautionWarning,
            };
    }
}
