using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopDelivery.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sqlServer = migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer";
            string ProviderType(string sqlType, string sqliteType) => sqlServer ? sqlType : sqliteType;

            migrationBuilder.CreateTable(
                name: "Brands",
                columns: table => new
                {
                    Id = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: ProviderType("nvarchar(200)", "TEXT"), maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: ProviderType("nvarchar(200)", "TEXT"), maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerBudgets",
                columns: table => new
                {
                    CustomerId = table.Column<string>(type: ProviderType("nvarchar(64)", "TEXT"), maxLength: 64, nullable: false),
                    MonthlyBudget = table.Column<decimal>(type: ProviderType("decimal(18,2)", "TEXT"), precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBudgets", x => x.CustomerId);
                });

            migrationBuilder.CreateTable(
                name: "ImageAssets",
                columns: table => new
                {
                    Id = table.Column<long>(type: ProviderType("bigint", "INTEGER"), nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContentHash = table.Column<string>(type: ProviderType("nvarchar(64)", "TEXT"), maxLength: 64, nullable: false),
                    MimeType = table.Column<string>(type: ProviderType("nvarchar(128)", "TEXT"), maxLength: 128, nullable: false),
                    Width = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false),
                    Height = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false),
                    ByteLength = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false),
                    StorageProvider = table.Column<string>(type: ProviderType("nvarchar(32)", "TEXT"), maxLength: 32, nullable: false),
                    Data = table.Column<byte[]>(type: ProviderType("varbinary(max)", "BLOB"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: ProviderType("datetimeoffset", "TEXT"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: ProviderType("nvarchar(200)", "TEXT"), maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: ProviderType("nvarchar(200)", "TEXT"), maxLength: 200, nullable: false),
                    BranchIdentifier = table.Column<string>(type: ProviderType("nvarchar(128)", "TEXT"), maxLength: 128, nullable: false, defaultValue: ""),
                    Location = table.Column<string>(type: ProviderType("nvarchar(500)", "TEXT"), maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CatalogKey = table.Column<string>(type: ProviderType("nvarchar(64)", "TEXT"), maxLength: 64, nullable: true),
                    Gtin = table.Column<string>(type: ProviderType("nvarchar(32)", "TEXT"), maxLength: 32, nullable: true),
                    Name = table.Column<string>(type: ProviderType("nvarchar(450)", "TEXT"), maxLength: 450, nullable: false),
                    NormalizedName = table.Column<string>(type: ProviderType("nvarchar(450)", "TEXT"), maxLength: 450, nullable: false),
                    BrandId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: true),
                    Category = table.Column<string>(type: ProviderType("nvarchar(256)", "TEXT"), maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: ProviderType("nvarchar(32)", "TEXT"), maxLength: 32, nullable: false),
                    SourceUrl = table.Column<string>(type: ProviderType("nvarchar(2048)", "TEXT"), maxLength: 2048, nullable: true),
                    MergedIntoProductId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: ProviderType("datetimeoffset", "TEXT"), nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: ProviderType("datetimeoffset", "TEXT"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Products_Products_MergedIntoProductId",
                        column: x => x.MergedIntoProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: ProviderType("nvarchar(64)", "TEXT"), maxLength: 64, nullable: false),
                    StoreId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false),
                    PurchasedAt = table.Column<DateTimeOffset>(type: ProviderType("datetimeoffset", "TEXT"), nullable: false),
                    Total = table.Column<decimal>(type: ProviderType("decimal(18,2)", "TEXT"), precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Receipts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false),
                    ImageAssetId = table.Column<long>(type: ProviderType("bigint", "INTEGER"), nullable: false),
                    IsPrimary = table.Column<bool>(type: ProviderType("bit", "INTEGER"), nullable: false),
                    SourceUrl = table.Column<string>(type: ProviderType("nvarchar(2048)", "TEXT"), maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductImages_ImageAssets_ImageAssetId",
                        column: x => x.ImageAssetId,
                        principalTable: "ImageAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductImages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductReviewItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: ProviderType("bigint", "INTEGER"), nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProposedProductId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false),
                    CandidateProductId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: true),
                    RawName = table.Column<string>(type: ProviderType("nvarchar(450)", "TEXT"), maxLength: 450, nullable: false),
                    NormalizedName = table.Column<string>(type: ProviderType("nvarchar(450)", "TEXT"), maxLength: 450, nullable: false),
                    SourceType = table.Column<string>(type: ProviderType("nvarchar(64)", "TEXT"), maxLength: 64, nullable: false),
                    SourceReference = table.Column<string>(type: ProviderType("nvarchar(2048)", "TEXT"), maxLength: 2048, nullable: true),
                    Status = table.Column<string>(type: ProviderType("nvarchar(32)", "TEXT"), maxLength: 32, nullable: false),
                    SubmittedByCustomerIdHash = table.Column<string>(type: ProviderType("nvarchar(64)", "TEXT"), maxLength: 64, nullable: true),
                    AdminNote = table.Column<string>(type: ProviderType("nvarchar(1000)", "TEXT"), maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: ProviderType("datetimeoffset", "TEXT"), nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: ProviderType("datetimeoffset", "TEXT"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductReviewItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductReviewItems_Products_CandidateProductId",
                        column: x => x.CandidateProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductReviewItems_Products_ProposedProductId",
                        column: x => x.ProposedProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoreProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false),
                    ProductId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false),
                    Name = table.Column<string>(type: ProviderType("nvarchar(450)", "TEXT"), maxLength: 450, nullable: false),
                    NormalizedName = table.Column<string>(type: ProviderType("nvarchar(450)", "TEXT"), maxLength: 450, nullable: false),
                    StoreProductCode = table.Column<string>(type: ProviderType("nvarchar(128)", "TEXT"), maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: ProviderType("nvarchar(32)", "TEXT"), maxLength: 32, nullable: false),
                    ConfirmationCount = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false),
                    RejectionCount = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: ProviderType("datetimeoffset", "TEXT"), nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: ProviderType("datetimeoffset", "TEXT"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreProducts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PriceObservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false),
                    StoreProductId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: true),
                    ReceiptId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false),
                    RawText = table.Column<string>(type: ProviderType("nvarchar(max)", "TEXT"), nullable: false),
                    Price = table.Column<decimal>(type: ProviderType("decimal(18,2)", "TEXT"), precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceObservations_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceObservations_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceObservations_StoreProducts_StoreProductId",
                        column: x => x.StoreProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoreProductMatchHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: ProviderType("bigint", "INTEGER"), nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreProductId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: false),
                    PreviousProductId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: true),
                    NewProductId = table.Column<int>(type: ProviderType("int", "INTEGER"), nullable: true),
                    Decision = table.Column<string>(type: ProviderType("nvarchar(32)", "TEXT"), maxLength: 32, nullable: false),
                    CustomerIdHash = table.Column<string>(type: ProviderType("nvarchar(64)", "TEXT"), maxLength: 64, nullable: true),
                    Note = table.Column<string>(type: ProviderType("nvarchar(1000)", "TEXT"), maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: ProviderType("datetimeoffset", "TEXT"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProductMatchHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreProductMatchHistory_Products_NewProductId",
                        column: x => x.NewProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreProductMatchHistory_Products_PreviousProductId",
                        column: x => x.PreviousProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreProductMatchHistory_StoreProducts_StoreProductId",
                        column: x => x.StoreProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Brands_NormalizedName",
                table: "Brands",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImageAssets_ContentHash",
                table: "ImageAssets",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceObservations_ProductId",
                table: "PriceObservations",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceObservations_ReceiptId",
                table: "PriceObservations",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceObservations_StoreProductId",
                table: "PriceObservations",
                column: "StoreProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ImageAssetId",
                table: "ProductImages",
                column: "ImageAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId_ImageAssetId",
                table: "ProductImages",
                columns: new[] { "ProductId", "ImageAssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId_IsPrimary",
                table: "ProductImages",
                columns: new[] { "ProductId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewItems_CandidateProductId",
                table: "ProductReviewItems",
                column: "CandidateProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewItems_ProposedProductId",
                table: "ProductReviewItems",
                column: "ProposedProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewItems_Status_CreatedAt",
                table: "ProductReviewItems",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_BrandId",
                table: "Products",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CatalogKey",
                table: "Products",
                column: "CatalogKey",
                unique: true,
                filter: "[CatalogKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Gtin",
                table: "Products",
                column: "Gtin",
                unique: true,
                filter: "[Gtin] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_MergedIntoProductId",
                table: "Products",
                column: "MergedIntoProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_NormalizedName_BrandId",
                table: "Products",
                columns: new[] { "NormalizedName", "BrandId" });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_CustomerId_PurchasedAt",
                table: "Receipts",
                columns: new[] { "CustomerId", "PurchasedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_StoreId",
                table: "Receipts",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductMatchHistory_NewProductId",
                table: "StoreProductMatchHistory",
                column: "NewProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductMatchHistory_PreviousProductId",
                table: "StoreProductMatchHistory",
                column: "PreviousProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductMatchHistory_StoreProductId_CreatedAt",
                table: "StoreProductMatchHistory",
                columns: new[] { "StoreProductId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_ProductId",
                table: "StoreProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_StoreId_NormalizedName",
                table: "StoreProducts",
                columns: new[] { "StoreId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_StoreId_StoreProductCode",
                table: "StoreProducts",
                columns: new[] { "StoreId", "StoreProductCode" },
                unique: true,
                filter: "[StoreProductCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Stores_NormalizedName_BranchIdentifier",
                table: "Stores",
                columns: new[] { "NormalizedName", "BranchIdentifier" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerBudgets");

            migrationBuilder.DropTable(
                name: "PriceObservations");

            migrationBuilder.DropTable(
                name: "ProductImages");

            migrationBuilder.DropTable(
                name: "ProductReviewItems");

            migrationBuilder.DropTable(
                name: "StoreProductMatchHistory");

            migrationBuilder.DropTable(
                name: "Receipts");

            migrationBuilder.DropTable(
                name: "ImageAssets");

            migrationBuilder.DropTable(
                name: "StoreProducts");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Stores");

            migrationBuilder.DropTable(
                name: "Brands");
        }
    }
}
