using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TallySyncService.Services
{
    public interface ITallyService
    {
        Task<string> PostTallyXmlAsync(string xmlPayload);
        Task<(int MasterId, int TransactionId)> GetLastAlterIdsAsync();
    }

    public class TallyService : ITallyService
    {
        private readonly string _tallyServer;
        private readonly int _tallyPort;
        private readonly string _company;
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;

        public TallyService(string server, int port, string company, ILogger logger)
        {
            _tallyServer = server;
            _tallyPort = port;
            _company = company;
            _logger = logger;
            _httpClient = new HttpClient();
        }

        public async Task<string> PostTallyXmlAsync(string xmlPayload)
        {
            try
            {
                var url = $"http://{_tallyServer}:{_tallyPort}";
                
                // Convert string to UTF-16LE bytes
                byte[] payloadBytes = Encoding.Unicode.GetBytes(xmlPayload);

                var content = new ByteArrayContent(payloadBytes);
                content.Headers.Add("Content-Type", "text/xml;charset=utf-16");

                var response = await _httpClient.PostAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {
                    // Response is in UTF-16LE
                    byte[] responseBytes = await response.Content.ReadAsByteArrayAsync();
                    string responseText = Encoding.Unicode.GetString(responseBytes);
                    return responseText;
                }
                else
                {
                    throw new Exception($"Tally server returned status {response.StatusCode}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError("TallyService.PostTallyXmlAsync()", 
                    "Unable to connect with Tally. Ensure tally XML port is enabled. " + ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("TallyService.PostTallyXmlAsync()", ex);
                throw;
            }
        }

        public async Task<(int MasterId, int TransactionId)> GetLastAlterIdsAsync()
        {
            try
            {
                string xmlPayload = "<?xml version=\"1.0\" encoding=\"utf-8\"?><ENVELOPE><HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST><TYPE>Data</TYPE><ID>MyReport</ID></HEADER><BODY><DESC><STATICVARIABLES><SVEXPORTFORMAT>ASCII (Comma Delimited)</SVEXPORTFORMAT></STATICVARIABLES><TDL><TDLMESSAGE><REPORT NAME=\"MyReport\"><FORMS>MyForm</FORMS></REPORT><FORM NAME=\"MyForm\"><PARTS>MyPart</PARTS></FORM><PART NAME=\"MyPart\"><LINES>MyLine</LINES><REPEAT>MyLine : MyCollection</REPEAT><SCROLLED>Vertical</SCROLLED></PART><LINE NAME=\"MyLine\"><FIELDS>FldAlterMaster,FldAlterTransaction</FIELDS></LINE><FIELD NAME=\"FldAlterMaster\"><SET>$AltMstId</SET></FIELD><FIELD NAME=\"FldAlterTransaction\"><SET>$AltVchId</SET></FIELD><COLLECTION NAME=\"MyCollection\"><TYPE>Company</TYPE><FILTER>FilterActiveCompany</FILTER></COLLECTION><SYSTEM TYPE=\"Formulae\" NAME=\"FilterActiveCompany\">$$IsEqual:##SVCurrentCompany:$Name</SYSTEM></TDLMESSAGE></TDL></DESC></BODY></ENVELOPE>";

                if (!string.IsNullOrEmpty(_company))
                {
                    xmlPayload = xmlPayload.Replace("##SVCurrentCompany", $"\"{EscapeHtml(_company)}\"");
                }

                string response = await PostTallyXmlAsync(xmlPayload);

                if (string.IsNullOrEmpty(response))
                {
                    if (string.IsNullOrEmpty(_company))
                    {
                        _logger.LogMessage("No company open in Tally");
                        throw new Exception("Please select one or more company in Tally to sync data");
                    }
                    else
                    {
                        _logger.LogMessage($"Specified company \"{_company}\" is closed in Tally");
                        throw new Exception("Please select target company in Tally to sync data");
                    }
                }

                // Parse response: "alterId1,alterId2"
                string[] parts = response.Replace("\"", "").Split(',');
                int masterId = parts.Length >= 1 && int.TryParse(parts[0], out int m) ? m : 0;
                int transactionId = parts.Length >= 2 && int.TryParse(parts[1], out int t) ? t : 0;

                return (masterId, transactionId);
            }
            catch (Exception ex)
            {
                _logger.LogError("TallyService.GetLastAlterIdsAsync()", ex);
                throw;
            }
        }

        private string EscapeHtml(string text)
        {
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
