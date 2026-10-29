using System.Security;
using ERPFlow.Application.Common;
using Microsoft.Data.SqlClient;

namespace ERPFlow.Api;

public enum ProductionStartupStage
{
    Schema,
    InventoryAuthority,
    ImagePath,
    ImageStorage,
    ImageBinding,
    ImageCodec
}

public static class ProductionStartupFailure
{
    public static string Code(ProductionStartupStage stage, Exception exception) => (stage, exception) switch
    {
        (_, SqlException { Number: 229 or 297 or 916 or 18456 or 4060 }) => "DATABASE_ACCESS_DENIED",
        (ProductionStartupStage.Schema, WorkflowConflictException) => "SCHEMA_INCOMPATIBLE",
        (ProductionStartupStage.Schema, _) => "SCHEMA_VERIFICATION_FAILED",
        (ProductionStartupStage.InventoryAuthority, WorkflowConflictException) => "INVENTORY_AUTHORITY_REJECTED",
        (ProductionStartupStage.InventoryAuthority, _) => "INVENTORY_ADMISSION_FAILED",
        (ProductionStartupStage.ImagePath, _) => "IMAGE_PATH_REJECTED",
        (ProductionStartupStage.ImageStorage, UnauthorizedAccessException or SecurityException) => "IMAGE_ACL_DENIED",
        (ProductionStartupStage.ImageStorage, IOException) => "IMAGE_STORAGE_IO_FAILED",
        (ProductionStartupStage.ImageStorage, _) => "IMAGE_STORAGE_PROBE_FAILED",
        (ProductionStartupStage.ImageBinding, InvalidOperationException) => "IMAGE_STORE_IDENTITY_MISMATCH",
        (ProductionStartupStage.ImageBinding, _) => "IMAGE_BINDING_CHECK_FAILED",
        (ProductionStartupStage.ImageCodec, _) => "IMAGE_CODEC_UNAVAILABLE",
        _ => "PREREQUISITE_FAILED"
    };
}
