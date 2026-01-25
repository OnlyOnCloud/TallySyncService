using System;
using System.Collections.Generic;

namespace TallySyncService.Models
{
    public class AppConfiguration
    {
        public TallyConfig Tally { get; set; } = new TallyConfig();
        public SyncConfig Sync { get; set; } = new SyncConfig();
    }

    public class TallyConfig
    {
        public string Server { get; set; } = "localhost";
        public int Port { get; set; } = 9000;
        public string Company { get; set; } = "";
        public List<string> SelectedTables { get; set; } = new List<string>();
    }

    public class SyncConfig
    {
        public int IntervalMinutes { get; set; } = 15;
        public string ExportPath { get; set; } = "./exports";
    }

    public class TableDefinition
    {
        public string Name { get; set; } = "";
        public string Collection { get; set; } = "";
        public List<Field> Fields { get; set; } = new List<Field>();
        public List<string> Filters { get; set; } = new List<string>();
        public List<string> Fetch { get; set; } = new List<string>();
    }

    public class Field
    {
        public string Name { get; set; } = "";
        public string FieldName { get; set; } = "";
        public string Type { get; set; } = "";
    }
}

