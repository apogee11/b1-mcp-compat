using System.Text.Json;
using B1.Mcp.Compat.Core.Catalog;

namespace B1.Mcp.Compat.Core.Personal;

/// <summary>
/// Reproduces <c>PersonalFieldsSetupsService_GetPersonalFieldsByTable</c> (FP2608) from the company's OPFS table.
/// The rows are those of OPFS selected like the native action does (<see cref="PersonalTableMap"/>); letter codes were verified against a native FP2608 (780/780 rows).
/// </summary>
public static class PersonalFieldMapper
{
    private static readonly Dictionary<string, string> Category = new()
    {
        ["P"] = "PurchaseAP", ["R"] = "SalesAR", ["B"] = "InventoryTransfersandRequestsPurchase", ["A"] = "InventoryTransfersandRequestsSales",
        ["Q"] = "PurchaseRequest", ["N"] = "None", ["O"] = "Company", ["I"] = "OutgoingPayments", ["H"] = "IncomingPayments",
        ["S"] = "BusinessPartnerContactPerson", ["Y"] = "ServiceCallEmployee", ["X"] = "PaymentResultsBusinessPartner",
        ["U"] = "PaymentResultsUser", ["M"] = "BillofExchangeforIncomingPayment", ["E"] = "CustomerEquipmentCardBusinessPartner",
        ["F"] = "CustomerEquipmentCardDirectPartner", ["C"] = "OpportunityBusinessPartner", ["D"] = "OpportunityBusinessPartnerChannel",
        ["L"] = "LandedCostVendor", ["G"] = "LandedCostBroker",
    };

    private static readonly Dictionary<string, string> Type = new() { ["P"] = "Personal", ["N"] = "NotPersonal", ["S"] = "SensitivePersonal" };

    /// <summary>Enum name of an OPFS category letter, as served by the Service Layer.</summary>
    public static string CategoryName(string? letter) => "pfsc_" + (letter != null && Category.TryGetValue(letter.Trim(), out var c) ? c : "None");

    public static string ToJson(IEnumerable<PersonalFieldRow> rows, string table, string contextBaseUrl)
    {
        var (opfsTable, category) = PersonalTableMap.Resolve(table);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("@odata.context", contextBaseUrl.TrimEnd('/') + "/b1s/v2/$metadata#PersonalFieldsSetups");
            w.WriteStartArray("value");
            foreach (var r in rows.Where(r => string.Equals(r.TableName, opfsTable, StringComparison.OrdinalIgnoreCase) && r.Category?.Trim() == category).OrderBy(r => r.AbsEntry))
            {
                w.WriteStartObject();
                w.WriteNumber("AbsEntry", r.AbsEntry);
                w.WriteString("DataType", r.RefObjType);
                w.WriteString("TableName", r.TableName);
                w.WriteString("FieldName", r.FieldName);
                w.WriteString("Category", CategoryName(r.Category));
                w.WriteString("DefaultDataClassification", "pfsddc_" + (r.OrigType?.Trim() == "S" ? "SensitivePersonal" : "Personal"));
                w.WriteString("DataClassification", "pfsdc_" + (r.Type != null && Type.TryGetValue(r.Type.Trim(), out var t) ? t : "Personal"));
                w.WriteNull("Description");
                w.WriteString("IsSystemDefined", r.AbsEntry < 0 ? "tYES" : "tNO");
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
