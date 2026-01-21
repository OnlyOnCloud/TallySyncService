using Newtonsoft.Json.Linq;
using TallySyncService.Services;
using System.IO;
using System.Reflection;

namespace TallySyncService.Tests;

/// <summary>
/// Unit tests for XML to JSON conversion and data type validation
/// Run with: dotnet test
/// </summary>
public class XmlToJsonConverterTests
{
    private readonly IXmlToJsonConverter _converter;
    private readonly ILogger<XmlToJsonConverter> _logger;

    public XmlToJsonConverterTests()
    {
        // Create logger
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<XmlToJsonConverter>();
        
        // Create converter
        _converter = new XmlToJsonConverter(_logger);
    }

    [Fact]
    public void ConvertLedgerXml_ValidInput_ReturnsCorrectRecordCount()
    {
        // Arrange
        var xmlData = LoadSampleData("sample-ledger.xml");
        var tableName = "Ledgers";

        // Act
        var records = _converter.ConvertTallyXmlToRecords(xmlData, tableName);

        // Assert
        Assert.NotEmpty(records);
        Assert.Equal(5, records.Count); // 5 ledgers in sample
    }

    [Fact]
    public void ConvertStockItemXml_ValidInput_ReturnsCorrectRecords()
    {
        // Arrange
        var xmlData = LoadSampleData("sample-stockitem.xml");
        var tableName = "StockItems";

        // Act
        var records = _converter.ConvertTallyXmlToRecords(xmlData, tableName);

        // Assert
        Assert.NotEmpty(records);
        Assert.Equal(3, records.Count); // 3 stock items in sample
    }

    [Fact]
    public void ConvertVoucherXml_ValidInput_ReturnsCorrectRecords()
    {
        // Arrange
        var xmlData = LoadSampleData("sample-voucher.xml");
        var tableName = "Vouchers";

        // Act
        var records = _converter.ConvertTallyXmlToRecords(xmlData, tableName);

        // Assert
        Assert.NotEmpty(records);
        Assert.Equal(3, records.Count); // 3 vouchers in sample
    }

    [Fact]
    public void ConvertLedger_ExtractsAllFields()
    {
        // Arrange
        var xmlData = LoadSampleData("sample-ledger.xml");
        var tableName = "Ledgers";

        // Act
        var records = _converter.ConvertTallyXmlToRecords(xmlData, tableName);
        var firstRecord = records.First();
        var data = (JObject)firstRecord.Data;

        // Assert
        Assert.NotNull(data["NAME"]);
        Assert.NotNull(data["GUID"]);
        Assert.NotNull(data["PARENT"]);
        Assert.NotNull(data["LEDGERTYPE"]);
        Assert.NotNull(data["CREATIONDATE"]);
        Assert.NotNull(data["LASTMODIFICATIONDATE"]);
        Assert.NotNull(data["ISDELETED"]);
    }

    [Fact]
    public void ConvertVoucher_NestedEntriesParsedCorrectly()
    {
        // Arrange
        var xmlData = LoadSampleData("sample-voucher.xml");
        var tableName = "Vouchers";

        // Act
        var records = _converter.ConvertTallyXmlToRecords(xmlData, tableName);
        var firstVoucher = records.First();
        var data = (JObject)firstVoucher.Data;

        // Assert
        Assert.NotNull(data["ENTRIES"]);
        var entries = data["ENTRIES"];
        Assert.NotEmpty(entries); // Should have nested entries
    }

    [Fact]
    public void ComputeHash_SameData_SameHash()
    {
        // Arrange
        var data1 = new { Name = "Test", Value = 123 };
        var data2 = new { Name = "Test", Value = 123 };

        // Act
        var hash1 = _converter.ComputeHash(data1);
        var hash2 = _converter.ComputeHash(data2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentData_DifferentHash()
    {
        // Arrange
        var data1 = new { Name = "Test", Value = 123 };
        var data2 = new { Name = "Test", Value = 456 };

        // Act
        var hash1 = _converter.ComputeHash(data1);
        var hash2 = _converter.ComputeHash(data2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ConvertLedger_DateFieldsAreCorrectType()
    {
        // Arrange
        var xmlData = LoadSampleData("sample-ledger.xml");
        var tableName = "Ledgers";

        // Act
        var records = _converter.ConvertTallyXmlToRecords(xmlData, tableName);
        var firstRecord = records.First();
        var data = (JObject)firstRecord.Data;

        // Assert - dates should be parsed as strings in ISO format
        var creationDate = data["CREATIONDATE"]?.ToString();
        var modificationDate = data["LASTMODIFICATIONDATE"]?.ToString();
        
        Assert.NotNull(creationDate);
        Assert.NotNull(modificationDate);
        
        // Should be in YYYY-MM-DD format
        Assert.Matches(@"\d{4}-\d{2}-\d{2}", creationDate);
        Assert.Matches(@"\d{4}-\d{2}-\d{2}", modificationDate);
    }

    [Fact]
    public void ConvertVoucher_NumericFieldsAreCorrectType()
    {
        // Arrange
        var xmlData = LoadSampleData("sample-voucher.xml");
        var tableName = "Vouchers";

        // Act
        var records = _converter.ConvertTallyXmlToRecords(xmlData, tableName);
        var firstRecord = records.First();
        var data = (JObject)firstRecord.Data;

        // Assert - amount fields should be numeric (not strings)
        var amount = data["AMOUNT"];
        var taxAmount = data["TAXAMOUNT"];
        
        Assert.NotNull(amount);
        Assert.NotNull(taxAmount);
        
        // Should be convertible to decimal
        Assert.True(decimal.TryParse(amount.ToString(), out _), "AMOUNT should be numeric");
        Assert.True(decimal.TryParse(taxAmount.ToString(), out _), "TAXAMOUNT should be numeric");
    }

    [Fact]
    public void ConvertVoucher_BooleanFieldsAreCorrectType()
    {
        // Arrange
        var xmlData = LoadSampleData("sample-voucher.xml");
        var tableName = "Vouchers";

        // Act
        var records = _converter.ConvertTallyXmlToRecords(xmlData, tableName);
        var firstRecord = records.First();
        var data = (JObject)firstRecord.Data;

        // Assert - ISDELETED should be boolean or string that represents boolean
        var isDeleted = data["ISDELETED"]?.ToString();
        Assert.NotNull(isDeleted);
        Assert.True(
            isDeleted.Equals("No", StringComparison.OrdinalIgnoreCase) || 
            isDeleted.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            isDeleted.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
            isDeleted.Equals("true", StringComparison.OrdinalIgnoreCase),
            "ISDELETED should be a boolean flag"
        );
    }

    [Fact]
    public void ConvertLedger_AllRecordsHaveValidIds()
    {
        // Arrange
        var xmlData = LoadSampleData("sample-ledger.xml");
        var tableName = "Ledgers";

        // Act
        var records = _converter.ConvertTallyXmlToRecords(xmlData, tableName);

        // Assert
        foreach (var record in records)
        {
            Assert.NotNull(record.Id);
            Assert.NotEmpty(record.Id);
        }
    }

    [Fact]
    public void ConvertLedger_AllRecordsHaveValidHashes()
    {
        // Arrange
        var xmlData = LoadSampleData("sample-ledger.xml");
        var tableName = "Ledgers";

        // Act
        var records = _converter.ConvertTallyXmlToRecords(xmlData, tableName);

        // Assert
        foreach (var record in records)
        {
            Assert.NotNull(record.Hash);
            Assert.NotEmpty(record.Hash);
            Assert.True(record.Hash.Length == 64, "Hash should be SHA256 (64 hex chars)");
        }
    }

    [Fact]
    public void ConvertXml_InvalidXmlEntities_HandledGracefully()
    {
        // Arrange
        var xmlWithInvalidEntities = @"<?xml version='1.0'?>
<RESPONSE>
  <LEDGER>
    <NAME>Test &#x00; Entry</NAME>
    <GUID>001-test</GUID>
    <PARENT>Assets</PARENT>
    <CREATIONDATE>2024-01-01</CREATIONDATE>
  </LEDGER>
</RESPONSE>";

        // Act & Assert - Should not throw
        var records = _converter.ConvertTallyXmlToRecords(xmlWithInvalidEntities, "Ledgers");
        Assert.NotEmpty(records);
    }

    [Fact]
    public void ConvertLedger_RecordOperationDefaultsToInsert()
    {
        // Arrange
        var xmlData = LoadSampleData("sample-ledger.xml");
        var tableName = "Ledgers";

        // Act
        var records = _converter.ConvertTallyXmlToRecords(xmlData, tableName);

        // Assert
        foreach (var record in records)
        {
            Assert.Equal("INSERT", record.Operation);
        }
    }

    // Helper method to load sample data
    private string LoadSampleData(string fileName)
    {
        var basePath = AppContext.BaseDirectory;
        var sampleDataPath = Path.Combine(basePath, "../../../../../sample-data", fileName);
        
        if (!File.Exists(sampleDataPath))
        {
            sampleDataPath = Path.Combine(basePath, "sample-data", fileName);
        }
        
        if (!File.Exists(sampleDataPath))
        {
            throw new FileNotFoundException($"Sample data file not found: {sampleDataPath}");
        }
        
        return File.ReadAllText(sampleDataPath);
    }
}
