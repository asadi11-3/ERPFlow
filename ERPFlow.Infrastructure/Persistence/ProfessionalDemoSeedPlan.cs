using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
using ERPFlow.Domain.Transfers;
using ERPFlow.Domain.Enums;

namespace ERPFlow.Infrastructure.Persistence;

internal sealed record ProfessionalDemoPurchaseOrderSeed(
    string Key, DateTimeOffset CreatedAt, int SupplierIndex, int ProductIndex, string WarehouseCode,
    int Quantity, decimal UnitCost, PurchaseOrderStatus Status);

internal sealed record ProfessionalDemoReceiptSeed(
    string Key, string PurchaseOrderKey, DateTimeOffset CreatedAt, int Quantity, GoodsReceiptStatus Status);

internal sealed record ProfessionalDemoTransferSeed(
    string Key, DateTimeOffset CreatedAt, string SourceWarehouseCode, string DestinationWarehouseCode,
    int ProductIndex, int Quantity, StockTransferStatus Status);

internal sealed record ProfessionalDemoSalesOrderSeed(
    string Key, DateTimeOffset CreatedAt, int CustomerIndex, int ProductIndex, string WarehouseCode,
    int Quantity, OrderStatus Status, bool ReturnedForCorrection, bool Dispatch);

internal sealed record ProfessionalDemoServiceSeed(
    string Key, DateTimeOffset CreatedAt, int CustomerIndex, int ProductIndex, int? AssetIndex,
    JobcardPriority Priority, JobcardStatus Status);

internal static class ProfessionalDemoSeedPlan
{
    public static readonly ProfessionalDemoPurchaseOrderSeed[] PurchaseOrders =
    [
        new("po-01", Utc(9, 8, 9), 0, 0, "AMM-CENTRAL", 7, 80m, PurchaseOrderStatus.Received),
        new("po-02", Utc(9, 10, 11), 1, 1, "AMM-SERVICE", 7, 81m, PurchaseOrderStatus.Received),
        new("po-03", Utc(9, 12, 9), 2, 2, "LEGACY-DEFAULT", 9, 82m, PurchaseOrderStatus.Received),
        new("po-04", Utc(9, 15, 9), 3, 3, "AMM-CENTRAL", 10, 83m, PurchaseOrderStatus.Received),
        new("po-05", Utc(9, 18, 9), 4, 4, "AMM-SERVICE", 12, 84m, PurchaseOrderStatus.Received),
        new("po-06", Utc(9, 21, 9), 5, 5, "LEGACY-DEFAULT", 10, 85m, PurchaseOrderStatus.PartiallyReceived),
        new("po-07", Utc(9, 24, 9), 6, 6, "AMM-CENTRAL", 10, 86m, PurchaseOrderStatus.PartiallyReceived),
        new("po-08", Utc(9, 29, 9), 7, 7, "AMM-SERVICE", 10, 87m, PurchaseOrderStatus.PartiallyReceived),
        new("po-09", Utc(10, 5, 9), 8, 8, "LEGACY-DEFAULT", 10, 88m, PurchaseOrderStatus.PartiallyReceived),
        new("po-10", Utc(10, 9, 9), 9, 9, "AMM-CENTRAL", 6, 89m, PurchaseOrderStatus.Approved),
        new("po-11", Utc(10, 12, 9), 0, 10, "AMM-SERVICE", 6, 90m, PurchaseOrderStatus.Approved),
        new("po-12", Utc(10, 15, 9), 1, 11, "LEGACY-DEFAULT", 6, 91m, PurchaseOrderStatus.Approved),
        new("po-13", Utc(10, 16, 9), 2, 12, "AMM-CENTRAL", 6, 92m, PurchaseOrderStatus.Draft),
        new("po-14", Utc(10, 17, 9), 3, 14, "AMM-SERVICE", 6, 93m, PurchaseOrderStatus.Draft),
        new("po-15", Utc(10, 18, 9), 4, 15, "LEGACY-DEFAULT", 6, 94m, PurchaseOrderStatus.PendingApproval),
        new("po-16", Utc(10, 19, 9), 5, 16, "AMM-CENTRAL", 6, 95m, PurchaseOrderStatus.PendingApproval),
        new("po-17", Utc(10, 20, 9), 6, 17, "AMM-SERVICE", 6, 96m, PurchaseOrderStatus.Rejected),
        new("po-18", Utc(10, 21, 9), 7, 20, "LEGACY-DEFAULT", 6, 97m, PurchaseOrderStatus.Rejected),
        new("po-19", Utc(10, 22, 9), 8, 21, "AMM-CENTRAL", 6, 98m, PurchaseOrderStatus.Rejected),
        new("po-20", Utc(10, 23, 9), 9, 22, "AMM-SERVICE", 6, 99m, PurchaseOrderStatus.Cancelled),
        new("po-21", Utc(10, 24, 9), 0, 23, "LEGACY-DEFAULT", 6, 100m, PurchaseOrderStatus.Cancelled),
        new("po-22", Utc(10, 25, 9), 1, 24, "AMM-CENTRAL", 6, 101m, PurchaseOrderStatus.Cancelled)
    ];

    public static readonly ProfessionalDemoReceiptSeed[] GoodsReceipts =
    [
        new("grn-01", "po-01", Utc(9, 8, 10), 7, GoodsReceiptStatus.Posted),
        new("grn-02", "po-02", Utc(9, 10, 12), 7, GoodsReceiptStatus.Posted),
        new("grn-03a", "po-03", Utc(9, 13, 10), 3, GoodsReceiptStatus.Posted), new("grn-03b", "po-03", Utc(9, 14, 10), 3, GoodsReceiptStatus.Posted), new("grn-03c", "po-03", Utc(9, 15, 10), 3, GoodsReceiptStatus.Posted),
        new("grn-04a", "po-04", Utc(9, 16, 10), 3, GoodsReceiptStatus.Posted), new("grn-04b", "po-04", Utc(9, 17, 10), 3, GoodsReceiptStatus.Posted), new("grn-04c", "po-04", Utc(9, 18, 10), 4, GoodsReceiptStatus.Posted),
        new("grn-05a", "po-05", Utc(9, 19, 10), 4, GoodsReceiptStatus.Posted), new("grn-05b", "po-05", Utc(9, 20, 10), 4, GoodsReceiptStatus.Posted), new("grn-05c", "po-05", Utc(9, 21, 10), 4, GoodsReceiptStatus.Posted),
        new("grn-06a", "po-06", Utc(9, 22, 10), 1, GoodsReceiptStatus.Posted), new("grn-06b", "po-06", Utc(9, 23, 10), 1, GoodsReceiptStatus.Posted), new("grn-06c", "po-06", Utc(9, 24, 10), 1, GoodsReceiptStatus.Posted),
        new("grn-07a", "po-07", Utc(9, 25, 10), 1, GoodsReceiptStatus.Posted), new("grn-07b", "po-07", Utc(9, 26, 10), 2, GoodsReceiptStatus.Posted),
        new("grn-08a", "po-08", Utc(10, 1, 10), 1, GoodsReceiptStatus.Posted), new("grn-08b", "po-08", Utc(10, 2, 10), 1, GoodsReceiptStatus.Posted), new("grn-08c", "po-08", Utc(10, 3, 10), 1, GoodsReceiptStatus.Posted),
        new("grn-09a", "po-09", Utc(10, 6, 10), 1, GoodsReceiptStatus.Posted), new("grn-09b", "po-09", Utc(10, 7, 10), 1, GoodsReceiptStatus.Posted), new("grn-09c", "po-09", Utc(10, 8, 10), 1, GoodsReceiptStatus.Posted),
        new("grn-10", "po-10", Utc(10, 10, 10), 1, GoodsReceiptStatus.Draft), new("grn-11", "po-11", Utc(10, 13, 10), 1, GoodsReceiptStatus.Cancelled)
    ];

    public static readonly ProfessionalDemoTransferSeed[] Transfers =
    [
        new("trf-01", Utc(9, 16, 11), "AMM-CENTRAL", "AMM-SERVICE", 0, 2, StockTransferStatus.Posted),
        new("trf-02", Utc(9, 17, 11), "AMM-SERVICE", "LEGACY-DEFAULT", 1, 2, StockTransferStatus.Posted),
        new("trf-03", Utc(9, 19, 11), "LEGACY-DEFAULT", "AMM-CENTRAL", 2, 3, StockTransferStatus.Posted),
        new("trf-04", Utc(9, 20, 11), "AMM-CENTRAL", "AMM-SERVICE", 3, 3, StockTransferStatus.Posted),
        new("trf-05", Utc(9, 22, 11), "AMM-SERVICE", "LEGACY-DEFAULT", 4, 4, StockTransferStatus.Posted),
        new("trf-06", Utc(9, 25, 11), "LEGACY-DEFAULT", "AMM-CENTRAL", 5, 1, StockTransferStatus.Draft),
        new("trf-07", Utc(10, 1, 11), "AMM-CENTRAL", "AMM-SERVICE", 6, 1, StockTransferStatus.Cancelled),
        new("trf-08", Utc(10, 2, 11), "AMM-SERVICE", "LEGACY-DEFAULT", 7, 1, StockTransferStatus.Draft),
        new("trf-09", Utc(10, 4, 11), "LEGACY-DEFAULT", "AMM-CENTRAL", 8, 1, StockTransferStatus.Cancelled),
        new("trf-10", Utc(10, 6, 11), "AMM-CENTRAL", "LEGACY-DEFAULT", 0, 1, StockTransferStatus.Posted),
        new("trf-11", Utc(10, 9, 11), "AMM-SERVICE", "AMM-CENTRAL", 1, 1, StockTransferStatus.Draft),
        new("trf-12", Utc(10, 11, 11), "AMM-CENTRAL", "LEGACY-DEFAULT", 3, 2, StockTransferStatus.Cancelled)
    ];

    public static readonly ProfessionalDemoSalesOrderSeed[] SalesOrders =
    [
        new("so-01", Utc(9, 27, 12), 0, 3, "AMM-CENTRAL", 1, OrderStatus.Draft, false, false),
        new("so-02", Utc(9, 27, 13), 1, 4, "LEGACY-DEFAULT", 1, OrderStatus.Draft, false, false),
        new("so-03", Utc(9, 28, 12), 2, 3, "AMM-CENTRAL", 1, OrderStatus.Draft, false, false),
        new("so-04", Utc(9, 28, 13), 3, 4, "LEGACY-DEFAULT", 1, OrderStatus.Draft, false, false),
        new("so-05", Utc(9, 29, 12), 4, 3, "AMM-CENTRAL", 1, OrderStatus.Draft, false, false),
        new("so-06", Utc(9, 29, 13), 5, 4, "LEGACY-DEFAULT", 1, OrderStatus.Draft, false, false),
        new("so-07", Utc(9, 30, 12), 6, 3, "AMM-CENTRAL", 1, OrderStatus.Draft, false, false),
        new("so-08", Utc(9, 30, 13), 7, 4, "LEGACY-DEFAULT", 1, OrderStatus.Draft, false, false),
        new("so-09", Utc(10, 1, 12), 8, 3, "AMM-CENTRAL", 1, OrderStatus.Draft, false, false),
        new("so-10", Utc(10, 1, 13), 9, 4, "LEGACY-DEFAULT", 1, OrderStatus.Draft, false, false),
        new("so-11", Utc(10, 2, 12), 10, 0, "AMM-CENTRAL", 1, OrderStatus.PendingApproval, false, false),
        new("so-12", Utc(10, 2, 13), 11, 2, "LEGACY-DEFAULT", 1, OrderStatus.PendingApproval, false, false),
        new("so-13", Utc(10, 3, 12), 12, 0, "AMM-CENTRAL", 1, OrderStatus.PendingApproval, false, false),
        new("so-14", Utc(10, 3, 13), 13, 2, "LEGACY-DEFAULT", 1, OrderStatus.PendingApproval, false, false),
        new("so-15", Utc(10, 4, 12), 14, 0, "AMM-CENTRAL", 1, OrderStatus.PendingApproval, false, false),
        new("so-16", Utc(10, 4, 13), 15, 2, "LEGACY-DEFAULT", 1, OrderStatus.PendingApproval, false, false),
        new("so-17", Utc(10, 5, 12), 16, 0, "AMM-CENTRAL", 1, OrderStatus.PendingApproval, false, false),
        new("so-18", Utc(10, 5, 13), 17, 2, "LEGACY-DEFAULT", 1, OrderStatus.PendingApproval, true, false),
        new("so-19", Utc(10, 6, 12), 18, 3, "AMM-CENTRAL", 1, OrderStatus.PendingApproval, true, false),
        new("so-20", Utc(10, 6, 13), 0, 2, "LEGACY-DEFAULT", 1, OrderStatus.PendingApproval, true, false),
        new("so-21", Utc(10, 7, 12), 1, 3, "AMM-CENTRAL", 1, OrderStatus.PendingApproval, true, false),
        new("so-22", Utc(10, 7, 13), 2, 2, "LEGACY-DEFAULT", 1, OrderStatus.PendingApproval, true, false),
        new("so-23", Utc(10, 8, 12), 3, 3, "AMM-CENTRAL", 1, OrderStatus.Approved, false, false),
        new("so-24", Utc(10, 8, 13), 4, 4, "LEGACY-DEFAULT", 1, OrderStatus.Approved, false, false),
        new("so-25", Utc(10, 9, 12), 5, 3, "AMM-CENTRAL", 1, OrderStatus.Approved, false, false),
        new("so-26", Utc(10, 9, 13), 6, 4, "LEGACY-DEFAULT", 1, OrderStatus.Approved, false, false),
        new("so-27", Utc(10, 10, 12), 7, 3, "AMM-CENTRAL", 1, OrderStatus.Approved, true, false),
        new("so-28", Utc(10, 10, 13), 8, 4, "LEGACY-DEFAULT", 1, OrderStatus.Approved, true, false),
        new("so-29", Utc(10, 11, 12), 9, 3, "AMM-CENTRAL", 1, OrderStatus.Approved, true, false),
        new("so-30", Utc(10, 11, 13), 10, 4, "LEGACY-DEFAULT", 1, OrderStatus.Approved, false, true),
        new("so-31", Utc(10, 12, 12), 11, 3, "AMM-CENTRAL", 1, OrderStatus.Approved, false, true),
        new("so-32", Utc(10, 12, 13), 12, 5, "LEGACY-DEFAULT", 1, OrderStatus.Approved, false, true),
        new("so-33", Utc(10, 13, 12), 13, 6, "AMM-CENTRAL", 1, OrderStatus.Rejected, false, false),
        new("so-34", Utc(10, 13, 13), 14, 5, "LEGACY-DEFAULT", 1, OrderStatus.Rejected, false, false),
        new("so-35", Utc(10, 14, 12), 15, 6, "AMM-CENTRAL", 1, OrderStatus.Rejected, false, false),
        new("so-36", Utc(10, 14, 13), 16, 5, "LEGACY-DEFAULT", 1, OrderStatus.Rejected, false, false),
        new("so-37", Utc(10, 15, 12), 17, 6, "AMM-CENTRAL", 1, OrderStatus.Rejected, false, false),
        new("so-38", Utc(10, 15, 13), 18, 5, "LEGACY-DEFAULT", 1, OrderStatus.Rejected, false, false),
        new("so-39", Utc(10, 16, 12), 0, 6, "AMM-CENTRAL", 1, OrderStatus.Rejected, false, false),
        new("so-40", Utc(10, 16, 13), 1, 5, "LEGACY-DEFAULT", 1, OrderStatus.Rejected, false, false),
        new("so-41", Utc(10, 17, 12), 2, 6, "AMM-CENTRAL", 1, OrderStatus.Rejected, false, false),
        new("so-42", Utc(10, 17, 13), 3, 5, "LEGACY-DEFAULT", 1, OrderStatus.Rejected, false, false),
        new("so-43", Utc(10, 18, 12), 4, 6, "AMM-CENTRAL", 1, OrderStatus.Rejected, false, false),
        new("so-44", Utc(10, 18, 13), 5, 5, "LEGACY-DEFAULT", 1, OrderStatus.Rejected, false, false),
        new("so-45", Utc(10, 19, 12), 6, 6, "AMM-CENTRAL", 1, OrderStatus.Rejected, false, false)
    ];

    public static readonly ProfessionalDemoServiceSeed[] ServiceJobcards =
    [
        new("srv-01", Utc(10, 20, 9), 0, 1, null, JobcardPriority.Low, JobcardStatus.Open),
        new("srv-02", Utc(10, 20, 11), 1, 4, null, JobcardPriority.Medium, JobcardStatus.Open),
        new("srv-03", Utc(10, 21, 9), 2, 1, null, JobcardPriority.High, JobcardStatus.Open),
        new("srv-04", Utc(10, 21, 11), 3, 4, null, JobcardPriority.Urgent, JobcardStatus.Open),
        new("srv-05", Utc(10, 22, 9), 4, 1, null, JobcardPriority.Medium, JobcardStatus.Open),
        new("srv-06", Utc(10, 22, 11), 5, 4, null, JobcardPriority.Medium, JobcardStatus.PartsRequested),
        new("srv-07", Utc(10, 23, 9), 6, 1, null, JobcardPriority.High, JobcardStatus.PartsRequested),
        new("srv-08", Utc(10, 23, 11), 7, 4, null, JobcardPriority.Urgent, JobcardStatus.PartsRequested),
        new("srv-09", Utc(10, 24, 9), 8, 1, null, JobcardPriority.Low, JobcardStatus.PartsRequested),
        new("srv-10", Utc(10, 24, 11), 9, 4, null, JobcardPriority.Medium, JobcardStatus.PartsRequested),
        new("srv-11", Utc(10, 25, 9), 10, 1, 0, JobcardPriority.High, JobcardStatus.Completed),
        new("srv-12", Utc(10, 25, 11), 11, 4, 1, JobcardPriority.Urgent, JobcardStatus.Completed),
        new("srv-13", Utc(10, 26, 9), 12, 1, 2, JobcardPriority.Medium, JobcardStatus.Completed),
        new("srv-14", Utc(10, 26, 11), 13, 4, null, JobcardPriority.Low, JobcardStatus.Completed),
        new("srv-15", Utc(10, 27, 9), 14, 1, null, JobcardPriority.High, JobcardStatus.Completed)
    ];

    private static DateTimeOffset Utc(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, TimeSpan.Zero);
}
