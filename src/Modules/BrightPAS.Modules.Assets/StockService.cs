using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Assets;

public sealed record StockItemEdit(Guid? Id, string Code, string Name, string Unit, bool IsImplant, Guid? SupplierId, string? SupplierCode, int ReorderLevel, int ReorderQuantity,
    decimal UnitCost, string? AccountCode, bool IsActive);
public sealed record StockRow(Guid Id, string Code, string Name, string Unit, bool IsImplant, Guid? SupplierId, string? Supplier, string? SupplierCode, int ReorderLevel,
    int ReorderQuantity, decimal UnitCost, string? AccountCode, bool IsActive, int OnHand, int OnOrder, DateOnly? NextExpiry, bool BelowReorder);
public sealed record UseRequest(Guid StockItemId, int Quantity, Guid? LotId, Guid? BookingId, Guid? PatientId, string? Reason);
public sealed record ImplantRequest(Guid StockItemId, Guid? LotId, string? Lot, string? SerialNumber, string? Manufacturer, string? Site, Guid PatientId, Guid? BookingId, DateOnly? ImplantedOn);
public sealed record ReceiptLine(Guid LineId, int Quantity, string Lot, DateOnly? Expiry);
public sealed record InvoiceRequest(string InvoiceNumber, DateOnly InvoiceDate, DateOnly DueDate, decimal AmountExGst);
public sealed record OrderLineEdit(Guid StockItemId, int Quantity, decimal? UnitCost);
public sealed record OrderRow(Guid Id, string Number, Guid SupplierId, string Supplier, string Status, string CreatedBy, DateTimeOffset CreatedAt, string? ApprovedBy, DateTimeOffset? ApprovedAt,
    decimal TotalExGst, int Lines, string? Notes, IReadOnlyList<OrderLineRow> Items, IReadOnlyList<SupplierInvoice> Invoices);
public sealed record OrderLineRow(Guid Id, Guid StockItemId, string Code, string Name, int Quantity, decimal UnitCost, int QuantityReceived);

/// <summary>
/// Consumables and implants (stock levels, reorder points, lot/batch and expiry, use on a case, patient implant records for recalls) and purchasing:
/// reorder alerts create draft orders per supplier for approval; goods receipting records lot and expiry; supplier invoices are matched to the order
/// and exported to Xero as bills.
/// </summary>
public sealed class StockService(AssetsDbContext db, EquipmentService equipment, AuditWriter audit, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    /// <summary>Invoice matching tolerance: within $1 or 1 % of the received value.</summary>
    public const decimal MatchToleranceDollars = 1m, MatchTolerancePercent = 0.01m;

    public async Task<IReadOnlyList<StockRow>> ItemsAsync(string? q, bool lowOnly, CancellationToken ct = default)
    {
        var items = await db.StockItems.AsNoTracking().OrderBy(i => i.Name).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(q)) { var t = q.Trim(); items = items.Where(i => i.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || i.Code.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList(); }
        var lots = await db.Lots.AsNoTracking().Where(l => l.OnHand > 0).ToListAsync(ct);
        var onOrder = await (from l in db.OrderLines.AsNoTracking() join o in db.Orders.AsNoTracking() on l.PurchaseOrderId equals o.Id
                             where o.Status == PoStatus.Draft || o.Status == PoStatus.Approved || o.Status == PoStatus.Sent || o.Status == PoStatus.PartReceived
                             select new { l.StockItemId, Open = l.Quantity - l.QuantityReceived }).ToListAsync(ct);
        var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var rows = items.Select(i =>
        {
            var mine = lots.Where(l => l.StockItemId == i.Id).ToList();
            var onHand = mine.Sum(l => l.OnHand);
            var ordered = onOrder.Where(o => o.StockItemId == i.Id).Sum(o => o.Open);
            return new StockRow(i.Id, i.Code, i.Name, i.Unit, i.IsImplant, i.SupplierId, i.SupplierId is { } s ? suppliers.GetValueOrDefault(s) : null, i.SupplierCode, i.ReorderLevel,
                i.ReorderQuantity, i.UnitCost, i.AccountCode, i.IsActive, onHand, ordered, mine.Where(l => l.Expiry != null).Select(l => l.Expiry).Min(), i.IsActive && onHand + ordered <= i.ReorderLevel && i.ReorderLevel > 0);
        });
        return (lowOnly ? rows.Where(r => r.BelowReorder) : rows).ToList();
    }

    public async Task<(StockItem? Item, IReadOnlyList<string> Errors)> SaveItemAsync(StockItemEdit e, string user, CancellationToken ct = default)
    {
        var code = EquipmentService.Clip(e.Code, 40)?.ToUpperInvariant();
        var name = EquipmentService.Clip(e.Name, 200);
        var errors = new List<string>();
        if (code is null) errors.Add("Enter the stock code.");
        if (name is null) errors.Add("Enter the item's name.");
        if (e.ReorderLevel < 0 || e.ReorderQuantity < 0) errors.Add("Reorder level and quantity cannot be negative.");
        if (e.ReorderLevel > 0 && e.ReorderQuantity == 0) errors.Add("Enter how many to order when stock reaches the reorder level.");
        if (e.UnitCost < 0) errors.Add("The unit cost cannot be negative.");
        if (e.SupplierId is { } s && !await db.Suppliers.AnyAsync(x => x.Id == s, ct)) errors.Add("Choose a supplier from the list.");
        if (code is not null && await db.StockItems.AnyAsync(i => i.Code == code && i.Id != e.Id, ct)) errors.Add($"Stock code {code} is already used.");
        if (errors.Count > 0) return (null, errors);
        StockItem row;
        if (e.Id is { } id) row = await db.StockItems.SingleAsync(i => i.Id == id, ct); else { row = new StockItem(); db.StockItems.Add(row); }
        row.Code = code!; row.Name = name!; row.Unit = EquipmentService.Clip(e.Unit, 20) ?? "each"; row.IsImplant = e.IsImplant; row.SupplierId = e.SupplierId;
        row.SupplierCode = EquipmentService.Clip(e.SupplierCode, 60); row.ReorderLevel = e.ReorderLevel; row.ReorderQuantity = e.ReorderQuantity; row.UnitCost = e.UnitCost;
        row.AccountCode = EquipmentService.Clip(e.AccountCode, 20); row.IsActive = e.IsActive;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, e.Id is null ? "Create" : "Update", "StockItem", row.Id.ToString(), reason: $"{row.Code} {row.Name}", ct: ct);
        return (row, []);
    }

    public async Task<IReadOnlyList<StockLot>> LotsAsync(Guid itemId, CancellationToken ct = default) =>
        await db.Lots.AsNoTracking().Where(l => l.StockItemId == itemId && l.OnHand > 0).OrderBy(l => l.Expiry ?? DateOnly.MaxValue).ToListAsync(ct);

    /// <summary>Takes stock for a case or by adjustment. Without a chosen lot, the lot expiring first is used (first-expiry-first-out); expired lots are refused.</summary>
    public async Task<string?> UseAsync(UseRequest r, string user, CancellationToken ct = default)
    {
        if (r.Quantity is < 1 or > 1000) return "Enter a quantity between 1 and 1000.";
        var item = await db.StockItems.SingleOrDefaultAsync(i => i.Id == r.StockItemId, ct);
        if (item is null) return "Stock item not found.";
        if (item.IsImplant && r.PatientId is null) return "Implants are recorded against a patient – use Record implant.";
        var lots = await db.Lots.Where(l => l.StockItemId == item.Id && l.OnHand > 0 && (r.LotId == null || l.Id == r.LotId)).OrderBy(l => l.Expiry ?? DateOnly.MaxValue).ToListAsync(ct);
        if (lots.Any(l => l.Expiry < Today) && r.LotId is not null) return "That lot has expired – do not use it. Record it as expired stock instead.";
        lots = lots.Where(l => l.Expiry is null || l.Expiry >= Today).ToList();
        if (lots.Sum(l => l.OnHand) < r.Quantity) return $"Only {lots.Sum(l => l.OnHand)} {item.Unit} of {item.Name} in date and in stock.";
        var left = r.Quantity;
        foreach (var l in lots)
        {
            if (left == 0) break;
            var take = Math.Min(left, l.OnHand);
            l.OnHand -= take; left -= take;
            db.Movements.Add(new StockMovement { StockItemId = item.Id, LotId = l.Id, Quantity = -take, Kind = MovementKind.Use, BookingId = r.BookingId, PatientId = r.PatientId,
                Reason = EquipmentService.Clip(r.Reason, 300), At = clock.GetUtcNow(), By = user });
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "StockUse", "StockItem", item.Id.ToString(), r.PatientId?.ToString(), reason: $"{item.Code} x{r.Quantity} booking {r.BookingId}", ct: ct);
        return null;
    }

    /// <summary>Stocktake correction (+/-) or removing expired stock, with a reason.</summary>
    public async Task<string?> AdjustAsync(Guid lotId, int change, bool expired, string reason, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "Enter the reason (stocktake, damaged, expired…).";
        var lot = await db.Lots.SingleOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null) return "Lot not found.";
        if (change == 0 || lot.OnHand + change < 0) return $"Enter a change that leaves 0 or more on hand (now {lot.OnHand}).";
        lot.OnHand += change;
        db.Movements.Add(new StockMovement { StockItemId = lot.StockItemId, LotId = lot.Id, Quantity = change, Kind = expired ? MovementKind.Expired : MovementKind.Adjust, Reason = EquipmentService.Clip(reason, 300), At = clock.GetUtcNow(), By = user });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "StockAdjust", "StockItem", lot.StockItemId.ToString(), reason: $"lot {lot.Lot} {change:+#;-#}: {reason}", ct: ct);
        return null;
    }

    /// <summary>An implant used on a patient: takes it from stock (by lot) and writes the patient-level implant record used for recalls.</summary>
    public async Task<(ImplantRecord? Record, string? Error)> RecordImplantAsync(ImplantRequest r, string user, CancellationToken ct = default)
    {
        var item = await db.StockItems.SingleOrDefaultAsync(i => i.Id == r.StockItemId, ct);
        if (item is null || !item.IsImplant) return (null, "Choose an implant from stock.");
        StockLot? lot = null;
        if (r.LotId is { } lid) lot = await db.Lots.SingleOrDefaultAsync(l => l.Id == lid && l.StockItemId == item.Id, ct);
        else if (!string.IsNullOrWhiteSpace(r.Lot)) lot = await db.Lots.SingleOrDefaultAsync(l => l.StockItemId == item.Id && l.Lot == r.Lot.Trim(), ct);
        var lotText = lot?.Lot ?? EquipmentService.Clip(r.Lot, 60);
        if (lotText is null) return (null, "Enter the lot/batch number from the implant packaging.");
        if (lot?.Expiry is { } exp && exp < Today) return (null, $"Lot {lot.Lot} expired on {exp:d MMM yyyy} – do not implant it.");
        if (lot is not null && lot.OnHand < 1) return (null, $"Lot {lot.Lot} shows none in stock – check the lot number.");
        var rec = new ImplantRecord
        {
            PatientId = r.PatientId, BookingId = r.BookingId, StockItemId = item.Id, LotId = lot?.Id, Lot = lotText, SerialNumber = EquipmentService.Clip(r.SerialNumber, 60),
            Manufacturer = EquipmentService.Clip(r.Manufacturer, 100), Site = EquipmentService.Clip(r.Site, 100), ImplantedOn = r.ImplantedOn ?? Today, RecordedBy = user, RecordedAt = clock.GetUtcNow(),
        };
        db.Implants.Add(rec);
        if (lot is not null)
        {
            lot.OnHand--;
            db.Movements.Add(new StockMovement { StockItemId = item.Id, LotId = lot.Id, Quantity = -1, Kind = MovementKind.Use, BookingId = r.BookingId, PatientId = r.PatientId, Reason = "Implant", At = clock.GetUtcNow(), By = user });
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Implant", "Patient", r.PatientId.ToString(), r.PatientId.ToString(), reason: $"{item.Code} lot {lotText} serial {rec.SerialNumber}", ct: ct);
        return (rec, null);
    }

    public async Task<IReadOnlyList<ImplantRecord>> ImplantsAsync(Guid? patientId, Guid? stockItemId, string? lot, CancellationToken ct = default)
    {
        var q = db.Implants.AsNoTracking().AsQueryable();
        if (patientId is { } p) q = q.Where(i => i.PatientId == p);
        if (stockItemId is { } s) q = q.Where(i => i.StockItemId == s);
        if (!string.IsNullOrWhiteSpace(lot)) q = q.Where(i => i.Lot == lot.Trim());
        return await q.OrderByDescending(i => i.ImplantedOn).Take(1000).ToListAsync(ct);
    }

    // ---------------------------------------------------------------- purchasing
    /// <summary>Creates one draft order per supplier for every active item at or below its reorder level (counting what is already on order).</summary>
    public async Task<IReadOnlyList<string>> DraftReordersAsync(string user, CancellationToken ct = default)
    {
        var low = (await ItemsAsync(null, true, ct)).Where(i => i.SupplierId is not null).ToList();
        var created = new List<string>();
        var s = await equipment.SettingsAsync(ct);
        foreach (var g in low.GroupBy(i => i.SupplierId!.Value))
        {
            var po = new PurchaseOrder { Number = $"PO{Today:yy}-{s.NextPo:0000}", SupplierId = g.Key, CreatedBy = user, CreatedAt = clock.GetUtcNow(), Notes = "Created from reorder alerts." };
            s.NextPo++;
            po.Lines = g.Select(i => new PurchaseOrderLine { PurchaseOrderId = po.Id, StockItemId = i.Id, Quantity = Math.Max(i.ReorderQuantity, i.ReorderLevel - i.OnHand - i.OnOrder + 1), UnitCost = i.UnitCost }).ToList();
            db.Orders.Add(po);
            created.Add($"{po.Number} ({g.First().Supplier}, {po.Lines.Count} item(s))");
        }
        await db.SaveChangesAsync(ct);
        if (created.Count > 0) await audit.WriteAsync(user, "Create", "PurchaseOrder", "reorder", reason: string.Join("; ", created), ct: ct);
        return created;
    }

    public async Task<(PurchaseOrder? Order, IReadOnlyList<string> Errors)> CreateOrderAsync(Guid supplierId, IReadOnlyList<OrderLineEdit> lines, string? notes, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!await db.Suppliers.AnyAsync(x => x.Id == supplierId && x.IsActive, ct)) errors.Add("Choose an active supplier.");
        var good = lines.Where(l => l.Quantity > 0).ToList();
        if (good.Count == 0) errors.Add("Add at least one item with a quantity.");
        var items = await db.StockItems.AsNoTracking().Where(i => good.Select(l => l.StockItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        if (good.Any(l => !items.ContainsKey(l.StockItemId))) errors.Add("An item on the order is not in stock items.");
        if (good.Any(l => l.Quantity > 10000 || l.UnitCost < 0)) errors.Add("Check the quantities and prices.");
        if (errors.Count > 0) return (null, errors);
        var s = await equipment.SettingsAsync(ct);
        var po = new PurchaseOrder { Number = $"PO{Today:yy}-{s.NextPo:0000}", SupplierId = supplierId, CreatedBy = user, CreatedAt = clock.GetUtcNow(), Notes = EquipmentService.Clip(notes, 500) };
        s.NextPo++;
        po.Lines = good.Select(l => new PurchaseOrderLine { PurchaseOrderId = po.Id, StockItemId = l.StockItemId, Quantity = l.Quantity, UnitCost = l.UnitCost ?? items[l.StockItemId].UnitCost }).ToList();
        db.Orders.Add(po);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "PurchaseOrder", po.Id.ToString(), reason: $"{po.Number}, {po.Lines.Count} lines", ct: ct);
        return (po, []);
    }

    public async Task<IReadOnlyList<OrderRow>> OrdersAsync(bool openOnly, CancellationToken ct = default)
    {
        var orders = await db.Orders.AsNoTracking().Include(o => o.Lines).Where(o => !openOnly || (o.Status != PoStatus.Invoiced && o.Status != PoStatus.Cancelled))
            .OrderByDescending(o => o.CreatedAt).Take(300).ToListAsync(ct);
        var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var items = await db.StockItems.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var ids = orders.Select(o => o.Id).ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => ids.Contains(i.PurchaseOrderId)).ToListAsync(ct);
        return orders.Select(o => new OrderRow(o.Id, o.Number, o.SupplierId, suppliers.GetValueOrDefault(o.SupplierId) ?? "?", o.Status.ToString(), o.CreatedBy, o.CreatedAt, o.ApprovedBy, o.ApprovedAt,
            o.Lines.Sum(l => l.Quantity * l.UnitCost), o.Lines.Count, o.Notes,
            o.Lines.Select(l => new OrderLineRow(l.Id, l.StockItemId, items[l.StockItemId].Code, items[l.StockItemId].Name, l.Quantity, l.UnitCost, l.QuantityReceived)).ToList(),
            invoices.Where(i => i.PurchaseOrderId == o.Id).ToList())).ToList();
    }

    /// <summary>Approve (purchasing approver; not the person who drafted it when someone else is available is good practice, but allowed), mark sent, or cancel.</summary>
    public async Task<string?> SetOrderStatusAsync(Guid id, PoStatus to, string user, CancellationToken ct = default)
    {
        var o = await db.Orders.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return "Order not found.";
        var ok = (o.Status, to) switch
        {
            (PoStatus.Draft, PoStatus.Approved) => true, (PoStatus.Approved, PoStatus.Sent) => true,
            (PoStatus.Draft or PoStatus.Approved or PoStatus.Sent, PoStatus.Cancelled) => true, _ => false,
        };
        if (!ok) return $"An order that is {o.Status.ToString().ToLowerInvariant()} cannot become {to.ToString().ToLowerInvariant()}.";
        if (to == PoStatus.Approved) { o.ApprovedBy = user; o.ApprovedAt = clock.GetUtcNow(); }
        o.Status = to;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, to.ToString(), "PurchaseOrder", o.Id.ToString(), reason: o.Number, ct: ct);
        return null;
    }

    /// <summary>Goods receipt: each received line adds stock with its lot and expiry. The order becomes part- or fully received.</summary>
    public async Task<string?> ReceiveAsync(Guid id, IReadOnlyList<ReceiptLine> lines, string user, CancellationToken ct = default)
    {
        var o = await db.Orders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return "Order not found.";
        if (o.Status is not (PoStatus.Approved or PoStatus.Sent or PoStatus.PartReceived)) return "Only approved orders can be received.";
        var good = lines.Where(l => l.Quantity > 0).ToList();
        if (good.Count == 0) return "Enter the quantity received for at least one line.";
        foreach (var r in good)
        {
            var line = o.Lines.SingleOrDefault(l => l.Id == r.LineId);
            if (line is null) return "A received line is not on this order.";
            if (string.IsNullOrWhiteSpace(r.Lot)) return "Enter the lot/batch number for every line received.";
            if (line.QuantityReceived + r.Quantity > line.Quantity) return $"More received than ordered for one line ({line.QuantityReceived + r.Quantity} of {line.Quantity}). Correct the quantity or add a new order.";
            if (r.Expiry is { } exp && exp < Today) return $"Lot {r.Lot} has already expired – do not accept it.";
        }
        foreach (var r in good)
        {
            var line = o.Lines.Single(l => l.Id == r.LineId);
            var lotNo = r.Lot.Trim();
            var lot = await db.Lots.SingleOrDefaultAsync(l => l.StockItemId == line.StockItemId && l.Lot == lotNo, ct);
            if (lot is null) { lot = new StockLot { StockItemId = line.StockItemId, Lot = lotNo, Expiry = r.Expiry, ReceivedOn = Today }; db.Lots.Add(lot); }
            lot.OnHand += r.Quantity;
            line.QuantityReceived += r.Quantity;
            db.Movements.Add(new StockMovement { StockItemId = line.StockItemId, LotId = lot.Id, Quantity = r.Quantity, Kind = MovementKind.Receipt, PurchaseOrderId = o.Id, At = clock.GetUtcNow(), By = user });
        }
        o.Status = o.Lines.All(l => l.QuantityReceived >= l.Quantity) ? PoStatus.Received : PoStatus.PartReceived;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Receive", "PurchaseOrder", o.Id.ToString(), reason: $"{o.Number}: {good.Count} line(s); now {o.Status}", ct: ct);
        return null;
    }

    /// <summary>Records the supplier's invoice and matches it to what was received (within $1 or 1 %). Matched invoices can be exported to Xero.</summary>
    public async Task<(SupplierInvoice? Invoice, string? Error)> AddInvoiceAsync(Guid orderId, InvoiceRequest r, string user, CancellationToken ct = default)
    {
        var o = await db.Orders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (o is null) return (null, "Order not found.");
        if (o.Status is not (PoStatus.PartReceived or PoStatus.Received)) return (null, "Receive the goods before entering the invoice.");
        if (string.IsNullOrWhiteSpace(r.InvoiceNumber)) return (null, "Enter the supplier's invoice number.");
        if (r.DueDate < r.InvoiceDate) return (null, "The due date cannot be before the invoice date.");
        if (r.AmountExGst <= 0) return (null, "Enter the invoice total excluding GST.");
        var no = r.InvoiceNumber.Trim();
        if (await db.Invoices.AnyAsync(i => i.InvoiceNumber == no && db.Orders.Any(x => x.Id == i.PurchaseOrderId && x.SupplierId == o.SupplierId), ct)) return (null, $"Invoice {no} from this supplier is already recorded.");
        var received = o.Lines.Sum(l => l.QuantityReceived * l.UnitCost);
        var already = await db.Invoices.Where(i => i.PurchaseOrderId == o.Id).SumAsync(i => (decimal?)i.AmountExGst, ct) ?? 0;
        var expected = received - already;
        var diff = Math.Abs(r.AmountExGst - expected);
        var matched = diff <= MatchToleranceDollars || (expected > 0 && diff / expected <= MatchTolerancePercent);
        var inv = new SupplierInvoice
        {
            PurchaseOrderId = o.Id, InvoiceNumber = no, InvoiceDate = r.InvoiceDate, DueDate = r.DueDate, AmountExGst = r.AmountExGst, Matched = matched,
            MatchNote = matched ? $"Matches goods received ({expected:C2} ex GST)." : $"Does not match goods received: invoice {r.AmountExGst:C2}, received {expected:C2} ex GST. Check with the supplier before paying.",
            RecordedBy = user, RecordedAt = clock.GetUtcNow(),
        };
        db.Invoices.Add(inv);
        if (matched && o.Status == PoStatus.Received) o.Status = PoStatus.Invoiced;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Invoice", "PurchaseOrder", o.Id.ToString(), reason: $"{o.Number}: invoice {no} {r.AmountExGst:0.00} {(matched ? "matched" : "NOT matched")}", ct: ct);
        return (inv, null);
    }

    public async Task<string?> ApproveMismatchAsync(Guid invoiceId, string note, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note)) return "Explain why the difference is accepted (e.g. freight, price change agreed).";
        var inv = await db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (inv is null) return "Invoice not found.";
        if (inv.Matched) return "This invoice already matches.";
        inv.Matched = true; inv.MatchNote = EquipmentService.Clip($"Difference accepted by {user}: {note}", 500);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "AcceptDifference", "SupplierInvoice", inv.Id.ToString(), reason: note, ct: ct);
        return null;
    }

    /// <summary>
    /// Matched supplier invoices not yet sent to Xero, as a Xero "Import bills" CSV (one line per order line, NZ "15% GST on Expenses").
    /// Marks them exported. The column layout follows Xero's bill import template – confirm against TDS's Xero organisation before go-live.
    /// </summary>
    public async Task<(string Csv, int Count)> XeroBillsCsvAsync(string user, bool markExported, CancellationToken ct = default)
    {
        var invoices = await db.Invoices.Where(i => i.Matched && i.ExportedToXeroAt == null).OrderBy(i => i.InvoiceDate).ToListAsync(ct);
        var orders = await db.Orders.AsNoTracking().Include(o => o.Lines).Where(o => invoices.Select(i => i.PurchaseOrderId).Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var items = await db.StockItems.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var sb = new StringBuilder();
        sb.AppendLine("*ContactName,EmailAddress,POAddressLine1,POAddressLine2,POAddressLine3,POAddressLine4,POCity,PORegion,POPostalCode,POCountry,*InvoiceNumber,*InvoiceDate,*DueDate,InventoryItemCode,Description,*Quantity,*UnitAmount,*AccountCode,*TaxType,TrackingName1,TrackingOption1,TrackingName2,TrackingOption2,Currency");
        static string C(string? v) => v is null ? "" : v.Contains(',') || v.Contains('"') || v.Contains('\n') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        foreach (var inv in invoices)
        {
            var o = orders[inv.PurchaseOrderId];
            var s = suppliers[o.SupplierId];
            var received = o.Lines.Where(l => l.QuantityReceived > 0).ToList();
            var total = received.Sum(l => l.QuantityReceived * l.UnitCost);
            var factor = total > 0 ? inv.AmountExGst / total : 1m; // spread an accepted difference across the lines
            foreach (var l in received)
            {
                var it = items[l.StockItemId];
                sb.AppendLine(string.Join(",", C(s.XeroContactName ?? s.Name), C(s.Email), "", "", "", "", "", "", "", "", C(inv.InvoiceNumber),
                    inv.InvoiceDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), inv.DueDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), C(it.Code),
                    C($"{it.Name} ({o.Number})"), l.QuantityReceived.ToString(CultureInfo.InvariantCulture), Math.Round(l.UnitCost * factor, 4).ToString("0.####", CultureInfo.InvariantCulture),
                    C(it.AccountCode ?? "300"), "15% GST on Expenses", "", "", "", "", "NZD"));
            }
            if (markExported) inv.ExportedToXeroAt = clock.GetUtcNow();
        }
        if (markExported && invoices.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(user, "Export", "XeroBills", "", reason: $"{invoices.Count} bill(s): {string.Join(", ", invoices.Select(i => i.InvoiceNumber))}", ct: ct);
        }
        return (sb.ToString(), invoices.Count);
    }
}
