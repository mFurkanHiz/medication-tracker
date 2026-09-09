using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Tests;

public sealed class StockProjectionTests
{
    [Fact]
    public void Fractional_stock_projects_only_complete_daily_doses()
    {
        var days = StockProjection.FullDaysRemaining(new ExactQuantity(7, 2), new ExactQuantity(2, 3));
        Assert.Equal(5, days);
    }

    [Fact]
    public void Depletion_date_is_unknown_without_positive_stock_or_consumption()
    {
        Assert.Null(StockProjection.DepletionDate(new DateOnly(2026, 9, 9), new ExactQuantity(3), new ExactQuantity(0)));
        Assert.Null(StockProjection.DepletionDate(new DateOnly(2026, 9, 9), new ExactQuantity(0), new ExactQuantity(1)));
    }
}
