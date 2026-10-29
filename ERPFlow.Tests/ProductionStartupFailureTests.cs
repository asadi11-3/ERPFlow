using System.Security;
using ERPFlow.Api;
using ERPFlow.Application.Common;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProductionStartupFailureTests
{
    [Theory]
    [InlineData(ProductionStartupStage.Schema, "SCHEMA_INCOMPATIBLE")]
    [InlineData(ProductionStartupStage.InventoryAuthority, "INVENTORY_AUTHORITY_REJECTED")]
    public void ConflictsAreAttributedToTheirOwnStage(ProductionStartupStage stage, string expected)
    {
        var code = ProductionStartupFailure.Code(stage, new WorkflowConflictException("private database detail"));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(ProductionStartupStage.ImagePath, "IMAGE_PATH_REJECTED")]
    [InlineData(ProductionStartupStage.ImageStorage, "IMAGE_STORAGE_PROBE_FAILED")]
    [InlineData(ProductionStartupStage.ImageBinding, "IMAGE_STORE_IDENTITY_MISMATCH")]
    [InlineData(ProductionStartupStage.ImageCodec, "IMAGE_CODEC_UNAVAILABLE")]
    public void ImageFailuresExposeOnlyStableCodes(ProductionStartupStage stage, string expected)
    {
        var code = ProductionStartupFailure.Code(stage, new InvalidOperationException("private physical path and identity"));
        Assert.Equal(expected, code);
        Assert.DoesNotContain("private", code);
    }

    [Theory]
    [InlineData(ProductionStartupStage.ImageStorage)]
    [InlineData(ProductionStartupStage.ImagePath)]
    public void DeniedAccessIsReportedWithoutDisclosingThePath(ProductionStartupStage stage)
    {
        var code = ProductionStartupFailure.Code(stage, new UnauthorizedAccessException("D:\\Sites\\secret"));
        Assert.Equal(stage == ProductionStartupStage.ImageStorage ? "IMAGE_ACL_DENIED" : "IMAGE_PATH_REJECTED", code);
        Assert.DoesNotContain("Sites", code);
    }

    [Fact]
    public void StorageIoAndAclFailuresAreDistinct()
    {
        Assert.Equal("IMAGE_STORAGE_IO_FAILED", ProductionStartupFailure.Code(ProductionStartupStage.ImageStorage, new IOException("locked")));
        Assert.Equal("IMAGE_ACL_DENIED", ProductionStartupFailure.Code(ProductionStartupStage.ImageStorage, new SecurityException("denied")));
    }
}
