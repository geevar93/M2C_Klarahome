using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Reviews.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialReviewsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reviews");

            migrationBuilder.CreateTable(
                name: "abuse_reports",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    resolution = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_abuse_reports", x => x.id);
                    table.CheckConstraint("ck_abuse_reports_reason", "reason IN ('Spam', 'Offensive', 'Irrelevant', 'Misleading', 'PersonalData', 'Illegal', 'Other')");
                    table.CheckConstraint("ck_abuse_reports_status", "status IN ('Open', 'Upheld', 'Dismissed')");
                    table.CheckConstraint("ck_abuse_reports_target", "target_type IN ('Review', 'Question', 'Answer')");
                });

            migrationBuilder.CreateTable(
                name: "product_ratings",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    average = table.Column<decimal>(type: "numeric(2,1)", nullable: true),
                    count = table.Column<int>(type: "integer", nullable: false),
                    one_star = table.Column<int>(type: "integer", nullable: false),
                    two_star = table.Column<int>(type: "integer", nullable: false),
                    three_star = table.Column<int>(type: "integer", nullable: false),
                    four_star = table.Column<int>(type: "integer", nullable: false),
                    five_star = table.Column<int>(type: "integer", nullable: false),
                    recomputed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_ratings", x => x.id);
                    table.CheckConstraint("ck_product_ratings_average", "(average IS NULL AND count = 0) OR (average BETWEEN 1 AND 5 AND count > 0)");
                    table.CheckConstraint("ck_product_ratings_histogram", "one_star >= 0 AND two_star >= 0 AND three_star >= 0 AND four_star >= 0 AND five_star >= 0 AND one_star + two_star + three_star + four_star + five_star = count");
                });

            migrationBuilder.CreateTable(
                name: "questions",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    moderated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    moderated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    moderation_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    answer_count = table.Column<int>(type: "integer", nullable: false),
                    report_count = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questions", x => x.id);
                    table.CheckConstraint("ck_questions_status", "status IN ('Pending', 'Approved', 'Rejected')");
                });

            migrationBuilder.CreateTable(
                name: "review_votes",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_helpful = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_votes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    author_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    moderated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    moderated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    moderation_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    vendor_reply = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    vendor_replied_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    vendor_replied_by = table.Column<Guid>(type: "uuid", nullable: true),
                    helpful_count = table.Column<int>(type: "integer", nullable: false),
                    not_helpful_count = table.Column<int>(type: "integer", nullable: false),
                    report_count = table.Column<int>(type: "integer", nullable: false),
                    is_verified_purchase = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    images = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.CheckConstraint("ck_reviews_counts", "helpful_count >= 0 AND not_helpful_count >= 0 AND report_count >= 0");
                    table.CheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_reviews_status", "status IN ('Pending', 'Approved', 'Rejected')");
                });

            migrationBuilder.CreateTable(
                name: "stock_subscriptions",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_price = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    price_at_subscription = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    notified_price = table.Column<decimal>(type: "numeric", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_subscriptions", x => x.id);
                    table.CheckConstraint("ck_stock_subscriptions_contact", "customer_id IS NOT NULL OR email IS NOT NULL");
                    table.CheckConstraint("ck_stock_subscriptions_kind", "kind IN ('BackInStock', 'PriceDrop')");
                    table.CheckConstraint("ck_stock_subscriptions_price", "target_price IS NULL OR target_price > 0");
                    table.CheckConstraint("ck_stock_subscriptions_status", "status IN ('Active', 'Notified', 'Cancelled', 'Expired')");
                    table.CheckConstraint("ck_stock_subscriptions_target", "kind = 'PriceDrop' OR (target_price IS NULL AND price_at_subscription IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "vendor_ratings",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    average = table.Column<decimal>(type: "numeric(2,1)", nullable: true),
                    count = table.Column<int>(type: "integer", nullable: false),
                    recomputed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_ratings", x => x.id);
                    table.CheckConstraint("ck_vendor_ratings_average", "(average IS NULL AND count = 0) OR (average BETWEEN 1 AND 5 AND count > 0)");
                });

            migrationBuilder.CreateTable(
                name: "wishlists",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    share_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wishlists", x => x.id);
                    table.CheckConstraint("ck_wishlists_item_count", "item_count >= 0");
                });

            migrationBuilder.CreateTable(
                name: "answers",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    author_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    moderated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    moderated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    report_count = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_answers", x => x.id);
                    table.CheckConstraint("ck_answers_author", "author_type IN ('Customer', 'Vendor', 'Store')");
                    table.CheckConstraint("ck_answers_status", "status IN ('Pending', 'Approved', 'Rejected')");
                    table.CheckConstraint("ck_answers_vendor", "(author_type = 'Vendor' AND vendor_id IS NOT NULL) OR (author_type <> 'Vendor' AND vendor_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_answers_questions_question_id",
                        column: x => x.question_id,
                        principalSchema: "reviews",
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wishlist_items",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wishlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wishlist_items", x => x.id);
                    table.CheckConstraint("ck_wishlist_items_priority", "priority >= 0");
                    table.ForeignKey(
                        name: "fk_wishlist_items_wishlists_wishlist_id",
                        column: x => x.wishlist_id,
                        principalSchema: "reviews",
                        principalTable: "wishlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_abuse_reports_target_type_target_id_status",
                schema: "reviews",
                table: "abuse_reports",
                columns: new[] { "target_type", "target_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_abuse_reports_tenant_id",
                schema: "reviews",
                table: "abuse_reports",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_abuse_reports_tenant_id_status_reason_created_at",
                schema: "reviews",
                table: "abuse_reports",
                columns: new[] { "tenant_id", "status", "reason", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_abuse_reports_open",
                schema: "reviews",
                table: "abuse_reports",
                columns: new[] { "tenant_id", "target_type", "target_id", "reporter_id" },
                unique: true,
                filter: "status = 'Open' AND reporter_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_answers_question_id_created_at",
                schema: "reviews",
                table: "answers",
                columns: new[] { "question_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_answers_tenant_id",
                schema: "reviews",
                table: "answers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_answers_tenant_id_status_created_at",
                schema: "reviews",
                table: "answers",
                columns: new[] { "tenant_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_product_ratings_tenant_id",
                schema: "reviews",
                table: "product_ratings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_product_ratings_product",
                schema: "reviews",
                table: "product_ratings",
                columns: new[] { "tenant_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questions_tenant_id",
                schema: "reviews",
                table: "questions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_questions_tenant_id_product_id_status",
                schema: "reviews",
                table: "questions",
                columns: new[] { "tenant_id", "product_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_questions_tenant_id_status_created_at",
                schema: "reviews",
                table: "questions",
                columns: new[] { "tenant_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_review_votes_tenant_id",
                schema: "reviews",
                table: "review_votes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_review_votes_voter",
                schema: "reviews",
                table: "review_votes",
                columns: new[] { "review_id", "customer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reviews_tenant_id",
                schema: "reviews",
                table: "reviews",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_tenant_id_customer_id_product_id",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "tenant_id", "customer_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_tenant_id_product_id_status_published_at",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "tenant_id", "product_id", "status", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_tenant_id_status_created_at",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "tenant_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_tenant_id_vendor_id_status",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "tenant_id", "vendor_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_reviews_order_line",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "tenant_id", "order_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_subscriptions_expiring",
                schema: "reviews",
                table: "stock_subscriptions",
                column: "expires_at",
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_stock_subscriptions_tenant_id",
                schema: "reviews",
                table: "stock_subscriptions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_subscriptions_tenant_id_customer_id_status",
                schema: "reviews",
                table: "stock_subscriptions",
                columns: new[] { "tenant_id", "customer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_subscriptions_waiting",
                schema: "reviews",
                table: "stock_subscriptions",
                columns: new[] { "tenant_id", "variant_id", "kind" },
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_ratings_tenant_id",
                schema: "reviews",
                table: "vendor_ratings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_vendor_ratings_vendor",
                schema: "reviews",
                table: "vendor_ratings",
                columns: new[] { "tenant_id", "vendor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_items_tenant_id",
                schema: "reviews",
                table: "wishlist_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_items_tenant_id_product_id",
                schema: "reviews",
                table: "wishlist_items",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ux_wishlist_items_variant",
                schema: "reviews",
                table: "wishlist_items",
                columns: new[] { "wishlist_id", "variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wishlists_tenant_id",
                schema: "reviews",
                table: "wishlists",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_wishlists_tenant_id_customer_id_name",
                schema: "reviews",
                table: "wishlists",
                columns: new[] { "tenant_id", "customer_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_wishlists_default",
                schema: "reviews",
                table: "wishlists",
                columns: new[] { "tenant_id", "customer_id" },
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ux_wishlists_share_token",
                schema: "reviews",
                table: "wishlists",
                column: "share_token",
                unique: true,
                filter: "share_token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abuse_reports",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "answers",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "product_ratings",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "review_votes",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "reviews",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "stock_subscriptions",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "vendor_ratings",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "wishlist_items",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "questions",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "wishlists",
                schema: "reviews");
        }
    }
}
