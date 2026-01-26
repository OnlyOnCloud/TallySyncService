using System.Text;
using System.Text.RegularExpressions;
using TallySyncService.Models;

namespace TallySyncService.Services;

public class XmlGenerator
{
    public string GenerateXmlFromTableDefinition(
        TableDefinition table, 
        Dictionary<string, string> substitutions)
    {
        var xml = new StringBuilder();
        
        // XML Header
        xml.Append(@"<?xml version=""1.0"" encoding=""utf-8""?>");
        xml.Append("<ENVELOPE><HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST>");
        xml.Append("<TYPE>Data</TYPE><ID>TallyDatabaseLoaderReport</ID></HEADER>");
        xml.Append("<BODY><DESC><STATICVARIABLES>");
        xml.Append("<SVEXPORTFORMAT>XML (Data Interchange)</SVEXPORTFORMAT>");
        xml.Append($"<SVFROMDATE>{{fromDate}}</SVFROMDATE>");
        xml.Append($"<SVTODATE>{{toDate}}</SVTODATE>");
        
        if (substitutions.ContainsKey("targetCompany") && !string.IsNullOrEmpty(substitutions["targetCompany"]))
        {
            xml.Append($"<SVCURRENTCOMPANY>{{targetCompany}}</SVCURRENTCOMPANY>");
        }
        
        xml.Append("</STATICVARIABLES><TDL><TDLMESSAGE>");
        xml.Append(@"<REPORT NAME=""TallyDatabaseLoaderReport""><FORMS>MyForm</FORMS></REPORT>");
        xml.Append(@"<FORM NAME=""MyForm""><PARTS>MyPart01</PARTS></FORM>");

        // Build routes from collection
        var routes = table.Collection.Split('.');
        var targetCollection = routes[0];
        var routesList = new List<string> { "MyCollection" };
        if (routes.Length > 1)
        {
            routesList.AddRange(routes.Skip(1));
        }

        // Generate PART elements
        for (int i = 0; i < routesList.Count; i++)
        {
            var partName = $"MyPart{(i + 1):D2}";
            var lineName = $"MyLine{(i + 1):D2}";
            xml.Append($@"<PART NAME=""{partName}"">");
            xml.Append($"<LINES>{lineName}</LINES>");
            xml.Append($"<REPEAT>{lineName} : {routesList[i]}</REPEAT>");
            xml.Append("<SCROLLED>Vertical</SCROLLED></PART>");
        }

        // Generate LINE elements
        for (int i = 0; i < routesList.Count - 1; i++)
        {
            var lineName = $"MyLine{(i + 1):D2}";
            var partName = $"MyPart{(i + 2):D2}";
            xml.Append($@"<LINE NAME=""{lineName}"">");
            xml.Append("<FIELDS>FldBlank</FIELDS>");
            xml.Append($"<EXPLODE>{partName}</EXPLODE></LINE>");
        }

        // Last LINE with fields
        var lastLine = $"MyLine{routesList.Count:D2}";
        xml.Append($@"<LINE NAME=""{lastLine}""><FIELDS>");
        
        for (int i = 0; i < table.Fields.Count; i++)
        {
            xml.Append($"Fld{(i + 1):D2}");
            if (i < table.Fields.Count - 1) xml.Append(",");
        }
        xml.Append("</FIELDS></LINE>");

        // Generate FIELD elements
        for (int i = 0; i < table.Fields.Count; i++)
        {
            var field = table.Fields[i];
            var fieldName = $"Fld{(i + 1):D2}";
            xml.Append($@"<FIELD NAME=""{fieldName}"">");
            
            var fieldExpr = GenerateFieldExpression(field);
            xml.Append($"<SET>{fieldExpr}</SET>");
            xml.Append($"<XMLTAG>F{(i + 1):D2}</XMLTAG>");
            xml.Append("</FIELD>");
        }

        // Blank field
        xml.Append(@"<FIELD NAME=""FldBlank""><SET>""""</SET></FIELD>");

        // Collection
        xml.Append($@"<COLLECTION NAME=""MyCollection""><TYPE>{targetCollection}</TYPE>");
        
        // Fetch
        if (table.Fetch != null && table.Fetch.Count > 0)
        {
            xml.Append($"<FETCH>{string.Join(",", table.Fetch)}</FETCH>");
        }

        // Filters
        if (table.Filters != null && table.Filters.Count > 0)
        {
            xml.Append("<FILTER>");
            for (int i = 0; i < table.Filters.Count; i++)
            {
                xml.Append($"Fltr{(i + 1):D2}");
                if (i < table.Filters.Count - 1) xml.Append(",");
            }
            xml.Append("</FILTER>");
        }

        xml.Append("</COLLECTION>");

        // Filter definitions
        if (table.Filters != null && table.Filters.Count > 0)
        {
            for (int i = 0; i < table.Filters.Count; i++)
            {
                xml.Append($@"<SYSTEM TYPE=""Formulae"" NAME=""Fltr{(i + 1):D2}"">");
                xml.Append(EscapeXml(table.Filters[i]));
                xml.Append("</SYSTEM>");
            }
        }

        xml.Append("</TDLMESSAGE></TDL></DESC></BODY></ENVELOPE>");

        return SubstituteTdlParameters(xml.ToString(), substitutions);
    }

    private string GenerateFieldExpression(FieldDefinition field)
    {
        var fieldName = field.Field;
        var fieldType = field.Type;

        // Check if it's a simple field reference or complex expression
        if (Regex.IsMatch(fieldName, @"^(\.\.)?[a-zA-Z0-9_]+$"))
        {
            // Simple field reference
            return fieldType switch
            {
                "text" => $"${fieldName}",
                "logical" => $"if ${fieldName} then 1 else 0",
                "date" => $@"if $$IsEmpty:${fieldName} then $$StrByCharCode:241 else $$PyrlYYYYMMDDFormat:${fieldName}:""-""",
                "number" => $@"if $$IsEmpty:${fieldName} then ""0"" else $$String:${fieldName}",
                "amount" => $@"$$StringFindAndReplace:(if $$IsDebit:${fieldName} then -$$NumValue:${fieldName} else $$NumValue:${fieldName}):""(-)"":""-""",
                "quantity" => $@"$$StringFindAndReplace:(if $$IsInwards:${fieldName} then $$Number:$$String:${fieldName}:""TailUnits"" else -$$Number:$$String:${fieldName}:""TailUnits""):""(-)"":""-""",
                "rate" => $"if $$IsEmpty:${fieldName} then 0 else $$Number:${fieldName}",
                _ => fieldName
            };
        }
        else
        {
            // Complex expression - use as is
            return fieldName;
        }
    }

    public string SubstituteTdlParameters(string xml, Dictionary<string, string> substitutions)
    {
        foreach (var kvp in substitutions)
        {
            var pattern = $"{{{kvp.Key}}}";
            xml = xml.Replace(pattern, EscapeXml(kvp.Value));
        }
        return xml;
    }

    private string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
