#!/usr/bin/env bash
#
# EF Core migration helper for the Klara Home modular monolith.
#
# Every module owns its own DbContext and its own migration history, so `dotnet ef` needs three
# arguments every time and gets them wrong quietly if you guess. This script derives them from a
# module name. Adding a module means adding one line to context_for().
#
#   ./tools/ef.sh add Platform AddTenantTable   create a migration
#   ./tools/ef.sh script Platform               write an idempotent SQL script to artifacts/
#   ./tools/ef.sh list Platform                 list migrations and which are applied
#   ./tools/ef.sh remove Platform               remove the most recent, if unapplied
#
set -euo pipefail

context_for() {
    case "$1" in
        Platform) echo "PlatformDbContext" ;;
        Identity) echo "IdentityDbContext" ;;
        Media) echo "MediaDbContext" ;;
        Notifications) echo "NotificationsDbContext" ;;
        Vendors) echo "VendorsDbContext" ;;
        Catalog) echo "CatalogDbContext" ;;
        Inventory) echo "InventoryDbContext" ;;
        Pricing) echo "PricingDbContext" ;;
        Carts) echo "CartsDbContext" ;;
        Orders) echo "OrdersDbContext" ;;
        Payments) echo "PaymentsDbContext" ;;
        *) return 1 ;;
    esac
}

usage() {
    echo "usage: $0 <add|remove|script|list> <Module> [MigrationName]" >&2
    exit 2
}

[ $# -ge 2 ] || usage

command="$1"
module="$2"
name="${3:-}"

context="$(context_for "$module")" || {
    echo "Unknown module '$module'. Add it to context_for() in tools/ef.sh." >&2
    exit 1
}

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
backend="$repo_root/src/backend"
project="$backend/modules/KlaraHome.Modules.$module"

[ -d "$project" ] || { echo "Module project not found: $project" >&2; exit 1; }

# Migrations live beside the DbContext, inside the module's Infrastructure layer.
output_dir="Infrastructure/Persistence/Migrations"

cd "$backend"

case "$command" in
    add)
        [ -n "$name" ] || usage
        dotnet dotnet-ef migrations add "$name" \
            --project "$project" --context "$context" --output-dir "$output_dir"
        ;;
    remove)
        dotnet dotnet-ef migrations remove --project "$project" --context "$context"
        ;;
    list)
        dotnet dotnet-ef migrations list --project "$project" --context "$context"
        ;;
    script)
        artifacts="$repo_root/artifacts/migrations"
        mkdir -p "$artifacts"
        output="$artifacts/$module-$(date -u +%Y%m%d%H%M%S).sql"

        # --idempotent guards every statement with a check against the history table, so the same
        # file can be applied to a database at any migration level. That is what makes it safe to
        # hand to a DBA who does not know which version production is on.
        dotnet dotnet-ef migrations script \
            --project "$project" --context "$context" --idempotent --output "$output"
        echo "Idempotent script written to $output"
        ;;
    *)
        usage
        ;;
esac
