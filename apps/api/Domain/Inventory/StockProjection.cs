using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Domain.Inventory;

public static class StockProjection
{
    public static long FullDaysRemaining(ExactQuantity balance, ExactQuantity dailyConsumption)
    {
        if (balance.Numerator <= 0 || dailyConsumption.Numerator <= 0)
        {
            return 0;
        }

        return checked(balance.Numerator * dailyConsumption.Denominator) /
               checked(balance.Denominator * dailyConsumption.Numerator);
    }

    public static DateOnly? DepletionDate(DateOnly from, ExactQuantity balance, ExactQuantity dailyConsumption)
    {
        var days = FullDaysRemaining(balance, dailyConsumption);
        return days == 0 ? null : from.AddDays(checked((int)days));
    }
}
