using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Persistence.Migrations;

/// <summary>
/// Completa o split financeiro por pagamento exigido pela secao 03 do documento de arquitetura.
///
/// `platform_fee` (comissao do marketplace) e `shipping_cost` (frete retido do seller) passam a
/// ter coluna propria, ao lado de `payment_fee`, `gross_value` e `net_value` que ja existiam.
///
/// Sao o split declarado pela plataforma, para conciliacao e auditoria. O razao contabil
/// continua sendo montado a partir de MarketplaceFees, que traz cada taxa individualizada com
/// a sua categoria — lancar as duas fontes levaria a contagem dobrada.
///
/// Linhas antigas ficam com zero: o split so passa a ser preenchido nas sincronizacoes
/// seguintes. Nao ha backfill possivel sem reconsultar as APIs.
/// </summary>
public partial class AddPaymentSplitColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "PlatformFee",
            table: "MarketplacePayments",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "ShippingCost",
            table: "MarketplacePayments",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ShippingCost", table: "MarketplacePayments");
        migrationBuilder.DropColumn(name: "PlatformFee", table: "MarketplacePayments");
    }
}
