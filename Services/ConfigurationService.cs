using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TallySyncService.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TallySyncService.Services
{
    public class ConfigurationService
    {
        private readonly string _configPath;
        private readonly string _yamlPath;
        private readonly ILogger _logger;

        public ConfigurationService(ILogger logger, string configPath = "config.json", string yamlPath = "tally-export-config.yaml")
        {
            _logger = logger;
            _configPath = configPath;
            _yamlPath = yamlPath;
        }

        public async Task<AppConfiguration> LoadConfigurationAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(_configPath))
                    {
                        _logger.LogMessage("Configuration file not found. Creating default...");
                        var defaultConfig = new AppConfiguration();
                        SaveConfiguration(defaultConfig);
                        return defaultConfig;
                    }

                    string json = File.ReadAllText(_configPath);
                    dynamic? obj = JsonConvert.DeserializeObject<dynamic>(json);
                    
                    var config = new AppConfiguration();
                    
                    if (obj?["tally"] != null)
                    {
                        var tallyObj = obj["tally"];
                        config.Tally.Server = tallyObj["server"]?.ToString() ?? "localhost";
                        config.Tally.Port = int.TryParse(tallyObj["port"]?.ToString() ?? "9000", out int p) ? p : 9000;
                        config.Tally.Company = tallyObj["company"]?.ToString() ?? "";
                        
                        if (tallyObj["selectedTables"] is Newtonsoft.Json.Linq.JArray tables)
                        {
                            config.Tally.SelectedTables = tables.ToObject<List<string>>() ?? new List<string>();
                        }
                    }

                    if (obj?["sync"] != null)
                    {
                        var syncObj = obj["sync"];
                        config.Sync.IntervalMinutes = int.TryParse(syncObj["intervalMinutes"]?.ToString() ?? "0", out int i) ? i : 0;
                        config.Sync.ExportPath = syncObj["exportPath"]?.ToString() ?? "./exports";
                    }

                    return config;
                }
                catch (Exception ex)
                {
                    _logger.LogError("ConfigurationService.LoadConfigurationAsync()", ex);
                    return new AppConfiguration();
                }
            });
        }

        public async Task SaveConfigurationAsync(AppConfiguration config)
        {
            await Task.Run(() => SaveConfiguration(config));
        }

        private void SaveConfiguration(AppConfiguration config)
        {
            try
            {
                var json = JsonConvert.SerializeObject(new
                {
                    tally = new
                    {
                        server = config.Tally.Server,
                        port = config.Tally.Port,
                        company = config.Tally.Company,
                        selectedTables = config.Tally.SelectedTables
                    },
                    sync = new
                    {
                        intervalMinutes = config.Sync.IntervalMinutes,
                        exportPath = config.Sync.ExportPath
                    }
                }, Formatting.Indented);

                File.WriteAllText(_configPath, json);
                _logger.LogMessage("Configuration saved to {0}", _configPath);
            }
            catch (Exception ex)
            {
                _logger.LogError("ConfigurationService.SaveConfiguration()", ex);
            }
        }

        public async Task<List<TableDefinition>> LoadTableDefinitionsAsync()
        {
            return await Task.Run(() =>
            {
                var result = new List<TableDefinition>();
                try
                {
                    if (!File.Exists(_yamlPath))
                    {
                        _logger.LogMessage("YAML configuration file not found: {0}", _yamlPath);
                        return result;
                    }

                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();

                    string yaml = File.ReadAllText(_yamlPath);
                    dynamic? config = deserializer.Deserialize<dynamic>(yaml);

                    if (config != null)
                    {
                        if (config.ContainsKey("master"))
                        {
                            result.AddRange(ParseTableDefinitions(config["master"]));
                        }

                        if (config.ContainsKey("transaction"))
                        {
                            result.AddRange(ParseTableDefinitions(config["transaction"]));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("ConfigurationService.LoadTableDefinitionsAsync()", ex);
                }

                return result;
            });
        }

        private List<TableDefinition> ParseTableDefinitions(dynamic? tableList)
        {
            var result = new List<TableDefinition>();

            if (tableList is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    try
                    {
                        if (item is Dictionary<object, object> dict)
                        {
                            var table = new TableDefinition
                            {
                                Name = dict.ContainsKey("name") ? dict["name"]?.ToString() ?? "" : "",
                                Collection = dict.ContainsKey("collection") ? dict["collection"]?.ToString() ?? "" : ""
                            };

                            if (dict.ContainsKey("fields") && dict["fields"] is System.Collections.IEnumerable fields)
                            {
                                foreach (var field in fields)
                                {
                                    if (field is Dictionary<object, object> fieldDict)
                                    {
                                        table.Fields.Add(new Field
                                        {
                                            Name = fieldDict.ContainsKey("name") ? fieldDict["name"]?.ToString() ?? "" : "",
                                            FieldName = fieldDict.ContainsKey("field") ? fieldDict["field"]?.ToString() ?? "" : "",
                                            Type = fieldDict.ContainsKey("type") ? fieldDict["type"]?.ToString() ?? "text" : "text"
                                        });
                                    }
                                }
                            }

                            if (dict.ContainsKey("filters") && dict["filters"] is System.Collections.IEnumerable filters)
                            {
                                foreach (var filter in filters)
                                {
                                    if (filter != null)
                                        table.Filters.Add(filter.ToString());
                                }
                            }

                            if (dict.ContainsKey("fetch") && dict["fetch"] is System.Collections.IEnumerable fetch)
                            {
                                foreach (var f in fetch)
                                {
                                    if (f != null)
                                        table.Fetch.Add(f.ToString());
                                }
                            }

                            if (!string.IsNullOrEmpty(table.Name))
                                result.Add(table);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("ParseTableDefinitions", ex);
                    }
                }
            }

            return result;
        }
    }
}
