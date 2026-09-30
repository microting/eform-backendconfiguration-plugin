/*
The MIT License (MIT)
Copyright (c) 2007 - 2026 Microting A/S
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Services.ChemicalInventoryService;

using System;
using Infrastructure.Models.Chemicals;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

/// <summary>Stock amount rules. Amounts are decimal(18,3); the wire carries thousandths.</summary>
public static class ChemicalQuantity
{
    /// <summary>One million L/kg: anything larger is a unit mistake, not stock.</summary>
    public const decimal MaxAmount = 1_000_000m;

    /// <summary>The positive amount a Received/Consumed/initial entry moves.</summary>
    public static decimal ResolveMovedAmount(ChemicalStockAmountModel amount)
    {
        if (amount == null)
        {
            throw new ArgumentException("An amount is required.");
        }

        RequireUnit(amount.Unit);
        if (amount.ContainerSize is { } size)
        {
            RequireQuantity(size, "container size", allowZero: false);
        }

        if (amount.ContainerCount is <= 0)
        {
            throw new ArgumentException("The container count must be greater than zero.");
        }

        var value = amount.Amount
                    ?? (amount.ContainerSize is { } containerSize && amount.ContainerCount is { } count
                        ? containerSize * count
                        : throw new ArgumentException("Give an amount, or a container size and a container count."));
        RequireQuantity(value, "amount", allowZero: false);
        return value;
    }

    /// <summary>The counted balance an Adjusted entry sets (zero allowed: the container is empty).</summary>
    public static decimal ResolveCountedBalance(ChemicalStockAmountModel amount)
    {
        if (amount?.Amount is not { } counted)
        {
            throw new ArgumentException("An adjustment needs the counted amount.");
        }

        RequireUnit(amount.Unit);
        RequireQuantity(counted, "counted amount", allowZero: true);
        return counted;
    }

    public static decimal RequireMoveAmount(decimal amount)
    {
        RequireQuantity(amount, "amount to move", allowZero: false);
        return amount;
    }

    public static void RequireUnit(ChemicalStockUnitEnum unit)
    {
        if (!System.Enum.IsDefined(unit))
        {
            throw new ArgumentException("A unit (L or kg) is required.");
        }
    }

    public static long ToMilli(decimal value) =>
        (long)decimal.Round(value * 1000m, 0, MidpointRounding.AwayFromZero);

    public static decimal FromMilli(long milli) => milli / 1000m;

    private static void RequireQuantity(decimal value, string what, bool allowZero)
    {
        if (value < 0 || (!allowZero && value == 0))
        {
            throw new ArgumentException(allowZero
                ? $"The {what} cannot be negative."
                : $"The {what} must be greater than zero.");
        }

        if (value > MaxAmount)
        {
            throw new ArgumentException($"The {what} is larger than {MaxAmount}.");
        }

        if (decimal.Round(value, 3) != value)
        {
            throw new ArgumentException($"The {what} has more than three decimals.");
        }
    }
}
