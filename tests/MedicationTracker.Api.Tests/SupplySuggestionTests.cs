using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Domain.Refill;
using MedicationTracker.Api.Domain.Scheduling;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// The date a supply runs out at the planned use, as suggested for the refill settings.
/// </summary>
/// <remarks>
/// The owner's rule: a prescription assumes the medicine is taken as written, so for the
/// official date an as-needed plan counts as one dose a day — "resmî olarak her gün
/// kullanacağı varsayılır". The forecast proper keeps excluding as-needed plans; this is a
/// suggestion the household confirms by saving. All data is synthetic.
/// </remarks>
public sealed class SupplySuggestionTests
{
    private static readonly DateOnly From = new(2026, 10, 5);

    [Fact]
    public void Twenty_tablets_at_two_a_day_run_out_in_ten_days()
    {
        var runsOut = RefillForecast.SupplyRunsOutOn(Tablets(20), [Daily(2)], From);

        Assert.Equal(From.AddDays(10), runsOut);
    }

    [Fact]
    public void An_as_needed_plan_counts_as_one_dose_a_day()
    {
        var asNeeded = new PlannedConsumption(RecurrenceSpecification.AsNeeded(From, null), Tablets(1));

        var runsOut = RefillForecast.SupplyRunsOutOn(Tablets(20), [asNeeded], From);

        // Twenty days, not "unknowable": the official reading assumes daily use.
        Assert.Equal(From.AddDays(20), runsOut);

        // The forecast proper still refuses to guess at an as-needed plan.
        var forecast = RefillForecast.Project(Tablets(20), [asNeeded], From, RefillSettings.None);
        Assert.False(forecast.IsForecastable);
    }

    [Fact]
    public void Without_a_plan_there_is_nothing_to_compute()
    {
        Assert.Null(RefillForecast.SupplyRunsOutOn(Tablets(20), [], From));
    }

    [Fact]
    public void An_empty_supply_runs_out_today()
    {
        Assert.Equal(From, RefillForecast.SupplyRunsOutOn(ExactQuantity.Zero, [Daily(1)], From));
    }

    [Fact]
    public void A_supply_that_outlasts_the_horizon_gives_no_date()
    {
        Assert.Null(RefillForecast.SupplyRunsOutOn(Tablets(100), [Daily(1)], From, horizonDays: 30));
    }

    private static ExactQuantity Tablets(long count) => new(count, 1);

    private static PlannedConsumption Daily(long dose) =>
        new(RecurrenceSpecification.Daily(From, null), Tablets(dose));
}
