using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Inventory;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

internal static class LegacyInventoryPreflight
{
    public static InventoryImportPreflight Check(LegacyInventorySnapshot snapshot)
    {
        var issues = new List<InventoryImportIssue>();
        void Add(string code, Guid? id, string detail) => issues.Add(new(code, id, detail));
        void Origin(Guid? warehouse, Guid id)
        {
            if (warehouse.HasValue && warehouse != LegacyDefaultWarehouse.Id)
                Add("UnsupportedWarehouseOrigin", id, "Product authority cannot attribute stock to a non-default warehouse.");
        }

        if (!snapshot.Warehouses.Any(x => x.Id == LegacyDefaultWarehouse.Id && x.Active))
            Add("DefaultWarehouseUnavailable", null, "The fixed legacy default warehouse must exist and be active.");
        if (snapshot.HasWarehouseActivity)
            Add("WarehouseActivityExists", null, "Preparation must precede any warehouse inventory activation or import.");

        var products = snapshot.Products.ToDictionary(x => x.Id);
        var orders = snapshot.Orders.ToDictionary(x => x.Id);
        var jobcards = snapshot.Jobcards.ToDictionary(x => x.Id);
        var delivered = snapshot.Deliveries.Select(x => x.OrderId).ToHashSet();
        var activeSales = snapshot.Reservations.Where(x => !x.Closed).ToArray();
        var salesTotals = activeSales.GroupBy(x => x.ProductId).ToDictionary(x => x.Key, x => x.Sum(r => (long)r.Quantity));
        var activeParts = snapshot.JobcardLines.Where(x => jobcards.TryGetValue(x.JobcardId, out var j) && j.Status is 0 or 1 or 2).ToArray();
        var partTotals = activeParts.GroupBy(x => x.ProductId).ToDictionary(x => x.Key, x => x.Sum(r => (long)r.Quantity));

        foreach (var order in snapshot.Orders)
        {
            if (order.Status is < 0 or > 3)
                Add("UnknownOrderStatus", order.Id, "The source order status is not supported.");
            if (delivered.Contains(order.Id) && order.Status != 2)
                Add("DeliveryStateMismatch", order.Id, "An existing delivery must belong to an Approved order.");
        }
        foreach (var line in snapshot.SalesLines)
        {
            Origin(line.WarehouseId, line.Id);
            if (line.Quantity <= 0 || !products.ContainsKey(line.ProductId) || !orders.ContainsKey(line.OrderId))
                Add("InvalidSalesLine", line.Id, "The source line has invalid quantity or ownership.");
            if (orders.TryGetValue(line.OrderId, out var order) && order.Status is 1 or 2 && !delivered.Contains(order.Id))
            {
                var matches = activeSales.Where(x => x.OrderId == line.OrderId && x.ProductId == line.ProductId).ToArray();
                if (matches.Length != 1 || matches[0].Quantity != line.Quantity ||
                    (matches[0].WarehouseId ?? LegacyDefaultWarehouse.Id) != (line.WarehouseId ?? LegacyDefaultWarehouse.Id))
                    Add("MissingOrMismatchedSalesReservation", line.Id, "Each pending/approved undispatched line needs one exact active reservation.");
            }
        }
        foreach (var reservation in snapshot.Reservations)
        {
            Origin(reservation.WarehouseId, reservation.Id);
            if (reservation.Quantity <= 0 || !products.ContainsKey(reservation.ProductId) || !orders.ContainsKey(reservation.OrderId))
                Add("InvalidSalesReservation", reservation.Id, "The reservation has invalid quantity or ownership.");
            if (!reservation.Closed)
            {
                if (reservation.Historical || reservation.ClosedAt.HasValue || reservation.ClosureKind.HasValue)
                    Add("InvalidActiveReservation", reservation.Id, "An active reservation cannot be historical or carry closure data.");
                if (!orders.TryGetValue(reservation.OrderId, out var order) || order.Status is not (1 or 2) || delivered.Contains(reservation.OrderId))
                    Add("ActiveReservationOwnerState", reservation.Id, "An active Sales reservation requires an undispatched pending/approved owner.");
                var line = snapshot.SalesLines.SingleOrDefault(x => x.OrderId == reservation.OrderId && x.ProductId == reservation.ProductId);
                if (line is null || line.Quantity != reservation.Quantity ||
                    (line.WarehouseId ?? LegacyDefaultWarehouse.Id) != (reservation.WarehouseId ?? LegacyDefaultWarehouse.Id))
                    Add("OrphanActiveReservation", reservation.Id, "An active reservation must match its current line and origin.");
            }
            else if (reservation.ClosureKind.HasValue && reservation.ClosureKind is not (1 or 2))
                Add("UnknownClosureKind", reservation.Id, "Explicit closure data is invalid; legacy null closure remains unknown.");
        }
        foreach (var jobcard in snapshot.Jobcards)
            if (jobcard.Status is < 0 or > 4)
                Add("UnknownJobcardStatus", jobcard.Id, "The source Jobcard status is not supported.");
        foreach (var part in snapshot.JobcardLines)
        {
            Origin(part.WarehouseId, part.Id);
            if (part.Quantity <= 0 || !products.ContainsKey(part.ProductId) || !jobcards.ContainsKey(part.JobcardId))
                Add("InvalidJobcardPart", part.Id, "The part has invalid quantity or ownership.");
        }
        var balances = new List<InventoryImportProduct>();
        foreach (var product in snapshot.Products)
        {
            var sales = salesTotals.GetValueOrDefault(product.Id);
            var service = partTotals.GetValueOrDefault(product.Id);
            balances.Add(new(product.Id, product.OnHand, product.Reserved, sales, service));
            if (product.OnHand < product.Reserved || product.Reserved < 0 || product.Version.Length != 8)
                Add("InvalidProductBalance", product.Id, "Legacy balances or rowversion violate the import bounds.");
            if (product.Reserved != checked(sales + service))
                Add("ReservationTotalMismatch", product.Id, "Product.ReservedQuantity must equal active Sales plus active Jobcard quantities.");
        }
        return new(issues.Count == 0, Convert.ToHexString(snapshot.Fingerprint()), issues, balances);
    }
}
