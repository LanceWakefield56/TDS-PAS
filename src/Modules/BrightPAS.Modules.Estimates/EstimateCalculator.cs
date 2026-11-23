using System.Text.Json;

namespace BrightPAS.Modules.Estimates;

/// <summary>An add-on chosen on an estimate: required (charged) or "if required" (listed under Additional costs, not totalled).</summary>
public sealed class AddOnChoice
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IfRequired { get; set; }
    public int Quantity { get; set; } = 1;
}

public sealed class ChargeLine
{
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
}

/// <summary>Estimate inputs stored as JSON on the estimate (and on templates).</summary>
public sealed class EstimateInputs
{
    public List<AddOnChoice> AddOns { get; set; } = [];
    public List<ChargeLine> OtherCharges { get; set; } = [];
    public ChargeLine Adjustment { get; set; } = new();
    public List<ChargeLine> IfRequired { get; set; } = [];

    public static EstimateInputs Read(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<EstimateInputs>(json, JsonSerializerOptions.Web) ?? new();

    /// <summary>Stores only meaningful entries (blank rows from the form are dropped).</summary>
    public string Write() => JsonSerializer.Serialize(new EstimateInputs
    {
        AddOns = [.. AddOns.Where(a => !string.IsNullOrWhiteSpace(a.Code)).Select(a => new AddOnChoice { Code = a.Code.Trim(), Name = a.Name, IfRequired = a.IfRequired, Quantity = Math.Clamp(a.Quantity, 1, 99) })],
        OtherCharges = [.. OtherCharges.Where(o => (o.Amount ?? 0) != 0 || !string.IsNullOrWhiteSpace(o.Description))],
        Adjustment = Adjustment,
        IfRequired = [.. IfRequired.Where(o => (o.Amount ?? 0) != 0 || !string.IsNullOrWhiteSpace(o.Description))],
    }, JsonSerializerOptions.Web);
}

public sealed record CalcLine(string Label, decimal? Amount, string? Display = null, string? Note = null);

public sealed class CalcResult
{
    public string Title { get; set; } = "";
    public List<CalcLine> Lines { get; } = [];
    public List<CalcLine> Additional { get; } = [];
    public decimal Facility { get; set; }
    public decimal Surgeon { get; set; }
    public decimal Anaesthetist { get; set; }
    public decimal Consumables { get; set; }
    public decimal ExtendedTime { get; set; }
    public decimal AllInAddOns { get; set; }
    public decimal Other { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Deposit { get; set; }
    public decimal Balance { get; set; }
    public int ScheduledMinutes { get; set; }
    public int PlannedMinutes { get; set; }
    public int ExtraMinutes { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public DateOnly? DepositDue { get; set; }
    public List<string> Warnings { get; } = [];
    // Internal costing (admins only, never printed)
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public decimal Margin { get; set; }
    public bool CostingComplete { get; set; }
}

/// <summary>Everything the calculator needs about the chosen procedure, loaded once by the caller.</summary>
public sealed class CalcContext
{
    public EstProcedure? Procedure { get; set; }
    public List<EstSurgeonFee> Fees { get; set; } = [];
    public List<EstAddOn> AddOns { get; set; } = [];
    /// <summary>Internal costing lines with their unit cost (stock item price or manual cost, ex GST; null = unknown).</summary>
    public List<(decimal Quantity, decimal? UnitCost)> Costing { get; set; } = [];
}

/// <summary>
/// The estimate calculation, unchanged from TDS Estimates v1.5 (business rules in ADR 0034). Pure: no database or clock, so the same
/// numbers come out in the screens, the PDF, the tests and the self-test scenarios.
/// </summary>
public static class EstimateCalculator
{
    public static decimal? FeeFor(CalcContext ctx, Guid? surgeonId, bool special)
    {
        var row = (surgeonId is not null ? ctx.Fees.Find(f => f.SurgeonId == surgeonId) : null) ?? ctx.Fees.Find(f => f.SurgeonId is null);
        if (row is null) return null;
        if (special && (row.SpecialFee ?? 0) > 0) return row.SpecialFee;
        return row.Fee;
    }

    /// <summary>Add-ons that can be chosen: same specialty, same group (or whole specialty), this surgeon (or any) and not already included.</summary>
    public static List<EstAddOn> AvailableAddOns(CalcContext ctx, Guid? surgeonId)
    {
        var p = ctx.Procedure;
        if (p is null) return [];
        var inc = p.IncludedCodes().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. ctx.AddOns
            .Where(a => a.IsActive && a.SpecialtyId == p.SpecialtyId
                        && (string.IsNullOrWhiteSpace(a.Group) || string.Equals(a.Group, p.Group, StringComparison.OrdinalIgnoreCase))
                        && (a.SurgeonId is null || a.SurgeonId == surgeonId)
                        && !inc.Contains(a.Code))
            .OrderBy(a => a.Code, StringComparer.Ordinal)];
    }

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.ToEven);

    public static CalcResult Calculate(Estimate e, EstimateInputs inp, CalcContext ctx, EstSettings s)
    {
        var r = new CalcResult();
        var p = ctx.Procedure;
        var mult = e.Multiplier <= 0 ? 1m : e.Multiplier;
        decimal M(decimal v) => R(v * mult);

        if (p is null)
        {
            r.Warnings.Add("Choose a procedure.");
            return r;
        }

        // Facility
        r.Facility = (e.FacilityOverride ?? 0) > 0 ? e.FacilityOverride!.Value
            : e.UseSpecial && (p.FacilitySpecial ?? 0) > 0 ? p.FacilitySpecial!.Value
            : p.Facility ?? 0;

        // Anaesthetist (only with a general anaesthetic or sedation by an anaesthetist)
        var hasAn = Anaesthetics.HasAnaesthetist(e.Anaesthetic);
        r.Anaesthetist = !hasAn ? 0
            : (e.AnaesthetistOverride ?? 0) > 0 ? e.AnaesthetistOverride!.Value
            : e.UseSpecial && (p.AnaesthetistSpecial ?? 0) > 0 ? p.AnaesthetistSpecial!.Value
            : p.Anaesthetist ?? 0;

        // Surgeon base fee + add-ons
        var available = AvailableAddOns(ctx, e.SurgeonId);
        var surgeonBase = FeeFor(ctx, e.SurgeonId, e.UseSpecial) ?? 0;
        decimal surgeonAddOns = 0;
        var allInLines = new List<CalcLine>();
        var surgeonAddOnLines = new List<CalcLine>();
        var included = p.IncludedCodes().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sel in inp.AddOns.Where(a => !string.IsNullOrWhiteSpace(a.Code)))
        {
            var a = available.Find(x => string.Equals(x.Code, sel.Code, StringComparison.OrdinalIgnoreCase));
            if (a is null)
            {
                var name = ctx.AddOns.Find(x => string.Equals(x.Code, sel.Code, StringComparison.OrdinalIgnoreCase))?.Name ?? sel.Name;
                r.Warnings.Add(included.Contains(sel.Code)
                    ? $"Add-on ‘{name}’ is already included in this procedure’s fee – not charged."
                    : $"Add-on ‘{name}’ does not apply to this procedure/surgeon – not charged.");
                continue;
            }
            var qty = Math.Max(1, sel.Quantity);
            var amt = a.Fee * qty;
            var label = qty > 1 ? $"{a.Name} (×{qty})" : a.Name;
            if (sel.IfRequired)
                r.Additional.Add(new CalcLine(label, M(amt), null, a.Type == AddOnType.Surgeon ? "Surgeon fee – only if required" : "Only if required"));
            else if (a.Type == AddOnType.Surgeon) { surgeonAddOns += amt; surgeonAddOnLines.Add(new CalcLine("Add-on – " + label, M(amt))); }
            else { allInLines.Add(new CalcLine("Add-on – " + label, M(amt))); r.AllInAddOns += amt; }
        }
        var manualSurgeon = (e.SurgeonOverride ?? 0) > 0;
        r.Surgeon = manualSurgeon ? e.SurgeonOverride!.Value : surgeonBase + surgeonAddOns;

        // Consumables, extended time ($ per started 15 minutes over the scheduled minutes)
        r.Consumables = p.ConsumablesLine ?? 0;
        r.ScheduledMinutes = p.Minutes;
        r.PlannedMinutes = (e.PlannedMinutes ?? 0) > 0 ? e.PlannedMinutes!.Value : p.Minutes;
        r.ExtraMinutes = Math.Max(0, r.PlannedMinutes - r.ScheduledMinutes);
        r.ExtendedTime = (r.ExtraMinutes + 14) / 15 * s.ExtendedTimePer15;

        // Printed lines – itemised for insurers: surgeon base fee, then each required add-on, then the anaesthetist.
        // A manual surgeon fee replaces the schedule fee and surgeon add-ons, so those are not itemised.
        r.Lines.Add(new CalcLine("Facility Fee", M(r.Facility)));
        var surgeonLine = manualSurgeon ? r.Surgeon : surgeonBase;
        r.Lines.Add(surgeonLine == 0 ? new CalcLine(s.SurgeonFeeLabel, 0, "To be advised") : new CalcLine(s.SurgeonFeeLabel, M(surgeonLine)));
        if (!manualSurgeon) r.Lines.AddRange(surgeonAddOnLines);
        r.Lines.AddRange(allInLines);
        if (hasAn) r.Lines.Add(new CalcLine("Anaesthetist Fee", M(r.Anaesthetist)));
        if (r.Consumables > 0) r.Lines.Add(new CalcLine("Consumables & Equipment", M(r.Consumables)));
        if (r.ExtendedTime > 0) r.Lines.Add(new CalcLine($"Extended Theatre Time ({r.ExtraMinutes} min)", M(r.ExtendedTime)));
        foreach (var o in inp.OtherCharges.Where(o => (o.Amount ?? 0) != 0))
        {
            r.Lines.Add(new CalcLine(string.IsNullOrWhiteSpace(o.Description) ? "Other charge" : o.Description.Trim(), M(o.Amount!.Value)));
            r.Other += o.Amount!.Value;
        }
        if ((inp.Adjustment.Amount ?? 0) != 0)
        {
            r.Lines.Add(new CalcLine(string.IsNullOrWhiteSpace(inp.Adjustment.Description) ? "Adjustment" : inp.Adjustment.Description.Trim(), M(inp.Adjustment.Amount!.Value)));
            r.Other += inp.Adjustment.Amount!.Value;
        }
        foreach (var a in inp.IfRequired.Where(a => !string.IsNullOrWhiteSpace(a.Description) && (a.Amount ?? 0) != 0))
            r.Additional.Add(new CalcLine(a.Description!.Trim(), M(a.Amount!.Value)));

        r.Subtotal = r.Lines.Sum(l => l.Amount ?? 0);
        r.Deposit = e.Funding == Funding.SelfFunding ? R(r.Subtotal * s.DepositPercent / 100m) : 0;
        r.Balance = r.Subtotal - r.Deposit;
        r.ValidUntil = e.EstimateDate.AddMonths(s.ValidityMonths);
        r.DepositDue = e.ProcedureDate?.AddDays(-s.DepositDaysBefore);
        r.Title = $"Estimate – {p.PrintTitle} {Anaesthetics.Phrase(e.Anaesthetic)}".Trim();

        // Internal costing
        r.Revenue = M(r.Facility + r.Consumables + r.ExtendedTime);
        var consCost = ctx.Costing.Sum(c => c.Quantity * (c.UnitCost ?? 0));
        r.CostingComplete = ctx.Costing.Count > 0 && ctx.Costing.TrueForAll(c => c.UnitCost is not null);
        r.Cost = R((consCost + r.PlannedMinutes * s.StaffCostPerMinute + s.OverheadPerCase) * (1 + s.GstRate));
        r.Margin = r.Revenue - r.Cost;

        Warn(e, r, p, s);
        return r;
    }

    private static void Warn(Estimate e, CalcResult r, EstProcedure p, EstSettings s)
    {
        var w = r.Warnings;
        if (string.IsNullOrWhiteSpace(e.FirstName) || string.IsNullOrWhiteSpace(e.LastName)) w.Add("Enter the patient’s first and last name.");
        if (e.DateOfBirth is null) w.Add("Enter the patient’s date of birth.");
        if (string.IsNullOrWhiteSpace(e.Nhi)) w.Add("Enter the patient NHI.");
        else if (e.Nhi.Trim().Length != 7) w.Add("NHI should be 7 characters – please check.");
        if (string.IsNullOrWhiteSpace(e.Email)) w.Add("Enter an email address (estimate is usually emailed).");
        else if (!e.Email.Contains('@', StringComparison.Ordinal) || !e.Email.Contains('.', StringComparison.Ordinal)) w.Add("Email address looks incorrect.");
        if (string.IsNullOrWhiteSpace(e.Address) || string.IsNullOrWhiteSpace(e.Town)) w.Add("Enter the postal address.");
        if (e.Funding == Funding.Insurance && string.IsNullOrWhiteSpace(e.Insurer)) w.Add("Enter the insurer name.");
        if (e.Funding == Funding.Insurance && string.IsNullOrWhiteSpace(e.PolicyNumber)) w.Add("Enter the insurance policy / membership number.");
        if (r.Facility == 0) w.Add("No facility fee set for this procedure – enter it in Admin › Procedures or use the manual entry.");
        if (r.Surgeon == 0) w.Add("No surgeon fee for this surgeon/procedure – estimate shows ‘To be advised’. Enter it manually or add it in Admin › Surgeon fees.");
        if (Anaesthetics.HasAnaesthetist(e.Anaesthetic) && r.Anaesthetist == 0)
            w.Add("No anaesthetist fee for this procedure – if the consultant sedates, choose ‘Sedation by the consultant’; otherwise enter the fee.");
        if (!Anaesthetics.HasAnaesthetist(e.Anaesthetic) && (p.Anaesthetist ?? 0) > 0 && e.Anaesthetic != Anaesthetic.Local)
            w.Add("This procedure is priced with an anaesthetist – anaesthetist fee excluded. Check the anaesthetic choice.");
        if (e.UseSpecial && (p.FacilitySpecial ?? 0) == 0 && (p.AnaesthetistSpecial ?? 0) == 0) w.Add("Special pricing requested but none set for this procedure – standard fees used.");
        if (e.UseSpecial && e.Funding != Funding.SelfFunding) w.Add("Special package rates are normally for self-funding patients only.");
        if (e.Funding == Funding.SelfFunding && r.DepositDue is not null && r.DepositDue < e.EstimateDate)
            w.Add($"Procedure is within {s.DepositDaysBefore} days – deposit is due immediately.");
        if (e.Multiplier != 1m) w.Add($"Whole-estimate multiplier of {e.Multiplier * 100:0}% applied to every line.");
    }
}
