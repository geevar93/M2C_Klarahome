using KlaraHome.Contracts.Orders;
using KlaraHome.Modules.Returns.Domain;

namespace KlaraHome.Modules.Returns.Infrastructure.Refunds;

/// <summary>What a return is worth, broken down the way a credit note needs it.</summary>
/// <param name="TaxableValue">What the tax was computed on.</param>
/// <param name="Cgst">Central GST being credited.</param>
/// <param name="Sgst">State GST being credited.</param>
/// <param name="Igst">Integrated GST being credited.</param>
/// <param name="Cess">Compensation cess being credited.</param>
/// <param name="GoodsTotal">What the goods are worth back, inclusive of tax.</param>
/// <param name="ShippingRefund">The original delivery charge going back, inclusive of its tax.</param>
/// <param name="ReturnShippingFee">What the shopper is charged for the reverse pickup.</param>
/// <param name="Payable">What actually goes back to the shopper, after both freight adjustments.</param>
internal readonly record struct RefundBreakdown(
    decimal TaxableValue,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal Cess,
    decimal GoodsTotal,
    decimal ShippingRefund,
    decimal ReturnShippingFee,
    decimal Payable)
{
    /// <summary>Every tax head together.</summary>
    public decimal TaxAmount => Cgst + Sgst + Igst + Cess;
}

/// <summary>
/// What a return is worth, and how its tax splits.
/// </summary>
/// <remarks>
/// <para>
/// The one calculation in this module, and it is deliberately arithmetic on frozen numbers rather
/// than a call to the pricing engine. A refund must credit the tax that was <em>charged</em>: the
/// GST rate on a category can change between a sale and a return, and a recomputed figure would put
/// a credit note out of agreement with the invoice it credits — which is exactly the mismatch a GST
/// return finds.
/// </para>
/// <para>
/// Everything is apportioned by unit rather than divided at the end. Three of five units is three
/// fifths of the line's tax computed once, not a fifth computed three times, because the second
/// shape accumulates a rounding error per unit and a credit note that is off by four paise is a
/// credit note somebody has to explain.
/// </para>
/// <para>
/// Rounding is to four decimal places away from zero, matching <c>numeric(18,4)</c> and the rest of
/// the platform's money. It rounds in the shopper's favour where it rounds at all — a fraction of a
/// paisa is not worth a policy, and the direction should be the one nobody has to defend.
/// </para>
/// </remarks>
internal static class ReturnRefundCalculator
{
    /// <summary>How money is rounded everywhere in this module.</summary>
    private const int Scale = 4;

    /// <summary>
    /// What a set of units is worth back, before any freight adjustment.
    /// </summary>
    /// <remarks>
    /// Per line, and it apportions from the whole line rather than from a stored unit figure. The
    /// order line holds the tax for the quantity that was bought; a unit figure would already have
    /// been rounded once, and rounding a rounded number is how a five-unit return stops adding up to
    /// the line it came from.
    /// </remarks>
    /// <param name="line">The order line, with the tax that was charged.</param>
    /// <param name="quantity">How many units are coming back.</param>
    public static RefundBreakdown ForUnits(ReturnableLine line, int quantity)
    {
        ArgumentNullException.ThrowIfNull(line);

        var units = Math.Clamp(quantity, 0, Math.Max(line.Quantity, 0));

        if (units == 0 || line.Quantity <= 0)
        {
            return default;
        }

        // The whole line comes back: hand the frozen figures over untouched. Apportioning by
        // quantity/quantity would give the same answer in exact arithmetic and a different one after
        // four roundings, and the frozen numbers are the ones on the invoice.
        if (units >= line.Quantity)
        {
            return new RefundBreakdown(
                line.TaxableValue,
                line.Cgst,
                line.Sgst,
                line.Igst,
                line.Cess,
                line.LineTotal,
                ShippingRefund: 0m,
                ReturnShippingFee: 0m,
                Payable: line.LineTotal);
        }

        var share = (decimal)units / line.Quantity;

        var taxable = Round(line.TaxableValue * share);
        var cgst = Round(line.Cgst * share);
        var sgst = Round(line.Sgst * share);
        var igst = Round(line.Igst * share);
        var cess = Round(line.Cess * share);
        var total = Round(line.LineTotal * share);

        return new RefundBreakdown(taxable, cgst, sgst, igst, cess, total, 0m, 0m, total);
    }

    /// <summary>
    /// What a whole return is worth, freight included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two freight decisions are separate and pull in opposite directions. The <b>original
    /// delivery charge</b> goes back only when the entire seller's part is coming back and the store
    /// says so: a shopper returning one of three items has still had the parcel delivered, and
    /// refunding the freight on it would refund a service that was performed. The <b>reverse pickup
    /// fee</b> is deducted whenever the shopper is the one paying it.
    /// </para>
    /// <para>
    /// The fee is clamped so a refund can never go below zero. A reverse pickup that cost more than
    /// the goods is a commercial problem, and turning it into a debt the shopper owes would be a new
    /// kind of problem.
    /// </para>
    /// </remarks>
    /// <param name="lines">What is coming back, per line, already apportioned.</param>
    /// <param name="order">The seller's part, for its delivery charge.</param>
    /// <param name="isFullReturn">Whether nothing of the seller's part is being kept.</param>
    /// <param name="refundShipping">Whether the store refunds delivery on a full return.</param>
    /// <param name="returnShippingFee">What the shopper is charged for the collection.</param>
    public static RefundBreakdown Combine(
        IReadOnlyCollection<RefundBreakdown> lines,
        SubOrderReturnView order,
        bool isFullReturn,
        bool refundShipping,
        decimal returnShippingFee)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(order);

        var taxable = 0m;
        var cgst = 0m;
        var sgst = 0m;
        var igst = 0m;
        var cess = 0m;
        var goods = 0m;

        foreach (var line in lines)
        {
            taxable += line.TaxableValue;
            cgst += line.Cgst;
            sgst += line.Sgst;
            igst += line.Igst;
            cess += line.Cess;
            goods += line.GoodsTotal;
        }

        var shipping = isFullReturn && refundShipping ? Math.Max(0m, order.ShippingTotal) : 0m;

        if (shipping > 0m)
        {
            // Delivery is a supply too, and its tax follows the goods' place of supply. Splitting it
            // the same way keeps the credit note's two halves consistent — a note crediting IGST on
            // goods and CGST on freight would not add up to anything a return could accept.
            var freightTax = Math.Min(Math.Max(0m, order.ShippingTax), shipping);

            taxable += Round(shipping - freightTax);

            if (order.IsIntraState)
            {
                var half = Round(freightTax / 2m);
                cgst += half;
                sgst += Round(freightTax - half);
            }
            else
            {
                igst += freightTax;
            }
        }

        var fee = Math.Max(0m, returnShippingFee);
        var payable = Math.Max(0m, Round(goods + shipping - fee));

        return new RefundBreakdown(
            Round(taxable),
            Round(cgst),
            Round(sgst),
            Round(igst),
            Round(cess),
            Round(goods),
            shipping,
            fee,
            payable);
    }

    /// <summary>What was actually accepted at quality control is worth this much.</summary>
    /// <remarks>
    /// Built from the return's own lines rather than recalculated from the order, because the
    /// per-line figures were settled when the shopper asked and must not move afterwards. Only the
    /// quantity changed, and <see cref="ReturnLine.AcceptedRefund"/> is where that apportioning
    /// lives.
    /// </remarks>
    /// <param name="request">The return, after quality control has recorded its verdict.</param>
    /// <param name="isIntraState">
    /// Whether the original supply was CGST + SGST rather than IGST, which is how the freight
    /// credit splits. Taken from the order rather than stored, because it is a fact about the sale.
    /// </param>
    /// <param name="freightTax">The tax inside the delivery charge being credited.</param>
    public static RefundBreakdown ForAccepted(ReturnRequest request, bool isIntraState, decimal freightTax)
    {
        ArgumentNullException.ThrowIfNull(request);

        var taxable = 0m;
        var cgst = 0m;
        var sgst = 0m;
        var igst = 0m;
        var cess = 0m;
        var goods = 0m;

        foreach (var line in request.Lines)
        {
            if (line.QuantityAccepted <= 0)
            {
                continue;
            }

            var share = line.QuantityAccepted >= line.Quantity
                ? 1m
                : (decimal)line.QuantityAccepted / line.Quantity;

            taxable += line.AcceptedTaxableValue;
            cgst += Round(line.Cgst * share);
            sgst += Round(line.Sgst * share);
            igst += Round(line.Igst * share);
            cess += Round(line.Cess * share);
            goods += line.AcceptedRefund;
        }

        // The freight decided at approval, credited only if anything at all passed. A return where
        // every unit failed inspection is not a return of the parcel, and crediting the delivery on
        // it would refund a service that was performed for goods the shopper still has.
        var shipping = goods > 0m ? Math.Max(0m, request.ShippingRefundAmount) : 0m;

        if (shipping > 0m)
        {
            var tax = Math.Min(Math.Max(0m, freightTax), shipping);

            taxable += Round(shipping - tax);

            if (isIntraState)
            {
                var half = Round(tax / 2m);
                cgst += half;
                sgst += Round(tax - half);
            }
            else
            {
                igst += tax;
            }
        }

        var payable = Math.Max(0m, Round(goods + shipping - request.ReturnShippingFee));

        return new RefundBreakdown(
            Round(taxable),
            Round(cgst),
            Round(sgst),
            Round(igst),
            Round(cess),
            Round(goods),
            shipping,
            request.ReturnShippingFee,
            payable);
    }

    /// <summary>Rounds money the way every other amount in this platform is rounded.</summary>
    private static decimal Round(decimal value)
        => Math.Round(value, Scale, MidpointRounding.AwayFromZero);
}
